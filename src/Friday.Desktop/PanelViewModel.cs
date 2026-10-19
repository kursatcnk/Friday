using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;
using Friday.Infrastructure;

namespace Friday.Desktop;

public sealed partial class ChatLine : ObservableObject
{
    public ChatLine(bool isUser, string text, DateTime time)
    {
        IsUser = isUser;
        _text = text;
        Time = time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public bool IsUser { get; }

    public string Time { get; }

    [ObservableProperty]
    private string _text;
}

public sealed record MemoryView(string Id, string Text, string Date);

public sealed record LedgerView(string Time, string Category, string Detail, bool IsOutbound)
{
    public string Glyph => IsOutbound ? "" : "";

    public string FlowLabel => IsOutbound ? "DIŞARI" : "YEREL";
}

public sealed record RoutineStepView(string Glyph, string Text);

public sealed record RoutineView(string Name, IReadOnlyList<RoutineStepView> Steps);

/// <summary>Panelin kullanıcı eylemleri; App bunları asistana bağlar.</summary>
public sealed class PanelActions
{
    public Action<string> Send { get; init; } = _ => { };

    public Action<string> Remember { get; init; } = _ => { };

    public Action<string> Forget { get; init; } = _ => { };

    public Action OpenSettingsFile { get; init; } = () => { };

    public Action OpenDataFolder { get; init; } = () => { };

    public Action<FridaySettings> Save { get; init; } = _ => { };

    public Action<string> PreviewVoice { get; init; } = _ => { };
}

public sealed record VoiceOption(string Name, string Label);

public sealed partial class PanelViewModel : ObservableObject
{
    private const int MaxLines = 80;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly PanelActions _actions;
    private readonly FridaySettings _settings;
    private ChatLine? _currentReply;

    public PanelViewModel(FridaySettings settings, PanelActions actions)
    {
        _settings = settings;
        _actions = actions;
        AssistantName = settings.AssistantName;
        LoadSettingsFields();
        LoadRoutines();
    }

    public string AssistantName { get; }

    public ObservableCollection<ChatLine> Lines { get; } = [];

    public ObservableCollection<MemoryView> Memories { get; } = [];

    public ObservableCollection<LedgerView> Ledger { get; } = [];

    public ObservableCollection<RoutineView> Routines { get; } = [];

    public IReadOnlyList<string> Suggestions { get; } =
    [
        "Ekranımda ne var?",
        "Panodaki metni özetle",
        "Günaydın, günün özeti?",
        "Bilgisayar neden yavaş?",
        "Şu an ne çalıyor?",
        "Bugün ne konuştuk?",
    ];

    // ---------- Gezinme ----------

    [ObservableProperty]
    private bool _isChat = true;

    [ObservableProperty]
    private bool _isMemory;

    [ObservableProperty]
    private bool _isPrivacy;

    [ObservableProperty]
    private bool _isRoutines;

    [ObservableProperty]
    private bool _isSettings;

    // ---------- Durum ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateBrush))]
    private AssistantState _state = AssistantState.Starting;

