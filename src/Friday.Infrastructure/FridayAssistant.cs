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

    private DateTime _quietUntil;

    /// <summary>Rahatsız etme modu açıksa bitiş zamanı.</summary>
    public DateTime? QuietUntil => _quietUntil > DateTime.Now ? _quietUntil : null;

    /// <summary>Rahatsız etme modu açıldı ya da kapandı.</summary>
    public event EventHandler? QuietChanged;

    /// <summary>Kendiliğinden konuşmalar (ritim, alışkanlık, odak bekçisi, sabah selamı) susar; hatırlatmalar susmaz.</summary>
    private bool Muted => _quietUntil > DateTime.Now;

    /// <summary>Rahatsız etmeyi açar (süreyle) ya da kapatır (null).</summary>
    public string SetQuiet(TimeSpan? duration)
    {
        _quietUntil = duration is { } span ? DateTime.Now + span : DateTime.MinValue;
        _meetingQuiet = false;
        QuietChanged?.Invoke(this, EventArgs.Empty);
        if (duration is null)
        {
            FridayLog.Info("Rahatsız etme kapandı");
            return "Rahatsız etme kapandı; yine öneride bulunabilirim.";
        }

        FridayLog.Info($"Rahatsız etme: {_quietUntil:HH:mm} olana kadar");
        return string.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"),
            $"Tamam, saat {_quietUntil:HH:mm} olana kadar kendiliğinden konuşmayacağım. Kurduğun hatırlatmalar yine gelir.");
    }

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
        _rhythm.Speak += (_, script) =>
        {
            if (!Muted)
            {
                // Odak bir işe bağlıysa sonunda "bitirdin mi?" diye sorulur; cevap için mikrofon açılır.
                Announce(script, listenAfter: _tools.FocusQuestionPending);
            }
        };
        _tools.QuietControl = SetQuiet;
        _tools.Announcements = Announced;
        _tools.MeetingApp = () => _meeting;
        _tools.PomodoroControl = Pomodoro;
        _rhythm.FocusInsight = FocusInsight;
        _rhythm.Changed += (_, _) => FollowFocusSound();
        _rhythm.MorningGreeting += (_, _) => _ = GreetAsync();
        _tools.TimerElapsed += (_, message) => Announce(message);
        _tools.CaptureFolder = Path.Combine(paths.Data, "captures");
        _tools.SaveFolder = Path.Combine(paths.Home, "Kaydedilenler");
        _tools.Workspaces = new WorkspaceStore(Path.Combine(paths.Data, "workspaces.json"));
        _tools.Lists = new ListStore(Path.Combine(paths.Data, "lists.json"));
        _tools.Journal = new JournalStore(Path.Combine(paths.Data, "journal"));
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
        _careTimer ??= new Timer(_ => _ = CareTickAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        _ = AutoBackupAsync();
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
        if (_settings.WakeWord.Length == 0 || _meetingPausedWake)
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
        _meetingTimer = new Timer(_ => CheckMeeting(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
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
            await PrimeAsync(settings.OllamaModel).ConfigureAwait(false);
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

    /// <summary>Sistem talimatını ve çekirdek araçları modelin önbelleğine işletir; başarısız olursa yalnızca günlüğe yazılır.</summary>
    private async Task PrimeAsync(string model)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            await _chat.PrimeAsync(model, SystemPrompt.Build(_settings, _longTermMemory.Items, DateTime.Now), _tools.DefinitionsFor("", false), CancellationToken.None).ConfigureAwait(false);
            FridayLog.Info($"Dil modeli önbelleği hazırlandı ({clock.Elapsed.TotalSeconds:0.0} sn)");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            FridayLog.Warn($"Önbellek hazırlanamadı: {exception.Message}");
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
            await PrimeAsync(next).ConfigureAwait(false);
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

                // Kontrol listesi ya da kelime pratiği gibi cevap bekleyen bir soru varsa dinleme sürer.
                if ((_settings.FollowUpSeconds <= 0 && !_tools.InChecklist && !_tools.AwaitsAnswer) || stopListening.IsCancellationRequested || _endConversation)
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
        var timeout = wait ?? (followUp ? TimeSpan.FromSeconds(Math.Max(_settings.FollowUpSeconds, _tools.InChecklist || _tools.AwaitsAnswer ? 12 : 0)) : FirstListenTimeout);
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
        // "Günün nasıl geçti?" gibi serbest cevap bekleyen bir soru varsa söz ona gider; açıkça bir komutsa komut çalışır.
        // "Şimdi değil", "hayır" gibi sözler de soruya gider (yoksa "şimdi değil" mola ertelemesi sanılır).
        var answering = _tools.AwaitsAnswer
            && (_tools.AnswerTakesPriority || QuickIntents.IsDecline(text) || QuickIntents.IsNo(text)
                || QuickIntents.Match(text, _settings.Routines.Keys, _settings.Triggers.Keys) is not { Tentative: false });
        var command = _tools.InChecklist
            ? new QuickCommand("kontrol_cevap", new Dictionary<string, string> { ["metin"] = text })
            : answering
            ? new QuickCommand("serbest_cevap", new Dictionary<string, string> { ["metin"] = text })
            : _tools.HasPendingConfirmation && (QuickIntents.IsYes(text) || QuickIntents.IsNo(text))
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

        var widened = false;

        // Talimatta olmayan ama soruyla ilgili hafıza bilgileri bütün hafızada aranır ve yalnızca bu soruya eklenir.
        var memories = _longTermMemory.Items;
        var recalled = MemoryRecall.Context(MemoryRecall.Relevant(memories, question, MemoryRecall.PinnedItems(memories, DateOnly.FromDateTime(DateTime.Now))))
            + _tools.Situation(DateTime.Now);

        for (var round = 0; round <= maxRounds; round++)
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt.Build(_settings, _longTermMemory.Items, DateTime.Now) + (multiStep ? "\n" + AgentPlan.Instruction : "") + (documents is null ? "" : "\n" + documents))
            };
            messages.AddRange(_memory.Messages);
            if (recalled.Length > 0 && messages.FindLastIndex(message => message.Role == ChatRole.User) is var lastUser and >= 0)
            {
                messages[lastUser] = messages[lastUser] with { Content = messages[lastUser].Content + recalled };
            }

            var chunker = new SentenceChunker();
            var content = new StringBuilder();
            var calls = new List<ToolCall>();
            // Son turda araç verilmez; model mutlaka sözlü bir cevap verir.
            // Araç tanımları her soruda baştan işlenir (~4.100 belirteç, ~1,1 sn); soruya göre gereken alt küme gönderilir.
            var tools = round < maxRounds ? (widened ? _tools.Definitions : _tools.DefinitionsFor(question, multiStep, RecentTools())) : [];
            var replyStart = reply.Length;
            var unable = false;
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
                foreach (var sentence in chunker.Push(delta.Content))
                {
                    // Seçilen araçlar yetmediyse model "bunu yapamıyorum" der: cümle okunmaz, tur bütün araçlarla yinelenir.
                    if (!widened && !multiStep && tools.Count > 0 && tools.Count < _tools.Definitions.Count && AgentPlan.SoundsUnable(sentence))
                    {
                        unable = true;
                        break;
                    }

                    SayUnlessFiller(sentence);
                }

                if (unable)
                {
                    break;
                }

                Show(reply.ToString());
            }

            if (unable)
            {
                FridayLog.Info("Seçilen araçlar yetmedi; tur bütün araçlarla yineleniyor");
                widened = true;
                reply.Length = replyStart;
                Show(reply.ToString());
                round--;
                continue;
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

                RememberTool(call.Name);
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
    /// <summary>
    /// Etkinlik izleyicisi her zaman çalışır (koşullu hatırlatmalar ve olay tetikli rutinler için); ekran süresi
    /// ayarı yalnızca sürelerin kaydedilip kaydedilmeyeceğini belirler.
    /// </summary>
    private void UpdateActivity()
    {
        if (_activity is null)
        {
            _activity = new ActivityTracker(Path.Combine(_paths.Data, "screentime.json"));
            _activity.Distracted += (_, site) => OnDistracted(site);
            _activity.AppFocused += (_, app) => OnAppFocused(app.Label, app.Process);
            _activity.Returned += (_, _) => OnReturned();
            _activity.Start();
        }

        if (_settings.ScreenTime && _tools.Activity is null)
        {
            _ledger.Record(DataFlow.Local, "Ekran süresi", "Öndeki uygulamanın adı 15 sn'de bir sayılıyor; pencere başlıkları ve içerikler saklanmıyor");
        }

        _activity.Records = _settings.ScreenTime;
        _tools.Activity = _settings.ScreenTime ? _activity : null;
    }

    private ActivityTracker? _activity;

    /// <summary>Bir uygulama öne geldi: ona bağlı hatırlatmalar ve "X açılınca" rutinleri (aynı rutin en fazla 30 dk'da bir).</summary>
    private void OnAppFocused(string label, string process)
    {
        _tools.FireTriggers("uygulama", label);
        if (!string.Equals(label, process, StringComparison.OrdinalIgnoreCase))
        {
            _tools.FireTriggers("uygulama", process);
        }

        RunTriggeredRoutines(schedule => schedule.Trigger is { } trigger && trigger.StartsWith("uygulama:", StringComparison.Ordinal)
            && (ReminderStore.AppMatches(trigger["uygulama:".Length..], label) || ReminderStore.AppMatches(trigger["uygulama:".Length..], process)),
            TimeSpan.FromMinutes(30));
    }

    /// <summary>
    /// Odak oturumunun özeti: o sürede en çok hangi uygulamalar öndeydi; dikkat dağıtan bir site payı büyükse nazikçe
    /// söylenir. Ekran süresi kapalıysa null.
    /// </summary>
    private string? FocusInsight(TimeSpan length)
    {
        var parts = new List<string>();
        if (_tools.Activity is { } activity && length >= TimeSpan.FromMinutes(5))
        {
            var apps = activity.Since(DateTime.Now - length);
            var total = apps.Sum(item => item.Seconds);
            if (total >= 120)
            {
                var top = string.Join(" ve ", apps.Take(2).Select(item => $"{item.App} ({ScreenTimeLog.Spoken(item.Seconds)})"));
                parts.Add($"Bu sürede en çok {top} açıktı.");
                var distraction = apps.Where(item => ScreenTimeLog.IsDistraction(item.App)).Sum(item => item.Seconds);
                if (distraction > total / 5)
                {
                    parts.Add($"{ScreenTimeLog.Spoken(distraction)} dikkat dağıtan sitelerde geçti; bir dahakine onları kapatmayı deneyebilirsin.");
                }
            }
        }

        // Günlük hedef bugün ilk kez tamamlandıysa kutlanır.
        if (_tools.Rhythm is { } rhythm && rhythm.Today.FocusMinutes >= _settings.FocusGoalMinutes
            && _celebrated != DateOnly.FromDateTime(DateTime.Now))
        {
            _celebrated = DateOnly.FromDateTime(DateTime.Now);
            var streak = RhythmGoal.Streak(rhythm.History(60), _settings.FocusGoalMinutes);
            parts.Add($"Bugünkü {TurkishGrammar.Duration(_settings.FocusGoalMinutes)}lik odak hedefine ulaştın, tebrikler!{(streak > 1 ? $" {streak} gündür üst üste." : "")}");
        }

        if (_tools.FocusTaskQuestion() is { } question)
        {
            parts.Add(question);
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private DateOnly _celebrated;

    private readonly Queue<(DateTime At, string Name)> _recentTools = new();

    /// <summary>Son 10 dakikada kullanılan araçlar ("onu da ekle" gibi devam sorularında yine gönderilir).</summary>
    private IReadOnlyList<string> RecentTools()
    {
        lock (_recentTools)
        {
            while (_recentTools.Count > 0 && DateTime.Now - _recentTools.Peek().At > TimeSpan.FromMinutes(10))
            {
                _recentTools.Dequeue();
            }

            return [.. _recentTools.Select(item => item.Name).Distinct()];
        }
    }

    private void RememberTool(string name)
    {
        lock (_recentTools)
        {
            _recentTools.Enqueue((DateTime.Now, name));
            while (_recentTools.Count > 8)
            {
                _recentTools.Dequeue();
            }
        }
    }

    // ---------------------------------------------------------------- pomodoro

    private CancellationTokenSource? _pomodoro;

    /// <summary>Pomodoro serisi: 25 dk odak (ritim koçunda sayılır), 5 dk mola; son turdan sonra uzun mola önerisi. 0 durdurur.</summary>
    private string Pomodoro(int count)
    {
        _pomodoro?.Cancel();
        _pomodoro = null;
        if (count <= 0)
        {
            _tools.Rhythm?.EndFocus();
            return "Pomodoro serisini durdurdum.";
        }

        if (_tools.Rhythm is not { } rhythm)
        {
            return "Pomodoro için ritim koçunun açık olması gerekiyor (Ayarlar › Ritim).";
        }

        var source = _pomodoro = new CancellationTokenSource();
        rhythm.StartFocus(25);
        _ = RunPomodoroAsync(rhythm, count, source.Token);
        return $"{count} pomodoro başladı: 25 dakika odak, 5 dakika mola. İlk tur şimdi.";
    }

    private async Task RunPomodoroAsync(RhythmCoach rhythm, int count, CancellationToken cancellationToken)
    {
        try
        {
            for (var round = 1; round <= count; round++)
            {
                if (round > 1)
                {
                    rhythm.StartFocus(25);
                    Announce($"Pomodoro {round}/{count}: 25 dakika odak, başla.");
                }

                // Odak bitişini ritim koçu kendisi söyler (odak özeti ve hedefle birlikte).
                await Task.Delay(TimeSpan.FromMinutes(25) + TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
                if (round == count)
                {
                    break;
                }

                rhythm.StartBreak();
                Announce("5 dakika mola: kalk, biraz esne, su iç.");
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                rhythm.EndBreak();
            }

            Announce($"{count} pomodoro tamam, harika iş! Şimdi 15 dakikalık uzun bir molayı hak ettin.");
        }
        catch (OperationCanceledException)
        {
            // Durduruldu.
        }
    }

    // ---------------------------------------------------------------- görüşme algılama

    private Timer? _meetingTimer;
    private string? _meeting;
    private bool _meetingQuiet;
    private bool _meetingPausedWake;
    private readonly List<string> _meetingHeld = [];

    /// <summary>Friday dışında mikrofonu kullanan uygulama (Teams, Zoom, Meet…); görüşme yoksa null.</summary>
    public string? Meeting => _meeting;

    /// <summary>Görüşme başladı ya da bitti.</summary>
    public event EventHandler? MeetingChanged;

    /// <summary>
    /// 20 sn'de bir Windows'un mikrofon kaydına bakılır. Başka bir uygulama mikrofonu kullanıyorsa (görüşme, kayıt)
    /// Friday rahatsız etmeye geçer, uyandırma kelimesini dinlemeyi bırakır ve hatırlatmaları sesli okumaz; görüşme
    /// bitince her şey eski haline döner ve bekleyen hatırlatmalar okunur.
    /// </summary>
    private void CheckMeeting()
    {
        try
        {
            var app = _settings.MeetingQuiet ? MeetingDetector.ActiveCall() : null;
            if (app is not null && _meeting is null)
            {
                StartMeeting(app);
            }
            else if (app is null && _meeting is not null)
            {
                EndMeeting();
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            FridayLog.Warn($"Görüşme algılama: {exception.Message}");
        }
    }

    private DateTime _meetingStart;

    private void StartMeeting(string app)
    {
        _meeting = app;
        _meetingStart = DateTime.Now;
        if (!Muted)
        {
            _quietUntil = DateTime.Now.AddHours(8);
            _meetingQuiet = true;
            QuietChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_wakeWord.IsRunning)
        {
            _wakeWord.Stop();
            _meetingPausedWake = true;
        }

        _ledger.Record(DataFlow.Local, "Görüşme", $"{app} mikrofonu kullanıyor · Friday sessiz, uyandırma kelimesi duraklatıldı");
        FridayLog.Info($"Görüşme başladı: {app}");
        MeetingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndMeeting()
    {
        var app = _meeting;
        _meeting = null;
        if (_meetingQuiet)
        {
            _meetingQuiet = false;
            _quietUntil = DateTime.MinValue;
            QuietChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_meetingPausedWake)
        {
            _meetingPausedWake = false;
            StartWakeWord();
        }

        FridayLog.Info($"Görüşme bitti: {app}");
        MeetingChanged?.Invoke(this, EventArgs.Empty);
        string[] held;
        lock (_meetingHeld)
        {
            held = [.. _meetingHeld];
            _meetingHeld.Clear();
        }

        var script = new List<CoachStep>();
        if (held.Length > 0)
        {
            script.Add(new CoachStep(held.Length == 1
                ? $"Görüşme sırasında bir hatırlatma geldi: {held[0]}"
                : $"Görüşme sırasında {held.Length} hatırlatma geldi. {string.Join(" ", held)}"));
        }

        // Uzun bir görüşmeden sonra not almak isteyip istemediği sorulur; cevap notlara yazılır.
        var question = Muted || app is null || !_tools.AllowsNudge("gorusme_notu") ? null : _tools.MeetingNoteQuestion(app, _meetingStart, DateTime.Now);
        if (question is not null)
        {
            _tools.MarkNudge("gorusme_notu");
            script.Add(new CoachStep(question));
        }

        if (script.Count > 0)
        {
            Announce(script, listenAfter: question is not null);
        }
    }

    // ---------------------------------------------------------------- dakikalık bakım: göz molası, hava uyarısı

    private Timer? _careTimer;
    private DateTime _lastEyeBreak = DateTime.Now;
    private DateOnly _weatherWarned;

    private DateTime _lastNudge;

    /// <summary>Kendiliğinden söylenen bir şey: türü kaydedilir ("bunu bir daha söyleme" ona uygulanır).</summary>
    private void AnnounceNudge(string kind, string text, bool listenAfter = false)
    {
        _tools.MarkNudge(kind);
        Announce([new CoachStep(text)], listenAfter);
    }

    /// <summary>
    /// Kendiliğinden yapılan akşam dürtmeleri (son tarih, seri, hava, doğum günü arifesi, günlük sorusu) arasında en az
    /// 45 dakika olur; hepsi aynı saatte üst üste konuşmaz. Kullanıcının kurduğu uyarılar (kur alarmı, süre sınırı, uyku
    /// saati, hatırlatmalar) bu sınıra takılmaz.
    /// </summary>
    private bool CanNudge(DateTime now) => now - _lastNudge >= TimeSpan.FromMinutes(45);

    private void Nudged(DateTime now) => _lastNudge = now;

    private async Task CareTickAsync()
    {
        try
        {
            var now = DateTime.Now;
            lock (_gate)
            {
                if (State is not AssistantState.Idle)
                {
                    return;
                }
            }

            // Kur alarmı: kullanıcının kurduğu bir uyarı; rahatsız etme açıkken de söylenir (görüşmedeyse sonra okunur).
            if (await _tools.RateAlertCheckAsync(now, CancellationToken.None).ConfigureAwait(false) is { } rate)
            {
                Announce(rate);
                return;
            }

            // Uyku saati: yarım saat önce ve yarım saat geç kalınca birer kez (bilgisayar başındaysa).
            if (!Muted && !_rhythm.Snapshot().IsAway && _tools.AllowsNudge("uyku") && _tools.BedTimeNudge(now) is { } bed)
            {
                AnnounceNudge("uyku", bed);
                return;
            }

            // 20-20-20: 20 dakika aralıksız ekran başında, odakta değilken, tam ekran yokken.
            if (_settings.EyeBreaks && !Muted && !FullScreenDetector.IsBusy())
            {
                var snapshot = _rhythm.Snapshot();
                var stretch = snapshot.StretchStart is { } start ? now - start : TimeSpan.Zero;
                var inFocus = snapshot.FocusEnd is { } end && end > now;
                if (stretch < TimeSpan.FromMinutes(2))
                {
                    _lastEyeBreak = now;
                }
                else if (!inFocus && now - _lastEyeBreak >= TimeSpan.FromMinutes(20) && _tools.AllowsNudge("goz"))
                {
                    _lastEyeBreak = now;
                    AnnounceNudge("goz", "Göz molası: 20 saniye boyunca uzağa, en az 6 metre öteye bak.");
                    return;
                }
            }

            // Günlük süre sınırı dolan uygulama varsa bir kez söylenir.
            if (!Muted && _tools.AppLimitAlert(now) is { } limit)
            {
                Announce(limit);
                return;
            }

            // Yarın bir doğum günü ya da yıldönümü varsa akşam 19.00–21.30 arası bir kez sorulur.
            if (!Muted && CanNudge(now) && now.Hour is >= 19 and <= 21 && (now.Hour < 21 || now.Minute < 30) && _tools.AllowsNudge("arife") && _tools.EveReminder(now) is { } eve)
            {
                Nudged(now);
                AnnounceNudge("arife", eve, listenAfter: true);
                return;
            }

            // Son tarih: yarın teslim edilecek işler akşam 18.00–21.30 arası bir kez söylenir.
            if (!Muted && CanNudge(now) && now.Hour is >= 18 and <= 21 && (now.Hour < 21 || now.Minute < 30) && !_rhythm.Snapshot().IsAway && _tools.AllowsNudge("son_tarih") && _tools.DeadlineNudge(now) is { } deadline)
            {
                Nudged(now);
                AnnounceNudge("son_tarih", deadline);
                return;
            }

            // Seri koruma: 20.00–21.30 arası, serisi olan bir alışkanlık bugün yapılmadıysa bir kez hatırlatılır.
            if (!Muted && CanNudge(now) && (now.Hour == 20 || (now.Hour == 21 && now.Minute < 30)) && !_rhythm.Snapshot().IsAway && _tools.AllowsNudge("seri") && _tools.StreakNudge(now) is { } nudge)
            {
                Nudged(now);
                AnnounceNudge("seri", nudge);
                return;
            }

            // Gün sonu günlük sorusu: 21.30–23.00 arası, bilgisayar başındayken, günde bir kez.
            if (!Muted && CanNudge(now) && (now.Hour == 22 || (now.Hour == 21 && now.Minute >= 30)) && !_rhythm.Snapshot().IsAway && !FullScreenDetector.IsBusy()
                && _tools.AllowsNudge("gun_sonu") && _tools.JournalQuestion(now) is { } journal)
            {
                Nudged(now);
                AnnounceNudge("gun_sonu", journal, listenAfter: true);
                return;
            }

            // Akşam hava uyarısı: 20.00–21.30 arası, günde bir kez.
            if (_settings.WeatherAlerts && !Muted && CanNudge(now) && _tools.AllowsNudge("hava") && now.Hour is 20 or 21 && (now.Hour < 21 || now.Minute < 30)
                && _weatherWarned != DateOnly.FromDateTime(now))
            {
                _weatherWarned = DateOnly.FromDateTime(now);
                if (await _tools.TomorrowWeatherAlertAsync(CancellationToken.None).ConfigureAwait(false) is { } alert)
                {
                    Nudged(now);
                    AnnounceNudge("hava", alert);
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            FridayLog.Warn($"Bakım: {exception.Message}");
        }
    }

    // ---------------------------------------------------------------- haftalık yedek

    /// <summary>Açılıştan 10 dk sonra: son yedek 7 günden eskiyse D:\\Friday-yedekler'e yeni yedek; son 4'ü kalır.</summary>
    private async Task AutoBackupAsync()
    {
        if (!_settings.AutoBackup)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMinutes(10)).ConfigureAwait(false);
        try
        {
            var folder = Path.Combine(Path.GetPathRoot(_paths.Home)!, "Friday-yedekler");
            Directory.CreateDirectory(folder);
            var backups = new DirectoryInfo(folder).GetFiles("friday-yedek-*.zip").OrderByDescending(file => file.LastWriteTime).ToList();
            if (backups.Count > 0 && DateTime.Now - backups[0].LastWriteTime < TimeSpan.FromDays(7))
            {
                return;
            }

            var zip = Path.Combine(folder, $"friday-yedek-{DateTime.Now:yyyy-MM-dd-HHmm}.zip");
            var files = await Task.Run(() => DataMaintenance.CreateBackup(_paths.Data, zip)).ConfigureAwait(false);
            foreach (var old in backups.Skip(3))
            {
                old.Delete();
            }

            _ledger.Record(DataFlow.Local, "Yedek", $"Haftalık yedek alındı: {Path.GetFileName(zip)} ({files} dosya)");
            FridayLog.Info($"Haftalık yedek: {zip} ({files} dosya)");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FridayLog.Warn($"Haftalık yedek alınamadı: {exception.Message}");
        }
    }

    private DateTime? _soundFocus;

    /// <summary>Odak başlayınca seçilen ortam sesi açılır, bitince (Friday açtıysa) kapanır.</summary>
    private void FollowFocusSound()
    {
        var snapshot = _rhythm.Snapshot();
        var active = snapshot.FocusStart is not null && snapshot.FocusEnd is { } end && end > DateTime.Now;
        if (active && _soundFocus != snapshot.FocusStart && _settings.FocusSound.Length > 0)
        {
            _soundFocus = snapshot.FocusStart;
            _tools.SetFocusSound(_settings.FocusSound);
        }
        else if (!active && _soundFocus is not null)
        {
            _soundFocus = null;
            _tools.SetFocusSound(null);
        }
    }

    /// <summary>Kullanıcı bilgisayara döndü: "dönünce" hatırlatmaları ve rutinleri (en fazla saatte bir).</summary>
    private readonly List<(DateTime At, string Text, bool Away)> _announced = [];

    /// <summary>Kendiliğinden söylenen her şey kaydedilir; kullanıcı bilgisayar başında değilken söylenenler dönünce tekrar edilir.</summary>
    private void RecordAnnouncement(IReadOnlyList<CoachStep> script)
    {
        var text = string.Join(" ", script.Select(step => step.Text)).Trim();
        if (text.Length == 0)
        {
            return;
        }

        lock (_announced)
        {
            _announced.Add((DateTime.Now, text, _rhythm.Snapshot().IsAway));
            if (_announced.Count > 40)
            {
                _announced.RemoveAt(0);
            }
        }
    }

    private IReadOnlyList<(DateTime At, string Text)> Announced()
    {
        lock (_announced)
        {
            return [.. _announced.Select(item => (item.At, item.Text))];
        }
    }

    private void OnReturned()
    {
        // Yokken söylenen (boş odaya okunan) hatırlatmalar dönünce bir kez tekrar edilir.
        List<string> missed;
        lock (_announced)
        {
            missed = [.. _announced.Where(item => item.Away && DateTime.Now - item.At < TimeSpan.FromHours(6)).Select(item => item.Text)];
            for (var index = 0; index < _announced.Count; index++)
            {
                _announced[index] = _announced[index] with { Away = false };
            }
        }

        if (missed.Count > 0 && _tools.AllowsNudge("kayip"))
        {
            _tools.MarkNudge("kayip");
            Announce(missed.Count == 1
                ? $"Sen yokken bir şey söyledim: {missed[0]}"
                : $"Sen yokken {missed.Count} şey söyledim. {string.Join(" ", missed.TakeLast(3))}");
        }

        _tools.FireTriggers("donus");
        RunTriggeredRoutines(schedule => schedule.Trigger == "donus", TimeSpan.FromHours(1));
    }

    private void RunTriggeredRoutines(Func<RoutineSchedule, bool> matches, TimeSpan cooldown)
    {
        foreach (var (name, text) in _settings.RoutineSchedules.ToArray())
        {
            if (!_settings.Routines.ContainsKey(name) || RoutineSchedule.Parse(text) is not { } schedule || !matches(schedule))
            {
                continue;
            }

            lock (_routineRuns)
            {
                if (_routineRuns.TryGetValue(name, out var last) && DateTime.Now - last < cooldown)
                {
                    continue;
                }

                _routineRuns[name] = DateTime.Now;
            }

            lock (_gate)
            {
                if (State is not AssistantState.Idle)
                {
                    return;
                }
            }

            FridayLog.Info($"Olayla rutin çalışıyor: {name} ({schedule.Describe()})");
            SubmitText(name);
            return;
        }
    }

    /// <summary>Odak oturumunda dikkat dağıtan bir sitede kalındı: oturum başına bir kez, nazikçe.</summary>
    private void OnDistracted(string site)
    {
        if (Muted || !_settings.FocusGuard || _tools.Rhythm?.Snapshot() is not { FocusStart: { } start, FocusEnd: { } end } || end <= DateTime.Now
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
        var timed = _settings.RoutineSchedules.Values.Any(text => RoutineSchedule.Parse(text) is { At: not null });
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
        if (Muted)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                if (State is not AssistantState.Idle)
                {
                    return;
                }
            }

            if (_tools.AllowsNudge("rutin_onerisi") && _tools.HabitSuggestion() is { } question)
            {
                AnnounceNudge("rutin_onerisi", question, listenAfter: true);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            FridayLog.Warn($"Alışkanlık önerisi: {exception.Message}");
        }
    }

    private void Announce(IReadOnlyList<CoachStep> script, bool listenAfter = false)
    {
        if (_meeting is not null)
        {
            // Görüşmedeyken Friday sesli konuşmaz; hatırlatma görüşme bitince okunur.
            lock (_meetingHeld)
            {
                if (_meetingHeld.Count < 10)
                {
                    _meetingHeld.Add(string.Join(" ", script.Select(step => step.Text)));
                }
            }

            return;
        }

        RecordAnnouncement(script);
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
        if (Muted)
        {
            return;
        }

        try
        {
            var greeting = await _tools.MorningGreetingAsync(_settings.UserName, CancellationToken.None).ConfigureAwait(false);
            Announce([new CoachStep(greeting)], listenAfter: _tools.GreetingAsks);
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
        _meetingTimer?.Dispose();
        _routineTimer?.Dispose();
        _activity?.Dispose();
        _careTimer?.Dispose();
        _pomodoro?.Cancel();
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
