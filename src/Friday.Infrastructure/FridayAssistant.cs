using System.Diagnostics;
using System.Globalization;
using System.Text;
using Friday.Core;

namespace Friday.Infrastructure;

public enum AssistantState
{
    Starting,
    Idle,
    Listening,
    Thinking,
    Speaking,
    Error
}

public sealed record AssistantStatus(AssistantState State, string Message);

/// <summary>
/// Sesli turu yönetir: kısayol → mikrofon açılır → konuşma bitince kapanır → Whisper yazıya döker → yerel dil
/// modeli cevaplar (gerekirse araç kullanır) → Piper cümle cümle seslendirir → kısa bir süre devam sorusu beklenir.
/// </summary>
public sealed class FridayAssistant : IDisposable
{
    private const int MaxToolRounds = 4;
    private static readonly HashSet<string> SlowTools =
        ["internette_ara", "sayfayi_oku", "ekrana_bak", "sistem_durumu", "dosya_bul", "pano", "gunluk_ozet", "video_ac"];

    private static readonly string[] Fillers = ["Hemen bakıyorum.", "Bir saniye, bakıyorum.", "Hemen kontrol ediyorum.", "Bakayım."];
    private static readonly TimeSpan FirstListenTimeout = TimeSpan.FromSeconds(5);

    private readonly FridayPaths _paths;
    private readonly MicrophoneRecorder _microphone;
    private readonly WhisperTranscriber _whisper;
    private readonly OllamaServer _ollama;
    private readonly OllamaChatClient _chat;
    private readonly Speaker _speaker;
    private readonly AssistantTools _tools;
    private readonly MemoryStore _longTermMemory;
    private readonly PrivacyLedger _ledger;
    private readonly ChatStore _chats;
    private readonly WakeWordListener _wakeWord;
    private readonly RhythmCoach _rhythm;
    private readonly object _gate = new();

    private FridaySettings _settings = new();
    private ConversationMemory _memory = new(TimeSpan.FromMinutes(10));
    private CancellationTokenSource? _turn;
    private CancellationTokenSource? _stopListening;
    private Task _currentTurn = Task.CompletedTask;
    private Timer? _idleTimer;
    private OpenWindow? _activeWindow;
    private string? _wakeDevice;
    private CancellationTokenSource? _announcement;

    /// <summary>Başka bir sohbete geçildi; bir sonraki turda sohbet hafızası bu turlarla yeniden kurulur.</summary>
    private IReadOnlyList<ConversationTurn>? _reload;

    /// <summary>Friday meşgulken gelen duyurular (hatırlatma, nöbet haberi); boşa düşünce sırayla okunur, kaybolmaz.</summary>
    private readonly Queue<IReadOnlyList<CoachStep>> _queued = new();

    /// <summary>Bu turda o ana kadar gösterilen cevap; tur yarıda kesilirse sohbete bu kadarı yazılır.</summary>
    private string _shown = "";

    /// <summary>"Teşekkürler", "bu kadar": devam sorusu beklenmez, konuşma biter.</summary>
    private bool _endConversation;

    /// <summary>"Dikte modu": bu turdan sonra söylenenler etkin pencereye yazılır.</summary>
    private bool _dictation;

    /// <summary>Son söylenen cevap ("tekrar et" için).</summary>
    private string _lastReply = "";

    /// <summary>Alışkanlık önerilerini 5 dk'da bir yoklar.</summary>
    private Timer? _habitTimer;

    /// <summary>Zamanlanmış rutin varken 20 sn'de bir bakar; yoksa hiç çalışmaz.</summary>
    private Timer? _routineTimer;