    [ObservableProperty]
    private string _statusText = "Başlatılıyor…";

    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyKeys))]
    private string _hotkey = "Ctrl+Alt+F";

    [ObservableProperty]
    private string _hotkeyNote = "";

    public IReadOnlyList<string> HotkeyKeys => Hotkey.Split('+');

    public Brush StateBrush
    {
        get
        {
            var brush = new SolidColorBrush(AudioRing.Palette(State));
            brush.Freeze();
            return brush;
        }
    }

    // ---------- Sohbet ----------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _draft = "";

    [ObservableProperty]
    private bool _hasLines;

    // ---------- Hafıza ----------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemoryCommand))]
    private string _newMemory = "";

    [ObservableProperty]
    private bool _hasMemories;

    // ---------- Gizlilik ----------

    [ObservableProperty]
    private int _localCount;

    [ObservableProperty]
    private int _outboundCount;

    [ObservableProperty]
    private string _microphoneSummary = "Bugün açılmadı";

    // ---------- Ayarlar ----------

    public IReadOnlyList<VoiceOption> Voices { get; } =
        [.. XttsSynthesizer.RecommendedVoices.Select(voice => new VoiceOption(voice.Name, voice.Label))];

    [ObservableProperty]
    private VoiceOption? _selectedVoice;

    [ObservableProperty]
    private bool _engineXtts = true;

    [ObservableProperty]
    private bool _engineAzure;

    [ObservableProperty]
    private bool _enginePiper;

    [ObservableProperty]
    private string _azureKey = "";

    [ObservableProperty]
    private string _azureRegion = "";

    [ObservableProperty]
    private bool _voiceEmel = true;

    [ObservableProperty]
    private bool _voiceAhmet;

    [ObservableProperty]
    private bool _personalityWitty = true;

    [ObservableProperty]
    private bool _personalityPlain;

    [ObservableProperty]
    private bool _personalityFormal;

    [ObservableProperty]
    private bool _followUpOff;

    [ObservableProperty]
    private bool _followUpShort = true;

    [ObservableProperty]
    private bool _followUpLong;

    [ObservableProperty]
    private string _city = "";

    /// <summary>"Otomatik" ve bilgisayardaki kayıt cihazları.</summary>
    public IReadOnlyList<string> Microphones { get; } = ["Otomatik", .. MicrophoneRecorder.DeviceNames()];

    [ObservableProperty]
    private string _selectedMicrophone = "Otomatik";

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _engineLabel = "";

    [ObservableProperty]
    private string _savedMessage = "";

    public string ModelSummary =>
        $"Sohbet: {_settings.OllamaModel} · Görme: {_settings.VisionModel} (yalnızca ekrana bakarken) · " +
        $"{_settings.GpuIdleMinutes} dk boşta kalınca GPU tamamen boşaltılır";

    // ---------- Asistandan gelenler ----------

    public void OnStatus(AssistantStatus status)
    {
        State = status.State;
        StatusText = status.Message;
        if (status.State is AssistantState.Listening)
        {
            _currentReply = null;
        }
    }

    public void OnUserSaid(string text)
    {
        _currentReply = null;
        AddLine(new ChatLine(true, text, DateTime.Now));
    }

    public void OnAssistantSaid(string text)
    {
        if (_currentReply is null)
        {
            _currentReply = new ChatLine(false, text, DateTime.Now);
            AddLine(_currentReply);
        }
        else
        {
            _currentReply.Text = text;
        }
    }

    public void LoadHistory(IEnumerable<ConversationTurn> turns)
    {
        foreach (var turn in turns.TakeLast(20))
        {
            AddLine(new ChatLine(true, turn.User, turn.Time.LocalDateTime));
            AddLine(new ChatLine(false, turn.Assistant, turn.Time.LocalDateTime));
        }
    }

    public void ShowMemories(IEnumerable<MemoryItem> items)
    {
        Memories.Clear();
        foreach (var item in items.Reverse())
        {
            Memories.Add(new MemoryView(item.Id, item.Text, item.Created.ToLocalTime().ToString("d MMMM yyyy", Turkish)));
        }

        HasMemories = Memories.Count > 0;
    }

    public void ShowLedger(IEnumerable<LedgerEntry> entries)
    {
        Ledger.Clear();
        foreach (var entry in entries)
        {
            AddLedger(entry);
        }
    }

    public void AddLedger(LedgerEntry entry)
    {
        Ledger.Insert(0, new LedgerView(entry.Time.ToString("HH:mm", Turkish), entry.Category, entry.Detail, entry.Flow == DataFlow.Outbound));
        LocalCount = Ledger.Count(item => !item.IsOutbound);
        OutboundCount = Ledger.Count(item => item.IsOutbound);
        var microphone = Ledger.Where(item => item.Category == "Mikrofon").ToList();
        MicrophoneSummary = microphone.Count == 0
            ? "Bugün açılmadı"
            : $"Bugün {microphone.Count} kez, yalnızca kısayoldan sonra";
    }

    // ---------- Komutlar ----------

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        var text = Draft.Trim();
        Draft = "";
        _actions.Send(text);
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(Draft);

    [RelayCommand]
    private void Suggest(string text) => _actions.Send(text);

    [RelayCommand(CanExecute = nameof(CanAddMemory))]
    private void AddMemory()
    {
        _actions.Remember(NewMemory.Trim());
        NewMemory = "";
    }

    private bool CanAddMemory() => !string.IsNullOrWhiteSpace(NewMemory);

    [RelayCommand]
    private void ForgetMemory(string id) => _actions.Forget(id);

    [RelayCommand]
    private void RunRoutine(string name)
    {
        IsRoutines = false;
        IsChat = true;
        _actions.Send($"{name} rutinini çalıştır");
    }

    [RelayCommand]
    private void OpenSettingsFile() => _actions.OpenSettingsFile();

    [RelayCommand]
    private void OpenDataFolder() => _actions.OpenDataFolder();

    [RelayCommand]
    private void PreviewVoice()
    {
        if (SelectedVoice is { } voice)
        {
            EngineXtts = true;
            EngineAzure = false;
            EnginePiper = false;
            _actions.PreviewVoice(voice.Name);
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settings.SpeechEngine = EngineXtts ? "xtts" : EngineAzure ? "azure" : "piper";
        if (SelectedVoice is { } voice)
        {
            _settings.XttsSpeaker = voice.Name;
        }

        _settings.AzureSpeechKey = AzureKey.Trim();
        _settings.AzureSpeechRegion = string.IsNullOrWhiteSpace(AzureRegion) ? "westeurope" : AzureRegion.Trim();
        _settings.AzureVoice = VoiceAhmet ? "tr-TR-AhmetNeural" : "tr-TR-EmelNeural";
        _settings.Personality = PersonalityPlain ? "sade" : PersonalityFormal ? "resmi" : "esprili";
        _settings.FollowUpSeconds = FollowUpOff ? 0 : FollowUpLong ? 7 : 4;
        _settings.City = City.Trim();
        _settings.MicrophoneDevice = SelectedMicrophone == "Otomatik" ? "" : SelectedMicrophone;
        _settings.UserName = UserName.Trim();
        _actions.Save(_settings);
        SavedMessage = $"Kaydedildi · {DateTime.Now:HH:mm}";
    }

    private void LoadSettingsFields()
    {
        var engine = _settings.SpeechEngine.Trim().ToLowerInvariant();
        EngineAzure = engine == "azure";
        EnginePiper = engine == "piper";
        EngineXtts = !EngineAzure && !EnginePiper;
        SelectedVoice = Voices.FirstOrDefault(voice => voice.Name == _settings.XttsSpeaker) ?? Voices[0];
        AzureKey = _settings.AzureSpeechKey;
        AzureRegion = _settings.AzureSpeechRegion;
        VoiceAhmet = _settings.AzureVoice.Contains("Ahmet", StringComparison.OrdinalIgnoreCase);
        VoiceEmel = !VoiceAhmet;
        PersonalityPlain = _settings.Personality == "sade";
        PersonalityFormal = _settings.Personality == "resmi";
        PersonalityWitty = !PersonalityPlain && !PersonalityFormal;
        FollowUpOff = _settings.FollowUpSeconds <= 0;
        FollowUpLong = _settings.FollowUpSeconds > 5;
        FollowUpShort = !FollowUpOff && !FollowUpLong;
        City = _settings.City;
        SelectedMicrophone = Microphones.FirstOrDefault(name => _settings.MicrophoneDevice.Length > 0
            && name.Contains(_settings.MicrophoneDevice, StringComparison.OrdinalIgnoreCase)) ?? "Otomatik";
        UserName = _settings.UserName;
    }

    private void LoadRoutines()
    {
        foreach (var (name, steps) in _settings.Routines)
        {
            var views = new List<RoutineStepView>();
            foreach (var text in steps)
            {
                try
                {
                    var step = RoutineStep.Parse(text);
                    views.Add(step.Action switch
                    {
                        RoutineAction.App => new RoutineStepView("", $"{step.Value} uygulamasını aç"),
                        RoutineAction.Website => new RoutineStepView("", $"{step.Value} sitesini aç"),
                        RoutineAction.Search => new RoutineStepView("", $"\"{step.Value}\" ara"),
                        RoutineAction.Media => new RoutineStepView("", $"Medya: {step.Value.Replace('_', ' ')}"),
                        RoutineAction.Say => new RoutineStepView("", $"\"{step.Value}\" de"),
                        RoutineAction.Briefing => new RoutineStepView("", "Günün özetini oku"),
                        RoutineAction.LinguaFlow => new RoutineStepView("", "LinguaFlow çevirisini başlat"),
                        _ => new RoutineStepView("", text)
                    });
                }
                catch (FormatException)
                {
                    views.Add(new RoutineStepView("", $"Geçersiz adım: {text}"));
                }
            }

            var title = name.Length == 0 ? name : char.ToUpper(name[0], Turkish) + name[1..];
            Routines.Add(new RoutineView(title, views));
        }
    }

    private void AddLine(ChatLine line)
    {
        Lines.Add(line);
        while (Lines.Count > MaxLines)
        {
            Lines.RemoveAt(0);
        }

        HasLines = Lines.Count > 0;
    }
}
