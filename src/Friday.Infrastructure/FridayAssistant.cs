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
    private readonly ConversationLog _history;
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
        ConversationLog history,
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
        _history = history;
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
        _speaker.FellBack += (_, reason) =>
            _ledger.Record(DataFlow.Local, "Ses", $"Azure kullanılamadı, yerel Piper'a geçildi ({reason})");
    }

    /// <summary>Kullanılan ses motoru (ör. "Azure nöral ses (Emel)").</summary>
    public string VoiceLabel => _speaker.EngineLabel;

    /// <summary>Panelden ayarlar kaydedilince ses motorunu ve uyandırma kelimesini yeni ayarlarla kurar.</summary>
    public void ApplySettings()
    {
        ReconfigureVoice();
        StartWakeWord();
        _rhythm.Apply(_settings);
    }

    /// <summary>Uyandırma kelimesi açıksa bekleme metni bunu hatırlatır.</summary>
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

        Report(AssistantState.Starting, "Ses sentezi hazırlanıyor…");
        var voice = string.IsNullOrWhiteSpace(settings.VoiceModelPath) ? _paths.DefaultVoice : settings.VoiceModelPath;
        _speaker.Configure(settings, voice);

        Report(AssistantState.Starting, "Dil modeli sunucusu başlatılıyor…");
        await _ollama.EnsureRunningAsync(settings.OllamaPort, cancellationToken).ConfigureAwait(false);
        Report(AssistantState.Starting, "Sohbet modeli hazırlanıyor…");
        await _ollama.EnsureTextModelAsync(settings.OllamaModel, settings.VisionModel, cancellationToken).ConfigureAwait(false);

        // Whisper, dil modeli ve ses modeli aynı anda GPU'ya yüklenir.
        _chat.KeepAlive = $"{Math.Max(1, settings.GpuIdleMinutes)}m";
        _whisper.Configure(_paths.ResolveWhisperModel(settings.WhisperModelPath), settings);
        Report(AssistantState.Starting, "Modeller GPU'ya yükleniyor…");
        await Task.WhenAll(
            _chat.WarmUpAsync(settings.OllamaModel, cancellationToken),
            _whisper.EnsureLoadedAsync(cancellationToken),
            _speaker.WarmUpAsync(cancellationToken)).ConfigureAwait(false);

        _idleTimer = new Timer(_ => _ = SleepModelsAsync(), null, Timeout.Infinite, Timeout.Infinite);
        StartWakeWord();
        _rhythm.Apply(settings);
        _tools.StartReminders();
        Report(AssistantState.Idle, $"{ReadyText} · Whisper {_whisper.RuntimeLabel} · {_speaker.EngineLabel}");
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
                await Task.WhenAll(
                    _chat.WarmUpAsync(_settings.OllamaModel, CancellationToken.None),
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
                if (_settings.FollowUpSeconds <= 0 || stopListening.IsCancellationRequested)
                {
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
    private async Task<string?> ListenAsync(bool followUp, CancellationToken stopListening, CancellationToken cancellationToken)
    {
        var device = _microphone.LastDeviceName.Length > 0 ? $" · {_microphone.LastDeviceName}" : "";
        Report(AssistantState.Listening, (followUp ? "Devam edebilirsin…" : "Dinliyorum…") + device);
        AudioCues.PlayListening();
        var opened = Stopwatch.StartNew();
        var timeout = followUp ? TimeSpan.FromSeconds(_settings.FollowUpSeconds) : FirstListenTimeout;
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
        var clock = Stopwatch.StartNew();
        var text = recording.Transcript ?? await _whisper.TranscribeAsync(audio, cancellationToken).ConfigureAwait(false);
        Diagnostics?.Invoke(this, string.Create(CultureInfo.InvariantCulture,
            $"yazıya dökme: {(recording.Transcript is null ? $"{clock.ElapsedMilliseconds} ms" : "tahmin hazırdı, 0 ms")}"));
        if (text.Length == 0)
        {
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
            && (QuickIntents.Match(text, _settings.Routines.Keys) is { Tentative: false } || text.TrimEnd().EndsWith('?'));
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

        Diagnostics?.Invoke(this, string.Create(CultureInfo.InvariantCulture, $"hızlı yol: {call.Name} {clock.ElapsedMilliseconds} ms"));
        _memory.Add(new ChatMessage(ChatRole.Assistant, "") { ToolCalls = [call] });
        _memory.Add(new ChatMessage(ChatRole.Tool, reply.Text) { ToolName = call.Name });
        _memory.Add(new ChatMessage(ChatRole.Assistant, reply.Text));
        if (reply.Script is { } script)
        {
            await PlayScriptAsync(script, cancellationToken).ConfigureAwait(false);
            return string.Join(" ", script.Select(step => step.Text));
        }

        AssistantSaid?.Invoke(this, reply.Text);
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

    /// <summary>Yazıya dökülmüş bir soruyu cevaplar ve cevabın okunması bitene kadar bekler.</summary>
    public async Task AskAsync(string text, CancellationToken cancellationToken)
    {
        UserSaid?.Invoke(this, text);
        Report(AssistantState.Thinking, "Düşünüyor…");
        _memory.BeginTurn();
        _tools.LastUserText = text;
        var context = _activeWindow is { } window ? $"\n[Bağlam: kullanıcı \"{window.Title}\" ({window.Process}) penceresindeydi]" : "";
        _memory.Add(new ChatMessage(ChatRole.User, text + context));
        var reply = QuickIntents.Match(text, _settings.Routines.Keys) is { } quick
            ? await RunQuickAsync(quick, cancellationToken).ConfigureAwait(false)
            : null;
        if (reply is null)
        {
            // Ollama bir sebeple kapandıysa (çökme, elle kapatma) sessizce yeniden başlatılır; soru boşa gitmez.
            await _ollama.EnsureRunningAsync(_settings.OllamaPort, cancellationToken).ConfigureAwait(false);
            reply = await AnswerAsync(text, cancellationToken).ConfigureAwait(false);
        }

        _memory.CompactToolResults();
        if (_settings.KeepHistory)
        {
            _history.Append(text, reply);
        }

        await _speaker.WaitUntilDoneAsync(cancellationToken).ConfigureAwait(false);
        RecordOnlineSpeech();
        Report(AssistantState.Idle, ReadyText);
    }

    private async Task<string> AnswerAsync(string question, CancellationToken cancellationToken)
    {
        var reply = new StringBuilder();
        var retried = false;
        var filled = false;
        // Düşünme adımı yalnızca hangi aracın çağrılacağına karar verirken gerekir. Selamlaşma ve sohbette, ya da araç
        // sonucu geldikten sonra cevabı cümleye dökerken kapalıdır: bu ikisi cevap başına 0,5–2 sn kazandırıyor.
        var smallTalk = ConversationStyle.IsSmallTalk(question);
        for (var round = 0; round <= MaxToolRounds; round++)
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt.Build(_settings, _longTermMemory.Items, DateTime.Now))
            };
            messages.AddRange(_memory.Messages);

            var chunker = new SentenceChunker();
            var content = new StringBuilder();
            var calls = new List<ToolCall>();
            // Son turda araç verilmez; model mutlaka sözlü bir cevap verir.
            var tools = round < MaxToolRounds ? _tools.Definitions : [];
            var think = round == 0 && !smallTalk;

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
                AssistantSaid?.Invoke(this, reply.ToString());
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
                AssistantSaid?.Invoke(this, reply.ToString());
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
                    AssistantSaid?.Invoke(this, clean);
                    return clean;
                }

                return reply.ToString();
            }

            foreach (var call in calls)
            {
                Report(AssistantState.Thinking, AssistantTools.DescribeActivity(call.Name) + "…");
                ToolStarted?.Invoke(this, call.Name);
                // Uzun süren işlerde sessiz beklemek yerine kısa bir cümle: gerçek bir asistan gibi "bakıyorum" der.
                if (!filled && reply.Length == 0 && SlowTools.Contains(call.Name))
                {
                    filled = true;
                    Say(Fillers[Random.Shared.Next(Fillers.Length)]);
                }

                var result = await _tools.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
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
                    AssistantSaid?.Invoke(this, reply.ToString());
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
        if (spoken.Length == 0)
        {
            return;
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
    private void Announce(IReadOnlyList<CoachStep> script)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (State is not (AssistantState.Idle or AssistantState.Error))
            {
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

            if (!token.IsCancellationRequested)
            {
                Report(AssistantState.Idle, ReadyText);
            }
        }, CancellationToken.None);
    }

    /// <summary>Cümleleri sırayla okur; her cümleden sonra adımın beklemesi kadar susar (nefes al… tut… ver…).</summary>
    private async Task PlayScriptAsync(IReadOnlyList<CoachStep> script, CancellationToken cancellationToken)
    {
        foreach (var step in script)
        {
            AssistantSaid?.Invoke(this, step.Text);
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

    private void Report(AssistantState state, string message)
    {
        lock (_gate)
        {
            State = state;
        }

        // Friday kendisi dinlerken kelime aranmaz; kullanıcının sorusu onu yeniden tetiklemez ve işlemci boşta kalır.
        _wakeWord.Suspended = state == AssistantState.Listening;

        // Her boşa düşüşte uyku sayacı yeniden başlar.
        if (state is AssistantState.Idle or AssistantState.Error)
        {
            _idleTimer?.Change(TimeSpan.FromMinutes(Math.Max(1, _settings.GpuIdleMinutes)), Timeout.InfiniteTimeSpan);
        }

        StatusChanged?.Invoke(this, new AssistantStatus(state, message));
    }

    public void Dispose()
    {
        _idleTimer?.Dispose();
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