    /// <summary>Rutinlerin bugün en son ne zaman kendiliğinden çalıştığı (aynı saatte iki kez çalışmasın).</summary>
    private readonly Dictionary<string, DateTime> _routineRuns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Dil modelinin hazırlığı (sunucu, model, GPU'ya yükleme). Modeller yavaş bir diskteyse ilk açılışta bir dakikayı
    /// bulabilir; Friday bunu beklemeden hazır olur, hızlı komutlar hemen çalışır, modele giden soru hazırlığın bitmesini bekler.
    /// </summary>
    private Task _brainReady = Task.CompletedTask;

    private bool _yielding;

    /// <summary>Konuşmanın bittiği (ya da yazının gönderildiği) an; ilk cümle sese gidince gecikme ölçülür.</summary>
    private long _turnStart;

    private long _transcribeMs = -1;

    /// <summary>Telefondan gelen ve sessiz cevaplanacak tur.</summary>
    private volatile bool _quiet;

    private RemotePanel? _remote;

    /// <summary>Her tur bittiğinde: soru ve cevap.</summary>
    public event EventHandler<TurnResult>? TurnFinished;

    /// <summary>Son cevabın gecikmesi ölçüldü (Ayarlar'da gösterilir).</summary>
    public event EventHandler<string>? LatencyMeasured;

    /// <summary>Belge hafızasının durumu değişti.</summary>
    public event EventHandler<string>? DocumentsChanged;

    public string LastLatency { get; private set; } = "Henüz ölçülmedi; ilk sorudan sonra burada görünür.";

    public string DocumentStatus => _tools.Documents?.Status ?? "Kapalı";

    public Task ScanDocumentsAsync() => _tools.Documents?.ScanAsync() ?? Task.CompletedTask;

    /// <summary>Belge hafızasını ayara göre açar, kapatır ya da klasörlerini günceller.</summary>
    private void UpdateDocuments()
    {
        if (_settings.DocumentMemory && _tools.Documents is null)
        {
            var library = new DocumentLibrary(_paths.Data) { Folders = _settings.DocumentFolders };
            library.Changed += (_, status) => DocumentsChanged?.Invoke(this, status);
            library.Start();
            _tools.Documents = library;
            _ledger.Record(DataFlow.Local, "Belge hafızası", "Belgeler bu bilgisayarda dizinleniyor; hiçbir şey dışarı gönderilmiyor");
        }
        else if (!_settings.DocumentMemory && _tools.Documents is { } running)
        {
            _tools.Documents = null;
            running.Dispose();
            DocumentsChanged?.Invoke(this, "Kapalı");
        }
        else if (_tools.Documents is { } library)
        {
            library.Folders = _settings.DocumentFolders;
        }
    }

    /// <summary>Telefondan erişimi ayara göre açar ya da kapatır; anahtar yoksa üretir.</summary>
    private void UpdateRemote()
    {
        if (!_settings.RemoteAccess)
        {
            _remote?.Stop();
            return;
        }

        if (_settings.RemoteKey.Length < 8)
        {
            _settings.RemoteKey = RemotePanel.NewKey();
            _tools.SaveSettings?.Invoke(_settings);
        }

        _remote ??= new RemotePanel(AskRemoteAsync, () => [.. _settings.Routines.Keys]);
        try
        {
            _remote.Start(_settings.RemotePort, _settings.RemoteKey);
            _ledger.Record(DataFlow.Local, "Telefondan erişim", $"Ev ağında {_settings.RemotePort} numaralı kapıdan, gizli anahtarla açık");
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            FridayLog.Warn($"Telefondan erişim açılamadı (port {_settings.RemotePort}): {exception.Message}");
        }
    }

    /// <summary>Telefondan gelen mesaj: yazılmış gibi cevaplanır; istenmediyse bilgisayarda sesli okunmaz.</summary>
    public async Task<string> AskRemoteAsync(string text, bool speak, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Finished(object? sender, TurnResult result) => done.TrySetResult(result.Reply);
        TurnFinished += Finished;
        _quiet = !speak;
        _ledger.Record(DataFlow.Local, "Telefondan erişim", "Telefondan bir mesaj geldi");
        try
        {
            SubmitText(text);
            return await done.Task.WaitAsync(TimeSpan.FromSeconds(120), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return "Friday zamanında cevap veremedi; bilgisayarda başka bir işle meşgul olabilir.";
        }
        finally
        {
            TurnFinished -= Finished;
            _quiet = false;
        }
    }

    /// <summary>GPU'ya yüklenmiş sohbet modeli (Hızlı ↔ Akıllı geçişinde eskisi boşaltılır).</summary>
    private string _loadedModel = "";

    public FridayAssistant(
        FridayPaths paths,
        MicrophoneRecorder microphone,
        WhisperTranscriber whisper,
        OllamaServer ollama,
        OllamaChatClient chat,
        Speaker speaker,
        AssistantTools tools,
        MemoryStore longTermMemory,
        PrivacyLedger ledger,
        ChatStore chats,
        WakeWordListener wakeWord,
        RhythmCoach rhythm)
    {
        _paths = paths;
        _microphone = microphone;
        _whisper = whisper;
        _ollama = ollama;
        _chat = chat;
        _speaker = speaker;
        _tools = tools;
        _longTermMemory = longTermMemory;
        _ledger = ledger;
        _chats = chats;
        // Panelden başka bir sohbet açıldı, yeni sohbete geçildi ya da açık sohbet silindi: model o sohbetle devam eder.
        _chats.CurrentChanged += (_, _) => Volatile.Write(ref _reload, _chats.CurrentTurns);
        _wakeWord = wakeWord;
        _wakeWord.Detected += (_, device) => OnWake(device);
        _wakeWord.Problem += (_, problem) => FridayLog.Warn($"Uyandırma kelimesi: {problem}");
        _rhythm = rhythm;
        _tools.Rhythm = rhythm;
        // Koç yalnızca Friday boştayken konuşur; konuşma ya da dinleme sırasında öneri bir sonraki yoklamaya kalır.
        _rhythm.CanSpeak = () => State is AssistantState.Idle or AssistantState.Error;
        _rhythm.Speak += (_, script) => Announce(script);
        _rhythm.MorningGreeting += (_, _) => _ = GreetAsync();
        _tools.TimerElapsed += (_, message) => Announce(message);
        _tools.CaptureFolder = Path.Combine(paths.Data, "captures");
        _tools.SaveFolder = Path.Combine(paths.Home, "Kaydedilenler");
        _tools.Workspaces = new WorkspaceStore(Path.Combine(paths.Data, "workspaces.json"));
        _tools.SettingsChanged += (_, _) => ScheduleRoutines();
        _tools.ReportFolder = Path.Combine(paths.Data, "reports");
        _tools.UndoFile = Path.Combine(paths.Data, "downloads-undo.json");
        _speaker.FellBack += (_, reason) =>
            _ledger.Record(DataFlow.Local, "Ses", $"Azure kullanılamadı, yerel Piper'a geçildi ({reason})");
    }

    /// <summary>Kullanılan ses motoru (ör. "Azure nöral ses (Emel)").</summary>
    public string VoiceLabel => _speaker.EngineLabel;

    /// <summary>Panelden ayarlar kaydedilince ses motorunu ve uyandırma kelimesini yeni ayarlarla kurar.</summary>
    public void ApplySettings()
    {
        ScheduleRoutines();
        UpdateActivity();
        UpdateDocuments();
        UpdateRemote();
        if (_settings.OllamaModel != _loadedModel && _brainReady.IsCompleted)
        {
            // Akıl değişti (Hızlı ↔ Akıllı): eski model GPU'dan çıkar, yenisi arkada hazırlanır.
            var previous = _loadedModel;
            _brainReady = SwitchBrainAsync(previous, _settings.OllamaModel);
            _tools.BrainReady = _brainReady;
        }

        _memory.Expiry = TimeSpan.FromMinutes(Math.Max(1, _settings.ConversationMemoryMinutes));
        _tools.Calendar.Configure(_settings.CalendarUrl);
        _tools.Clipboard.Enable(_settings.ClipboardHistory);
        _chat.KeepAlive = $"{Math.Max(1, _settings.GpuIdleMinutes)}m";
        ReconfigureVoice();
        StartWakeWord();
        _rhythm.Apply(_settings);
    }

    /// <summary>Uyandırma kelimesi açıksa bekleme metni bunu hatırlatır.</summary>
    /// <summary>Durum satırında görünen akıl modu (teknik ayrıntılar günlükte ve Ayarlar'da).</summary>
    private string BrainLabel => _settings.OllamaModel == FridaySettings.SmartTextModel ? "akıllı mod" : "hızlı mod";

    private string ReadyText => _wakeWord.IsRunning ? $"Hazır · \"{WakeWordLabel}\" de ya da {_settings.Hotkey}" : "Hazır";

    private string WakeWordLabel =>
        CultureInfo.GetCultureInfo("tr-TR").TextInfo.ToTitleCase(_settings.WakeWord.Replace('_', ' '));

    /// <summary>Uyandırma kelimesini ayara göre açar, yeniden başlatır ya da kapatır.</summary>
    private void StartWakeWord()
    {
        if (_settings.WakeWord.Length == 0)
        {
            _wakeWord.Stop();
            return;
        }

        if (!WakeWordListener.IsInstalled(_paths.WakeWordModels, _settings.WakeWord))
        {
            FridayLog.Warn($"Uyandırma kelimesi modeli yok: {_paths.WakeWordModels}. setup.ps1'i çalıştırın.");
            return;
        }

        try
        {
            _wakeWord.Start(_settings.WakeWord, _settings.WakeWordThreshold, _settings.MicrophoneDevice);
            _ledger.Record(DataFlow.Local, "Mikrofon",
                $"\"{WakeWordLabel}\" bekleniyor · ses 80 ms'lik parçalar halinde yalnızca bellekte işlenip atılıyor, kaydedilmiyor");
        }
        catch (Exception exception) when (exception is Microsoft.ML.OnnxRuntime.OnnxRuntimeException or IOException or InvalidOperationException)
        {
            FridayLog.Error("Uyandırma kelimesi başlatılamadı", exception);
        }
    }

    /// <summary>Panelden ses ayarları değişince ses motorunu yeni ayarlarla kurar.</summary>
    public void ReconfigureVoice()
    {
        var voice = string.IsNullOrWhiteSpace(_settings.VoiceModelPath) ? _paths.DefaultVoice : _settings.VoiceModelPath;
        _speaker.Stop();
        _speaker.Configure(_settings, voice);
        _ = _speaker.WarmUpAsync(CancellationToken.None);
        Report(State, $"Ses ayarlandı · {_speaker.EngineLabel}");
    }

    /// <summary>
    /// Panelde "Dinle"ye basılınca seçilen XTTS sesine geçer ve kısa bir cümle okur. Sunucu yeniden başlamaz;
    /// kalıcı olması için panelden kaydedilir.
    /// </summary>
    public void PreviewVoice(string speaker)
    {
        _settings.XttsSpeaker = speaker;
        var voice = string.IsNullOrWhiteSpace(_settings.VoiceModelPath) ? _paths.DefaultVoice : _settings.VoiceModelPath;
        _speaker.Stop();
        _speaker.Configure(_settings, voice);
        Announce($"Merhaba, ben {_settings.AssistantName}. Sesim böyle; beğendiysen kaydedebilirsin.");
    }

    /// <summary>Okunan metin Azure'a gittiyse gizlilik defterine tek satır yazılır (mikrofon sesi gitmez).</summary>
    private void RecordOnlineSpeech()
    {
        var characters = _speaker.TakeOnlineCharacters();
        if (characters > 0)
        {
            _ledger.Record(DataFlow.Outbound, "Ses (Azure)",
                $"Cevabın {characters} karakterlik metni nöral sese çevrilmek üzere Microsoft'a gönderildi · ses kaydı gitmedi");
        }
    }

    public event EventHandler<AssistantStatus>? StatusChanged;

    /// <summary>Kullanıcının söylediği (yazıya dökülmüş) ya da yazdığı cümle.</summary>
    public event EventHandler<string>? UserSaid;

    /// <summary>Asistanın cevabı; akarken birden çok kez, her seferinde o ana kadarki tam metinle gelir.</summary>
    public event EventHandler<string>? AssistantSaid;

    /// <summary>Her model çağrısının ölçümleri (gecikme teşhisi için).</summary>
    public event EventHandler<string>? Diagnostics;

    /// <summary>Bir araç çalışmaya başladı (araç adı).</summary>
    public event EventHandler<string>? ToolStarted;

    /// <summary>Cevabın görsel kartı (hava, döviz, durum raporu…); kapsülün üstünde gösterilir.</summary>
    public event EventHandler<AnswerCard>? CardShown;

    /// <summary>Mikrofona gelen sesin seviyesi (0–1).</summary>
    public event EventHandler<float>? MicrophoneLevel
    {
        add => _microphone.LevelChanged += value;
        remove => _microphone.LevelChanged -= value;
    }

    /// <summary>Friday'in o an çıkardığı sesin seviyesi (0–1).</summary>
    public event EventHandler<float>? OutputLevel
    {
        add => _speaker.LevelChanged += value;
        remove => _speaker.LevelChanged -= value;
    }

    public AssistantState State { get; private set; } = AssistantState.Starting;

    public FridaySettings Settings => _settings;

    public async Task InitializeAsync(FridaySettings settings, CancellationToken cancellationToken)
    {
        _settings = settings;
        _tools.Settings = settings;
        _memory = new ConversationMemory(TimeSpan.FromMinutes(settings.ConversationMemoryMinutes));

        var clock = Stopwatch.StartNew();
        Report(AssistantState.Starting, "Konuşma tanıma ve ses hazırlanıyor…");
        var voice = string.IsNullOrWhiteSpace(settings.VoiceModelPath) ? _paths.DefaultVoice : settings.VoiceModelPath;
        _speaker.Configure(settings, voice);
        _chat.KeepAlive = $"{Math.Max(1, settings.GpuIdleMinutes)}m";
        _whisper.Configure(_paths.ResolveWhisperModel(settings.WhisperModelPath), settings);

        // Önce dinlemek ve konuşmak için gerekenler (Whisper, Piper) yüklenir; dil modeli (~5 GB) onlardan sonra diskten
        // okunur. Yavaş bir diskte ikisi aynı anda okunursa ikisi de gecikir.
        var voiceReady = Task.WhenAll(_whisper.EnsureLoadedAsync(cancellationToken), _speaker.WarmUpAsync(cancellationToken));
        _brainReady = PrepareBrainAsync(settings, voiceReady, clock, cancellationToken);
        _tools.BrainReady = _brainReady;
        _tools.Calendar.Configure(settings.CalendarUrl);
        await voiceReady.ConfigureAwait(false);
        FridayLog.Info($"Konuşma tanıma ve ses hazır ({clock.Elapsed.TotalSeconds:0.0} sn) · Whisper {_whisper.RuntimeLabel} · {_speaker.EngineLabel}");

        _idleTimer = new Timer(_ => _ = SleepModelsAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _habitTimer = new Timer(_ => SuggestHabit(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        ScheduleRoutines();
        _ = RunStartupRoutinesAsync();
        UpdateActivity();
        UpdateDocuments();
        UpdateRemote();
        _tools.Clipboard.Enable(settings.ClipboardHistory);
        StartWakeWord();
        _rhythm.Apply(settings);
        _tools.StartReminders();
        Report(AssistantState.Idle, _brainReady.IsCompleted
            ? $"{ReadyText} · {BrainLabel}"
            : $"{ReadyText} · dil modeli yükleniyor, hızlı komutlar hazır");
    }

    /// <summary>Ollama'yı başlatır, sohbet modelini hazırlar ve ses hazır olunca GPU'ya yükler.</summary>
    private async Task PrepareBrainAsync(FridaySettings settings, Task voiceReady, Stopwatch clock, CancellationToken cancellationToken)
    {
        try
        {
            await _ollama.EnsureRunningAsync(settings.OllamaPort, cancellationToken).ConfigureAwait(false);
            await _ollama.EnsureTextModelAsync(settings.OllamaModel, settings.TextModelSource(), cancellationToken).ConfigureAwait(false);
            try
            {
                await voiceReady.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Ses tarafının hatasını InitializeAsync bildirir; dil modeli yine yüklenir.
            }

            await _chat.WarmUpAsync(settings.OllamaModel, cancellationToken).ConfigureAwait(false);
            _loadedModel = settings.OllamaModel;
            FridayLog.Info($"Dil modeli hazır ({clock.Elapsed.TotalSeconds:0.0} sn)");
            lock (_gate)
            {
                if (State != AssistantState.Idle)
                {
                    return;
                }
            }

            Report(AssistantState.Idle, $"{ReadyText} · {BrainLabel}");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or TimeoutException)
        {
            // Soru geldiğinde EnsureRunningAsync yeniden dener ve hatayı kullanıcıya söyler.
            FridayLog.Error("Dil modeli hazırlanamadı", exception);
        }
    }

    /// <summary>Sohbet modeli Hızlı ↔ Akıllı arasında değişti.</summary>
    private async Task SwitchBrainAsync(string previous, string next)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            if (previous.Length > 0)
            {
                await _chat.UnloadAsync(previous, CancellationToken.None).ConfigureAwait(false);
            }

            await _ollama.EnsureRunningAsync(_settings.OllamaPort, CancellationToken.None).ConfigureAwait(false);
            await _ollama.EnsureTextModelAsync(next, _settings.TextModelSource(), CancellationToken.None).ConfigureAwait(false);
            await _chat.WarmUpAsync(next, CancellationToken.None).ConfigureAwait(false);
            _loadedModel = next;
            FridayLog.Info($"Sohbet modeli {next} oldu ({clock.Elapsed.TotalSeconds:0.0} sn)");
            lock (_gate)
            {
                if (State != AssistantState.Idle)
                {
                    return;
                }
            }

            Report(AssistantState.Idle, $"{ReadyText} · {BrainLabel}");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or TimeoutException)
        {
            FridayLog.Error($"Sohbet modeli {next} hazırlanamadı", exception);
        }
    }

    /// <summary>Dil modeli daha yükleniyorsa (ilk açılış) bekler; beklerken ne olduğunu söyler.</summary>
    private async Task WaitForBrainAsync(CancellationToken cancellationToken)
    {
        if (_brainReady.IsCompleted)
        {
            return;
        }

        Report(AssistantState.Thinking, "Dil modeli yükleniyor (ilk açılış)…");
        try
        {
            await _brainReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or TimeoutException)
        {
            // EnsureRunningAsync yeniden dener.
        }

        Report(AssistantState.Thinking, "Düşünüyor…");
    }

    /// <summary>
    /// Kısayola basılınca modeller uyuyorsa hemen yüklenmeye başlar. Yükleme (~3 sn) kullanıcı konuşurken biter,
    /// bu yüzden boştayken VRAM'i boşaltmanın bedeli neredeyse hissedilmez.
    /// </summary>
    private void Prewarm()
    {
        _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _speaker.Prewarm();
        _ = Task.Run(async () =>
        {
            try
            {
                // İlk açılışta dil modeli zaten yükleniyorsa ikinci bir yükleme başlatılmaz.
                await Task.WhenAll(
                    _brainReady.IsCompleted ? _chat.WarmUpAsync(_settings.OllamaModel, CancellationToken.None) : Task.CompletedTask,
                    _whisper.EnsureLoadedAsync(CancellationToken.None)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                // Asıl istek hatayı yeniden yakalar ve kullanıcıya gösterir.
            }
        });
    }

    /// <summary>Boşta kalınca Whisper'ı GPU'dan çıkarır; dil modelini Ollama keep_alive süresi dolunca kendisi çıkarır.</summary>
    private async Task SleepModelsAsync()
    {
        lock (_gate)
        {
            if (State is not (AssistantState.Idle or AssistantState.Error) || !_whisper.IsLoaded)
            {
                return;
            }
        }

        await Task.WhenAll(_whisper.UnloadAsync(), _speaker.SleepAsync()).ConfigureAwait(false);
        // Modellerden kalan yönetilen ve yerel bellek işletim sistemine geri verilir; boştayken Friday birkaç on MB'a iner.
        MemoryTrim.Now();
        Report(AssistantState.Idle, $"{ReadyText} · modeller uykuda");
    }

    /// <summary>
    /// "Hey Friday" duyuldu: kısayola basılmış gibi davranır. Friday konuşuyor ya da düşünüyorsa sözü kesilir ve
    /// dinlemeye geçilir; zaten dinliyorsa bir şey yapılmaz. Kayıt yalnızca kelimeyi duyan mikrofonla yapılır.
    /// </summary>
    private void OnWake(string device)
    {
        lock (_gate)
        {
            if (State is AssistantState.Starting or AssistantState.Listening)
            {
                return;
            }
        }

        FridayLog.Info($"Uyandırma kelimesi duyuldu ({device})");
        StartTurn(text: null, device);
    }

    /// <summary>
    /// Kısayola basıldı. Boştaysa dinlemeye başlar; dinliyorsa dinlemeyi bitirir; konuşuyor ya da düşünüyorsa
    /// keser ve hemen yeniden dinler.
    /// </summary>
    public void OnHotkey()
    {
        lock (_gate)
        {
            if (State == AssistantState.Listening)
            {
                _stopListening?.Cancel();
                return;
            }
        }

        StartTurn(text: null);
    }

    /// <summary>Panodan yazılan soru: konuşma yerine yazıyla aynı tur.</summary>
    public void SubmitText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            StartTurn(text.Trim());
        }
    }

    private void StartTurn(string? text, string? device = null)
    {
        lock (_gate)
        {
            if (State == AssistantState.Starting)
            {
                return;
            }

            _wakeDevice = device;
            _announcement?.Cancel();

            if (State is AssistantState.Thinking or AssistantState.Speaking or AssistantState.Listening)
            {
                _turn?.Cancel();
                _stopListening?.Cancel();
                _speaker.Stop();
            }

            Prewarm();
            // Kısayola basıldığı andaki pencere: "bunu özetle" gibi isteklerin neyi kastettiği buradan anlaşılır.
            _activeWindow = Desktop.Foreground();
            var previous = _currentTurn;
            _turn?.Dispose();
            _turn = new CancellationTokenSource();
            _stopListening?.Dispose();
            _stopListening = new CancellationTokenSource();
            var token = _turn.Token;
            var stop = _stopListening.Token;
            State = text is null ? AssistantState.Listening : AssistantState.Thinking;
            _currentTurn = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                await RunTurnAsync(text, stop, token).ConfigureAwait(false);
            });
        }
    }

    private async Task RunTurnAsync(string? typed, CancellationToken stopListening, CancellationToken cancellationToken)
    {
        try
        {
            if (typed is not null)
            {
                await AskAsync(typed, cancellationToken).ConfigureAwait(false);
                if (_dictation)
                {
                    await DictationAsync(stopListening, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            // İlk dinlemeden sonra, ayar açıksa birkaç saniye devam sorusu beklenir; tuşa yeniden basmak gerekmez.
            var followUp = false;
            while (true)
            {
                var text = await ListenAsync(followUp, stopListening, cancellationToken).ConfigureAwait(false);
                if (text is null)
                {
                    return;
                }

                await AskAsync(text, cancellationToken).ConfigureAwait(false);
                if (_dictation)
                {
                    await DictationAsync(stopListening, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (_settings.FollowUpSeconds <= 0 || stopListening.IsCancellationRequested || _endConversation)
                {
                    _endConversation = false;
                    return;
                }

                followUp = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Kısayola tekrar basılıp kesildi.
        }
        catch (Exception exception)
        {
            FridayLog.Error("Tur yarıda kaldı", exception);
            Report(AssistantState.Error, exception.Message);
        }
    }

    /// <returns>Yazıya dökülmüş konuşma; konuşulmadıysa null.</returns>
    private async Task<string?> ListenAsync(bool followUp, CancellationToken stopListening, CancellationToken cancellationToken,
        TimeSpan? wait = null, string? prompt = null)
    {
        var device = _microphone.LastDeviceName.Length > 0 ? $" · {_microphone.LastDeviceName}" : "";
        Report(AssistantState.Listening, (prompt ?? (followUp ? "Devam edebilirsin…" : "Dinliyorum…")) + device);
        AudioCues.PlayListening();
        var opened = Stopwatch.StartNew();
        var timeout = wait ?? (followUp ? TimeSpan.FromSeconds(_settings.FollowUpSeconds) : FirstListenTimeout);
        var recording = await _microphone.RecordAsync(_settings, timeout, stopListening, cancellationToken, _wakeDevice, SpeculateAsync)
            .ConfigureAwait(false);
        var audio = recording.Audio;
        AudioCues.PlayDone();
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        _ledger.Record(DataFlow.Local, "Mikrofon",
            $"{_microphone.LastDeviceName} · {opened.Elapsed.TotalSeconds.ToString("0.0", turkish)} sn açık kaldı · " +
            $"en yüksek seviye %{(_microphone.LastPeak * 100).ToString("0", turkish)} · ses bellekte işlendi, kaydedilmedi");

        if (audio.Length == 0)
        {
            // Hiç sinyal gelmediyse sorun büyük ihtimalle mikrofon seçimidir; kullanıcıya bunu söylemek işe yarar.
            var hint = _microphone.LastPeak < 0.001f
                ? $"{_microphone.LastDeviceName} hiç ses vermiyor; Ayarlar'dan mikrofonu seç."
                : "Bir şey duymadım.";
            Report(AssistantState.Idle, followUp ? ReadyText : hint);
            return null;
        }

        Report(AssistantState.Thinking, "Anlamaya çalışıyorum…");
        Interlocked.Exchange(ref _turnStart, Stopwatch.GetTimestamp());
        var clock = Stopwatch.StartNew();
        var text = recording.Transcript ?? await _whisper.TranscribeAsync(audio, cancellationToken).ConfigureAwait(false);
        _transcribeMs = clock.ElapsedMilliseconds;
        Diagnostics?.Invoke(this, string.Create(CultureInfo.InvariantCulture,
            $"yazıya dökme: {(recording.Transcript is null ? $"{clock.ElapsedMilliseconds} ms" : "tahmin hazırdı, 0 ms")}"));
        if (text.Length == 0)
        {
            Interlocked.Exchange(ref _turnStart, 0);
            Report(AssistantState.Idle, "Anlayamadım, tekrar dener misin?");
            return null;
        }

        return text;
    }

    /// <summary>
    /// Kullanıcı yarım saniye susunca o ana kadarki ses yazıya dökülür. Yazı tamamlanmış bir komut ya da soruysa kayıt
    /// 1,2 sn'lik sessizliği beklemeden biter; değilse yazı hazır bekler ve kullanıcı devam etmezse yeniden dökülmez.
    /// </summary>
    private async Task<(string Text, bool Complete)> SpeculateAsync(float[] audio, CancellationToken cancellationToken)
    {
        var text = await _whisper.TranscribeAsync(audio, cancellationToken).ConfigureAwait(false);
        var complete = text.Length > 0
            && (QuickIntents.Match(text, _settings.Routines.Keys, _settings.Triggers.Keys) is { Tentative: false } || text.TrimEnd().EndsWith('?'));
        return (text, complete);
    }

    /// <summary>
    /// Kalıbı belli komutlarda (ör. "YouTube'da X aç") araç modelsiz çalışır; model böyle komutlarda aracı çağırmadan
    /// önce saniyelerce düşünebiliyor. Aracın sonucu doğrudan okunur ve sohbet hafızasına normal bir tur gibi yazılır.
    /// </summary>
    /// <returns>Söylenen cevap; komut hızlı yolda yapılamadıysa null (soru dil modeline gider).</returns>
    private async Task<string?> RunQuickAsync(QuickCommand quick, CancellationToken cancellationToken)
    {
        var call = new ToolCall(quick.Tool, quick.Arguments);
        var clock = Stopwatch.StartNew();
        if (quick.Tool == "secili")
        {
            // Seçili metni yerel dil modeli işler; ilk açılışta model daha yükleniyorsa beklenir.
            await WaitForBrainAsync(cancellationToken).ConfigureAwait(false);
        }

        var work = _tools.RunQuickAsync(quick, cancellationToken);
        if (!quick.Tentative)
        {
            Report(AssistantState.Thinking, AssistantTools.DescribeActivity(call.Name) + "…");
            ToolStarted?.Invoke(this, call.Name);
        }

        // Çoğu hızlı cevap 50 ms'de hazır; internete çıkan biri uzarsa sessiz beklemek yerine kısa bir söz.
        if (await Task.WhenAny(work, Task.Delay(700, cancellationToken)).ConfigureAwait(false) != work)
        {
            Say(Fillers[Random.Shared.Next(Fillers.Length)]);
        }

        if (await work.ConfigureAwait(false) is not { } reply)
        {
            return null;
        }

        if (reply.Card is { } card)
        {
            CardShown?.Invoke(this, card);
        }

        Diagnostics?.Invoke(this, string.Create(CultureInfo.InvariantCulture, $"hızlı yol: {call.Name} {clock.ElapsedMilliseconds} ms"));
        _memory.Add(new ChatMessage(ChatRole.Assistant, "") { ToolCalls = [call] });
        _memory.Add(new ChatMessage(ChatRole.Tool, reply.Text) { ToolName = call.Name });
        _memory.Add(new ChatMessage(ChatRole.Assistant, reply.Text));
        if (reply.Script is { } script)
        {
            await PlayScriptAsync(script, cancellationToken).ConfigureAwait(false);
            return string.Join(" ", script.Select(step => step.Text));
        }

        Show(reply.Text);
        if (reply.Silent)
        {
            AudioCues.PlayDone();
        }
        else
        {
            Say(reply.Text);
        }

        return reply.Text;
    }

    /// <summary>
    /// Panelden bir sohbet açar (null: yeni, boş sohbet). Konuşuyorsa susar; sonraki soru o sohbetin bağlamıyla cevaplanır.
    /// </summary>
    public void OpenChat(string? id)
    {
        lock (_gate)
        {
            if (State is AssistantState.Thinking or AssistantState.Speaking)
            {
                _turn?.Cancel();
                _speaker.Stop();
            }
        }

        if (id is null)
        {
            _chats.NewChat();
        }
        else
        {
            _chats.Open(id);
        }
    }

    /// <summary>Yazıya dökülmüş bir soruyu cevaplar ve cevabın okunması bitene kadar bekler.</summary>
    public async Task AskAsync(string text, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _turnStart, Stopwatch.GetTimestamp(), 0) == 0)
        {
            _transcribeMs = -1;
        }

        if (Interlocked.Exchange(ref _reload, null) is { } turns)
        {
            _memory.Load(turns);
        }
        else if (_memory.BeginTurn())
        {
            // Uzun bir sessizlikten sonra gelen soru yeni bir sohbettir; panel önce temizlenir, sonra soru görünür.
            _chats.NewChat();
            Volatile.Write(ref _reload, null);
        }

        // Bekleyen bir soruya ("İndirilenleri düzenleyeyim mi?") verilen kısa "evet" ya da "hayır" önce ona gider.
        var command = _tools.HasPendingConfirmation && (QuickIntents.IsYes(text) || QuickIntents.IsNo(text))
            ? new QuickCommand("onay", new Dictionary<string, string> { ["cevap"] = QuickIntents.IsYes(text) ? "evet" : "hayir" })
            : QuickIntents.Match(text, _settings.Routines.Keys, _settings.Triggers.Keys);
        if (command is { Tool: "sohbet" })
        {
            ChatCommand(command.Arguments.TryGetValue("islem", out var action) ? action : "yeni");
            await _speaker.WaitUntilDoneAsync(cancellationToken).ConfigureAwait(false);
            Report(AssistantState.Idle, ReadyText);
            return;
        }

        if (command is { Tool: "tekrar" })
        {
            // "Tekrar et", "ne dedin": son cevap yeniden okunur; sohbete yazılmaz.
            var again = _lastReply.Length > 0 ? _lastReply : "Henüz bir şey söylemedim.";
            Report(AssistantState.Speaking, "Tekrar");
            Show(again);
            Say(again);
            await _speaker.WaitUntilDoneAsync(cancellationToken).ConfigureAwait(false);
            Report(AssistantState.Idle, ReadyText);
            return;
        }

        if (command is { Tool: "dikte" })
        {
            _dictation = true;
            Report(AssistantState.Speaking, "Dikte modu");
            Say("Dikte modu açık. Söylediklerini yazıyorum; bitirince dikte bitti de.");
            await _speaker.WaitUntilDoneAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (command is { Tool: "kapanis" })
        {
            // Teşekküre kısa bir karşılık; devam sorusu beklenmez.
            _endConversation = true;
            var address = _settings.AddressAs.Trim();
            string[] replies = ["Rica ederim.", "Ne demek.", "Her zaman.", "Ne zaman istersen."];
            var answer = replies[Random.Shared.Next(replies.Length)];
            if (address.Length > 0 && Random.Shared.Next(2) == 0)
            {
                answer = answer.TrimEnd('.') + $", {address}.";
            }

            Report(AssistantState.Speaking, answer);
            Say(answer);
            await _speaker.WaitUntilDoneAsync(cancellationToken).ConfigureAwait(false);
            Report(AssistantState.Idle, ReadyText);
            return;
        }

        UserSaid?.Invoke(this, text);
        Report(AssistantState.Thinking, "Düşünüyor…");
        _tools.LastUserText = text;
        _tools.ActiveWindow = _activeWindow;
        _shown = "";
        var chatAtStart = _chats.CurrentId;
        var context = _activeWindow is { } window ? $"\n[Bağlam: kullanıcı \"{window.Title}\" ({window.Process}) penceresindeydi]" : "";
        _memory.Add(new ChatMessage(ChatRole.User, text + context));
        string? reply;
        try
        {
            reply = command is { } quick
                ? await RunQuickAsync(quick, cancellationToken).ConfigureAwait(false)
                : null;
            if (reply is null)
            {
                // Ollama bir sebeple kapandıysa (çökme, elle kapatma) sessizce yeniden başlatılır; soru boşa gitmez.
                await WaitForBrainAsync(cancellationToken).ConfigureAwait(false);
                await _ollama.EnsureRunningAsync(_settings.OllamaPort, cancellationToken).ConfigureAwait(false);
                reply = await AnswerAsync(text, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_settings.KeepHistory && _chats.CurrentId == chatAtStart)
        {
            // Söz kesildi ya da Friday kapandı: soru kaybolmasın, o ana kadarki cevapla birlikte sohbete yazılır.
            _chats.Append(text, _shown.Length > 0 ? _shown.TrimEnd() + " …" : "(yarıda kesildi)");
            throw;
        }

        _memory.CompactToolResults();
        if (_settings.KeepHistory)
        {
            _chats.Append(text, reply);
        }

        TurnFinished?.Invoke(this, new TurnResult(text, reply));
        Interlocked.Exchange(ref _turnStart, 0);

        if (reply.Length > 0)
        {
            _lastReply = reply;
        }

        await _speaker.WaitUntilDoneAsync(cancellationToken).ConfigureAwait(false);
        RecordOnlineSpeech();
        Report(AssistantState.Idle, ReadyText);
    }

    /// <summary>
    /// Dikte: her konuşma parçası yazıya dökülüp etkin pencereye yazılır ("yeni satır" Enter, "geri al" Ctrl+Z).
    /// "Dikte bitti" denince, kısayola basılınca ya da 30 sn konuşulmayınca biter. Söylenenler sohbete yazılmaz.
    /// </summary>
    private async Task DictationAsync(CancellationToken stopListening, CancellationToken cancellationToken)
    {
        var target = _activeWindow;
        var written = 0;
        try
        {
            while (!stopListening.IsCancellationRequested)
            {
                var text = await ListenAsync(followUp: true, stopListening, cancellationToken, TimeSpan.FromSeconds(30),
                    written == 0 ? "Dikte · konuş, yazıyorum…" : $"Dikte · {written} parça yazıldı, devam et…").ConfigureAwait(false);
                if (text is null || QuickIntents.IsDictationEnd(text))
                {
                    break;
                }

                AssistantTools.Dictate(text, target);
                written++;
                Show($"Yazıldı: {text}");
            }
        }
        finally
        {
            _dictation = false;
            _ledger.Record(DataFlow.Local, "Dikte", $"{written} parça {target?.Process ?? "etkin pencere"} penceresine yazıldı · ses kaydedilmedi");
        }

        Say(written == 0 ? "Dikte kapandı." : "Dikte bitti.");
        await _speaker.WaitUntilDoneAsync(cancellationToken).ConfigureAwait(false);
        Report(AssistantState.Idle, ReadyText);
    }

    /// <summary>Cevabı arayüze gönderir ve turun o ana kadarki cevabı olarak saklar.</summary>
    private void Show(string text)
    {
        _shown = text;
        AssistantSaid?.Invoke(this, text);
    }

    /// <summary>"Yeni sohbet" ve "sohbeti temizle": sohbete yazılmaz, yalnızca söylenir.</summary>
    private void ChatCommand(string action)
    {
        string reply;
        if (action == "temizle" && _chats.CurrentId is { } current)
        {
            _chats.Delete(current);
            reply = "Bu sohbeti sildim, temiz bir sayfa açtım.";
        }
        else if (action == "temizle")
        {
            reply = "Sohbet zaten boş.";
        }
        else
        {
            _chats.NewChat();
            reply = "Yeni bir sohbet açtım. Dinliyorum.";
        }

        _memory.Clear();
        Volatile.Write(ref _reload, null);
        _ledger.Record(DataFlow.Local, "Sohbet", action == "temizle" ? "Açık sohbet silindi" : "Yeni sohbet açıldı");
        Report(AssistantState.Speaking, reply);
        Say(reply);
    }

    private async Task<string> AnswerAsync(string question, CancellationToken cancellationToken)
    {
        var reply = new StringBuilder();
        var retried = false;
        var filled = false;
        // Düşünme adımı yalnızca hangi aracın çağrılacağına karar verirken gerekir. Selamlaşma ve sohbette, ya da araç
        // sonucu geldikten sonra cevabı cümleye dökerken kapalıdır: bu ikisi cevap başına 0,5–2 sn kazandırıyor.
        var smallTalk = ConversationStyle.IsSmallTalk(question);
        // Ajan modu: "PDF'i bul, özetle, notlarıma ekle" gibi çok adımlı isteklerde daha çok adım, her adımda düşünme ve
        // önce planı söyleyip sonra sırayla yapma talimatı.
        var multiStep = !smallTalk && AgentPlan.IsMultiStep(question);
        var maxRounds = multiStep ? 8 : MaxToolRounds;
        var documents = !smallTalk && !multiStep && AgentPlan.MentionsDocuments(question)
            ? await Task.Run(() => _tools.DocumentContext(question), cancellationToken).ConfigureAwait(false)
            : null;
        var step = 0;
        if (multiStep)
        {
            FridayLog.Info("Ajan modu: çok adımlı istek");
        }

        for (var round = 0; round <= maxRounds; round++)
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt.Build(_settings, _longTermMemory.Items, DateTime.Now) + (multiStep ? "\n" + AgentPlan.Instruction : "") + (documents is null ? "" : "\n" + documents))
            };
            messages.AddRange(_memory.Messages);

            var chunker = new SentenceChunker();
            var content = new StringBuilder();
            var calls = new List<ToolCall>();
            // Son turda araç verilmez; model mutlaka sözlü bir cevap verir.
            var tools = round < maxRounds ? _tools.Definitions : [];
            // Akıllı model (12B) serbest isteklerde (fıkra, sohbet, öneri) düşünmeden cevap verir; düşünme orada 9 sn'ye
            // kadar ekliyordu. Kesin bilgi isteyen sorularda düşünme kalır: yoksa model aracı atlayıp sayı uydurabiliyor.
            // Küçük model düşünmeden aracı çağırmak yerine anlatmaya kaçtığı için onda ilk turda hep açıktır.
            var think = round == 0 && !smallTalk && documents is null
                        && (multiStep || _settings.OllamaModel != FridaySettings.SmartTextModel || AgentPlan.IsFactual(question));

            var thinking = 0;
            await foreach (var delta in _chat.StreamAsync(_settings.OllamaModel, messages, tools, cancellationToken, think).ConfigureAwait(false))
            {
                thinking += delta.ThinkingLength;
                if (delta.Timings is { } t)
                {
                    Diagnostics?.Invoke(this, string.Create(CultureInfo.InvariantCulture,
                        $"komut {t.PromptTokens} belirteç {t.PromptMs:0} ms · düşünce {thinking} karakter · üretim {t.GeneratedTokens} belirteç {t.GenerateMs:0} ms · yükleme {t.LoadMs:0} ms"));
                }

                calls.AddRange(delta.ToolCalls);
                if (delta.Content.Length == 0)
                {
                    continue;
                }

                content.Append(delta.Content);
                reply.Append(delta.Content);
                Show(reply.ToString());
                foreach (var sentence in chunker.Push(delta.Content))
                {
                    SayUnlessFiller(sentence);
                }
            }

            if (chunker.Flush() is { } rest)
            {
                SayUnlessFiller(rest);
            }

            // Model ara sıra yalnızca "düşünüp" ne metin ne araç çağrısı üretiyor; bir kez daha denenir.
            if (content.Length == 0 && calls.Count == 0)
            {
                if (!retried)
                {
                    retried = true;
                    round--;
                    continue;
                }

                const string fallback = "Bunu şu an yapamadım, bir daha söyler misin?";
                reply.Append(fallback);
                Show(reply.ToString());
                Say(fallback);
                return reply.ToString();
            }

            _memory.Add(new ChatMessage(ChatRole.Assistant, content.ToString()) { ToolCalls = calls });
            if (calls.Count == 0)
            {
                // Ekranda da kalıp sözler görünmesin; konuşmada zaten okunmadılar.
                var clean = ConversationStyle.RemoveFiller(reply.ToString());
                if (clean.Length > 0 && clean != reply.ToString())
                {
                    Show(clean);
                    return clean;
                }

                return reply.ToString();
            }

            foreach (var call in calls)
            {
                Report(AssistantState.Thinking, (multiStep ? $"{++step}. adım: " : "") + AssistantTools.DescribeActivity(call.Name) + "…");
                ToolStarted?.Invoke(this, call.Name);
                // Uzun süren işlerde sessiz beklemek yerine kısa bir cümle: gerçek bir asistan gibi "bakıyorum" der.
                if (!filled && reply.Length == 0 && SlowTools.Contains(call.Name))
                {
                    filled = true;
                    Say(Fillers[Random.Shared.Next(Fillers.Length)]);
                }

                var result = await _tools.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
                if (result.Length > 7500)
                {
                    // Bağlam penceresi (8192 belirteç) sabit: uzun araç sonuçları sistem talimatını dışarı itmesin.
                    result = result[..7500] + " …(kısaltıldı)";
                }

                if (_tools.TakeCard() is { } toolCard)
                {
                    CardShown?.Invoke(this, toolCard);
                }
                _memory.Add(new ChatMessage(ChatRole.Tool, result) { ToolName = call.Name });

                // Nefes egzersizi gibi rehberli metinler aralarında beklenerek okunur; tur burada biter.
                if (_tools.TakeScript() is { } script)
                {
                    await PlayScriptAsync(script, cancellationToken).ConfigureAwait(false);
                    var spoken = string.Join(" ", script.Select(step => step.Text));
                    _memory.Add(new ChatMessage(ChatRole.Assistant, spoken));
                    return reply.Append(spoken).ToString();
                }

                if (calls.Count == 1 && AssistantTools.AnswersDirectly(call.Name))
                {
                    // Görme modelinin cevabı doğrudan okunur; sohbet modeli arka planda yeniden yüklenir.
                    Prewarm();
                    reply.Append(result);
                    Show(reply.ToString());
                    var direct = new SentenceChunker();
                    foreach (var sentence in direct.Push(result + " "))
                    {
                        Say(sentence);
                    }

                    if (direct.Flush() is { } last)
                    {
                        Say(last);
                    }

                    _memory.Add(new ChatMessage(ChatRole.Assistant, result));
                    return reply.ToString();
                }
            }

            if (reply.Length > 0)
            {
                reply.Append(' ');
            }
        }

        return reply.ToString();
    }

    /// <summary>"Başka bir şeye ihtiyacın var mı?" gibi kalıp cümleler okunmaz; geri kalanı okunur.</summary>
    private void SayUnlessFiller(string sentence)
    {
        if (!ConversationStyle.IsFiller(sentence))
        {
            Say(sentence);
        }
    }

    private void Say(string sentence)
    {
        var spoken = Pronunciation.Apply(SpokenText.Clean(sentence), _settings.Pronunciations);
        if (spoken.Length == 0 || _quiet)
        {
            return;
        }

        // 45 sn'den eskiyse ölçüm yarıda kalmış bir turdan kalmıştır; gösterilmez.
        if (Interlocked.Exchange(ref _turnStart, 0) is var started and > 0 && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(45))
        {
            var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            var ms = _transcribeMs;
            LastLatency = ms >= 0
                ? string.Create(CultureInfo.GetCultureInfo("tr-TR"), $"Son cevap: konuşman bitince {seconds:0.0} sn'de söze başladı (yazıya dökme {ms} ms, gerisi düşünme).")
                : string.Create(CultureInfo.GetCultureInfo("tr-TR"), $"Son cevap: yazınca {seconds:0.0} sn'de söze başladı.");
            FridayLog.Info(string.Create(CultureInfo.InvariantCulture, $"Gecikme: ilk cümle {seconds:0.00} sn (yazıya dökme {ms} ms)"));
            LatencyMeasured?.Invoke(this, LastLatency);
        }

        if (State != AssistantState.Speaking)
        {
            Report(AssistantState.Speaking, "Konuşuyor…");
        }

        _speaker.Speak(spoken);
    }

    /// <summary>Zamanlayıcı gibi kendiliğinden gelen duyurular; o an başka bir iş yoksa okunur.</summary>
    private void Announce(string message) => Announce([new CoachStep(message)]);

    /// <summary>
    /// Kendiliğinden gelen konuşma (ritim önerisi, hoş geldin, zamanlayıcı). Friday boşta değilse söylenmez. Kısayola
    /// basılır ya da "Hey Friday" denirse hemen susar.
    /// </summary>
    /// <summary>Ekran süresi sayacını ayara göre açar ya da kapatır.</summary>
    private void UpdateActivity()
    {
        if (_settings.ScreenTime && _tools.Activity is null)
        {
            var activity = new ActivityTracker(Path.Combine(_paths.Data, "screentime.json"));
            activity.Distracted += (_, site) => OnDistracted(site);
            activity.Start();
            _tools.Activity = activity;
            _ledger.Record(DataFlow.Local, "Ekran süresi", "Öndeki uygulamanın adı 15 sn'de bir sayılıyor; pencere başlıkları ve içerikler saklanmıyor");
        }
        else if (!_settings.ScreenTime && _tools.Activity is { } running)
        {
            _tools.Activity = null;
            running.Dispose();
        }
    }

    /// <summary>Odak oturumunda dikkat dağıtan bir sitede kalındı: oturum başına bir kez, nazikçe.</summary>
    private void OnDistracted(string site)
    {
        if (!_settings.FocusGuard || _tools.Rhythm?.Snapshot() is not { FocusStart: { } start, FocusEnd: { } end } || end <= DateTime.Now
            || _guardedFocus == start)
        {
            return;
        }

        _guardedFocus = start;
        var left = Math.Max(1, (int)Math.Round((end - DateTime.Now).TotalMinutes));
        Announce($"Odak oturumundaydın, {site} açık. {left} dakika kaldı; ara vermek istersen \"odağı bitir\" demen yeter.");
    }

    private DateTime? _guardedFocus;

    /// <summary>Saati olan bir rutin varsa saati kurar, kalmadıysa durdurur.</summary>
    private void ScheduleRoutines()
    {
        var timed = _settings.RoutineSchedules.Values.Any(text => RoutineSchedule.Parse(text) is { OnStart: false });
        lock (_routineRuns)
        {
            if (timed && _routineTimer is null)
            {
                _routineTimer = new Timer(_ => RunDueRoutines(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));
            }
            else if (!timed && _routineTimer is not null)
            {
                _routineTimer.Dispose();
                _routineTimer = null;
            }
        }
    }

    /// <summary>Saati gelen rutini, rutinin adı söylenmiş gibi çalıştırır. Friday o an konuşuyorsa sonraki yoklamada.</summary>
    private void RunDueRoutines()
    {
        var now = DateTime.Now;
        foreach (var (name, text) in _settings.RoutineSchedules.ToArray())
        {
            if (!_settings.Routines.ContainsKey(name) || RoutineSchedule.Parse(text) is not { } schedule)
            {
                continue;
            }

            DateTime? last;
            lock (_routineRuns)
            {
                last = _routineRuns.TryGetValue(name, out var run) ? run : null;
            }

            if (!schedule.IsDue(now, last))
            {
                continue;
            }

            lock (_gate)
            {
                if (State is not AssistantState.Idle)
                {
                    return;
                }
            }

            lock (_routineRuns)
            {
                _routineRuns[name] = now;
            }

            FridayLog.Info($"Zamanlanmış rutin çalışıyor: {name} ({schedule.Describe()})");
            SubmitText(name);
            return;
        }
    }

    /// <summary>"Friday açılınca" rutinleri: açılış selamı ve duyurular bitip Friday boşa düşünce, sırayla bir kez.</summary>
    private async Task RunStartupRoutinesAsync()
    {
        var names = _settings.RoutineSchedules
            .Where(pair => _settings.Routines.ContainsKey(pair.Key) && RoutineSchedule.Parse(pair.Value) is { OnStart: true })
            .Select(pair => pair.Key)
            .ToList();
        foreach (var name in names)
        {
            var quiet = 0;
            for (var wait = 0; wait < 180 && quiet < 4; wait++)
            {
                await Task.Delay(500).ConfigureAwait(false);
                lock (_gate)
                {
                    quiet = State == AssistantState.Idle ? quiet + 1 : 0;
                }
            }

            FridayLog.Info($"Açılış rutini çalışıyor: {name}");
            SubmitText(name);
        }
    }

    /// <summary>Hep aynı saatlerde yapılan bir şey önerilir; soru okunduktan sonra cevap için mikrofon açılır.</summary>
    private void SuggestHabit()
    {
        try
        {
            lock (_gate)
            {
                if (State is not AssistantState.Idle)
                {
                    return;
                }
            }

            if (_tools.HabitSuggestion() is { } question)
            {
                Announce([new CoachStep(question)], listenAfter: true);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            FridayLog.Warn($"Alışkanlık önerisi: {exception.Message}");
        }
    }

    private void Announce(IReadOnlyList<CoachStep> script, bool listenAfter = false)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (State is not (AssistantState.Idle or AssistantState.Error))
            {
                // Meşgulken gelen hatırlatma kaybolmaz; Friday boşa düşünce okunur.
                if (_queued.Count < 8)
                {
                    _queued.Enqueue(script);
                }

                return;
            }

            _announcement?.Dispose();
            _announcement = new CancellationTokenSource();
            token = _announcement.Token;
            State = AssistantState.Speaking;
        }

        Report(AssistantState.Speaking, "Hatırlatma");
        _ = Task.Run(async () =>
        {
            try
            {
                AudioCues.PlayListening();
                await PlayScriptAsync(script, token).ConfigureAwait(false);
                RecordOnlineSpeech();
            }
            catch (OperationCanceledException)
            {
                // Kullanıcı sözü kesti; yeni tur kendi durumunu bildirir.
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (listenAfter)
            {
                // Soru soruldu ("Açayım mı?"); cevap için kısayola basmak gerekmez.
                Report(AssistantState.Idle, ReadyText);
                StartTurn(text: null);
                return;
            }

            Report(AssistantState.Idle, ReadyText);
        }, CancellationToken.None);
    }

    /// <summary>Cümleleri sırayla okur; her cümleden sonra adımın beklemesi kadar susar (nefes al… tut… ver…).</summary>
    private async Task PlayScriptAsync(IReadOnlyList<CoachStep> script, CancellationToken cancellationToken)
    {
        foreach (var step in script)
        {
            Show(step.Text);
            Say(step.Text);
            await _speaker.WaitUntilDoneAsync(cancellationToken).ConfigureAwait(false);
            if (step.PauseSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(step.PauseSeconds), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Günün ilk oturuşu: "Günaydın, İstanbul'da hava açık, 9 derece. Arabanın muayenesi yarın."</summary>
    private async Task GreetAsync()
    {
        try
        {
            var greeting = await _tools.MorningGreetingAsync(_settings.UserName, CancellationToken.None).ConfigureAwait(false);
            Announce([new CoachStep(greeting)]);
        }
        catch (HttpRequestException exception)
        {
            FridayLog.Warn($"Sabah karşılaması hazırlanamadı: {exception.Message}");
        }
    }

    /// <summary>Oyun, tam ekran video ya da sunum açık mı (ve ayar açık mı)?</summary>
    private bool YieldsResources()
    {
        var busy = _settings.YieldToFullScreen && FullScreenDetector.IsBusy();
        if (busy != _yielding)
        {
            _yielding = busy;
            FridayLog.Info(busy
                ? "Tam ekran uygulama açık: modeller cevaptan kısa süre sonra ekran kartından çıkarılacak"
                : "Tam ekran kapandı: modeller normal süreyle hazır tutuluyor");
        }

        return busy;
    }

    private void Report(AssistantState state, string message)
    {
        lock (_gate)
        {
            State = state;
        }

        // Friday kendisi dinlerken kelime aranmaz; kullanıcının sorusu onu yeniden tetiklemez ve işlemci boşta kalır.
        _wakeWord.Suspended = state == AssistantState.Listening;
        var yielding = YieldsResources();
        if (state == AssistantState.Listening)
        {
            // Oyun açıkken model yine yüklenir ve tam güçle cevap verir, ama cevaptan kısa süre sonra boşaltılır.
            _chat.KeepAlive = yielding ? "20s" : $"{Math.Max(1, _settings.GpuIdleMinutes)}m";
        }


        // Ortam sesi (yağmur, gürültü) Friday dinlerken ve konuşurken kısılır.
        _tools.DuckAmbient(state is AssistantState.Listening or AssistantState.Speaking);

        // Her boşa düşüşte uyku sayacı yeniden başlar.
        if (state is AssistantState.Idle or AssistantState.Error)
        {
            _idleTimer?.Change(yielding ? TimeSpan.FromSeconds(20) : TimeSpan.FromMinutes(Math.Max(1, _settings.GpuIdleMinutes)), Timeout.InfiniteTimeSpan);
            FlushQueuedSoon();
        }

        StatusChanged?.Invoke(this, new AssistantStatus(state, message));
    }

    /// <summary>Bekleyen duyuru varsa kısa bir aradan sonra (kullanıcı yeni bir şey sormadıysa) okunur.</summary>
    private void FlushQueuedSoon()
    {
        lock (_gate)
        {
            if (_queued.Count == 0)
            {
                return;
            }
        }

        _ = Task.Delay(1500).ContinueWith(_ =>
        {
            IReadOnlyList<CoachStep>? next = null;
            lock (_gate)
            {
                if (State is AssistantState.Idle or AssistantState.Error && _queued.Count > 0)
                {
                    next = _queued.Dequeue();
                }
            }

            if (next is not null)
            {
                Announce(next);
            }
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _idleTimer?.Dispose();
        _habitTimer?.Dispose();
        _routineTimer?.Dispose();
        _tools.Activity?.Dispose();
        _tools.Documents?.Dispose();
        _remote?.Dispose();
        _announcement?.Cancel();
        _announcement?.Dispose();
        _rhythm.Dispose();
        _tools.Dispose();
        _wakeWord.Dispose();
        _turn?.Cancel();
        _turn?.Dispose();
        _stopListening?.Dispose();
        _speaker.Dispose();
        _whisper.Dispose();
        _ollama.Dispose();
    }
}

/// <summary>Biten bir tur: kullanıcının sorusu ve Friday'in cevabı.</summary>
public sealed record TurnResult(string Question, string Reply);
