using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;
using Friday.Infrastructure;

namespace Friday.Desktop;

public sealed partial class PanelViewModel
{
    private string? _pendingClear;
    private string? _updateUrl;

    /// <summary>Konuşma kısayolu seçenekleri.</summary>
    public IReadOnlyList<string> HotkeyOptions { get; } = ["Ctrl+Alt+F", "Ctrl+Alt+J", "Ctrl+Shift+Space", "Win+Shift+F", "Ctrl+Alt+H"];

    /// <summary>Hızlı komut kutusu kısayolu seçenekleri.</summary>
    public IReadOnlyList<string> QuickAskOptions { get; } = ["Kapalı", "Ctrl+Alt+Space", "Ctrl+Shift+K", "Ctrl+Alt+K", "Win+Shift+Space"];

    /// <summary>Bilgisayardaki Türkçe Piper sesleri.</summary>
    public IReadOnlyList<VoiceOption> PiperVoices => _actions.PiperVoices;

    [ObservableProperty]
    private string _addressAs = "";

    [ObservableProperty]
    private string _calendarUrl = "";

    [ObservableProperty]
    private bool _clipboardHistoryEnabled;

    [ObservableProperty]
    private bool _brainFast = true;

    [ObservableProperty]
    private bool _brainSmart;

    /// <summary>Büyük model kurulu değilse "Akıllı" seçeneği kapalıdır.</summary>
    public bool CanUseSmartBrain => _actions.SmartModelInstalled;

    public string BrainHint => _actions.SmartModelInstalled
        ? "Hızlı: Gemma 4 E4B, kısa cevaplar ~0,3 sn. Akıllı: Gemma 4 12B, niyeti ve bağlamı çok daha iyi anlar; kısa cevaplar ~0,5–1 sn, ekran kartında ~3 GB fazla yer kaplar."
        : "Akıllı mod için Gemma 4 12B gerekir (7,5 GB, D:\\Friday\\models). Kurmak için: setup.ps1 -Smart";

    [ObservableProperty]
    private bool _habitSuggestionsEnabled = true;

    [ObservableProperty]
    private bool _ambientLow;

    [ObservableProperty]
    private bool _ambientMedium = true;

    [ObservableProperty]
    private bool _ambientHigh;

    [ObservableProperty]
    private string _calendarMessage = "";

    [ObservableProperty]
    private string _selectedHotkey = "Ctrl+Alt+F";

    [ObservableProperty]
    private string _selectedQuickAsk = "Ctrl+Alt+Space";

    [ObservableProperty]
    private VoiceOption? _selectedPiperVoice;

    [ObservableProperty]
    private bool _speedSlow;

    [ObservableProperty]
    private bool _speedNormal = true;

    [ObservableProperty]
    private bool _speedFast;

    [ObservableProperty]
    private bool _speedFaster;

    [ObservableProperty]
    private bool _listenQuick;

    [ObservableProperty]
    private bool _listenNormal = true;

    [ObservableProperty]
    private bool _listenPatient;

    [ObservableProperty]
    private bool _chatAfter10;

    [ObservableProperty]
    private bool _chatAfter30 = true;

    [ObservableProperty]
    private bool _chatAfter120;

    [ObservableProperty]
    private bool _breakLength5 = true;

    [ObservableProperty]
    private bool _breakLength10;

    [ObservableProperty]
    private bool _breakLength15;

    [ObservableProperty]
    private bool _gpu1;

    [ObservableProperty]
    private bool _gpu2 = true;

    [ObservableProperty]
    private bool _gpu5;

    [ObservableProperty]
    private bool _gpu15;

    [ObservableProperty]
    private bool _roomDry;

    [ObservableProperty]
    private bool _roomLight = true;

    [ObservableProperty]
    private bool _roomWarm;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _keepHistory = true;

    [ObservableProperty]
    private bool _isConfirmingClear;

    [ObservableProperty]
    private string _clearQuestion = "";

    [ObservableProperty]
    private string _dataMessage = "";

    [ObservableProperty]
    private string _updateMessage = "";

    [ObservableProperty]
    private string _maintenanceMessage = "";

    [ObservableProperty]
    private bool _needsRestart;

    [ObservableProperty]
    private bool _isConfirmingReclaim;

    [ObservableProperty]
    private string _reclaimQuestion = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanReclaimCommand), nameof(ConfirmReclaimCommand))]
    private bool _isWorking;

    private IReadOnlyList<ReclaimItem> _reclaimItems = [];

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    public string VersionText
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return string.Create(CultureInfo.InvariantCulture, $"{AssistantName} {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}");
        }
    }

    [RelayCommand]
    private void AskClear(string kind)
    {
        _pendingClear = kind;
        ClearQuestion = kind switch
        {
            "chats" => "Bütün sohbetler kalıcı olarak silinsin mi?",
            "memory" => "Friday'in senin hakkında bildiği her şey silinsin mi?",
            "reminders" => "Kurulu bütün hatırlatmalar silinsin mi?",
            _ => ""
        };
        IsConfirmingClear = ClearQuestion.Length > 0;
        DataMessage = "";
    }

    [RelayCommand]
    private void ConfirmClear()
    {
        if (_pendingClear is { } kind)
        {
            var count = _actions.ClearData(kind);
            DataMessage = kind switch
            {
                "chats" => $"{count} sohbet silindi.",
                "memory" => $"{count} bilgi silindi.",
                _ => $"{count} hatırlatma silindi."
            };
        }

        _pendingClear = null;
        IsConfirmingClear = false;
    }

    [RelayCommand]
    private void CancelClear()
    {
        _pendingClear = null;
        IsConfirmingClear = false;
    }

    [RelayCommand]
    private void OpenLogs() => _actions.OpenLogs();

    [RelayCommand]
    private async Task TestCalendarAsync()
    {
        CalendarMessage = "Deniyorum…";
        CalendarMessage = await _actions.TestCalendar(CalendarUrl).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Backup()
    {
        if (_actions.BackupData() is { } message)
        {
            MaintenanceMessage = message;
        }
    }

    [RelayCommand]
    private void Restore()
    {
        if (_actions.RestoreData() is { } message)
        {
            MaintenanceMessage = message;
            NeedsRestart = message.StartsWith("Yedek hazır", StringComparison.Ordinal);
        }
    }

    [RelayCommand]
    private void RestartNow() => _actions.Restart();

    private bool CanMaintain() => !IsWorking;

    /// <summary>Silinebilecek klasörleri bulur ve onay ister.</summary>
    [RelayCommand(CanExecute = nameof(CanMaintain))]
    private async Task ScanReclaimAsync()
    {
        IsWorking = true;
        MaintenanceMessage = "Bakıyorum…";
        try
        {
            _reclaimItems = await _actions.ScanReclaimable().ConfigureAwait(true);
        }
        finally
        {
            IsWorking = false;
        }

        if (_reclaimItems.Count == 0)
        {
            MaintenanceMessage = "Silinebilecek fazlalık yok; her şey kullanılıyor.";
            return;
        }

        var total = _reclaimItems.Sum(item => item.Bytes);
        ReclaimQuestion = $"{DataMaintenance.Describe(total)} açılabilir: " +
                          string.Join(", ", _reclaimItems.Select(item => $"{item.Label} {DataMaintenance.Describe(item.Bytes)}")) +
                          ". Kişisel verilerine dokunulmaz. Silinsin mi?";
        MaintenanceMessage = "";
        IsConfirmingReclaim = true;
    }

    [RelayCommand(CanExecute = nameof(CanMaintain))]
    private async Task ConfirmReclaimAsync()
    {
        IsConfirmingReclaim = false;
        IsWorking = true;
        MaintenanceMessage = "Siliniyor…";
        try
        {
            var freed = await _actions.Reclaim(_reclaimItems).ConfigureAwait(true);
            MaintenanceMessage = $"{DataMaintenance.Describe(freed)} yer açıldı.";
        }
        finally
        {
            IsWorking = false;
            _reclaimItems = [];
        }
    }

    [RelayCommand]
    private void CancelReclaim()
    {
        IsConfirmingReclaim = false;
        _reclaimItems = [];
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        UpdateMessage = "Denetleniyor…";
        try
        {
            var result = await _actions.CheckForUpdate().ConfigureAwait(true);
            UpdateMessage = result.Message;
            HasUpdate = result.Available;
            _updateUrl = result.Url;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        if (_updateUrl is { } url && url.StartsWith("https://github.com/", StringComparison.Ordinal))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>Kaydetmeden önce: kısayollar canlı değişir, başlangıç kaydı yazılır.</summary>
    private void SaveMoreSettings()
    {
        _settings.SpeechSpeed = SpeedSlow ? 0.9 : SpeedFast ? 1.15 : SpeedFaster ? 1.3 : 1.0;
        _settings.SilenceMilliseconds = ListenQuick ? 800 : ListenPatient ? 1800 : 1200;
        _settings.ConversationMemoryMinutes = ChatAfter10 ? 10 : ChatAfter120 ? 120 : 30;
        _settings.BreakMinutes = BreakLength10 ? 10 : BreakLength15 ? 15 : 5;
        _settings.GpuIdleMinutes = Gpu1 ? 1 : Gpu5 ? 5 : Gpu15 ? 15 : 2;
        _settings.VoiceRoom = RoomDry ? 0 : RoomWarm ? 0.14 : 0.07;
        _settings.VoiceModelPath = SelectedPiperVoice?.Name ?? "";
        _settings.KeepHistory = KeepHistory;
        _settings.AddressAs = AddressAs.Trim();
        _settings.CalendarUrl = CalendarUrl.Trim();
        _settings.ClipboardHistory = ClipboardHistoryEnabled;
        _settings.OllamaModel = BrainSmart && _actions.SmartModelInstalled ? FridaySettings.SmartTextModel : FridaySettings.FastTextModel;
        _settings.HabitSuggestions = HabitSuggestionsEnabled;
        _settings.AmbientVolume = AmbientLow ? 0.12 : AmbientHigh ? 0.45 : 0.25;

        var notes = new List<string>();
        if (StartWithWindows != _settings.StartWithWindows)
        {
            if (_actions.SetStartWithWindows(StartWithWindows))
            {
                _settings.StartWithWindows = StartWithWindows;
            }
            else
            {
                StartWithWindows = _settings.StartWithWindows;
                notes.Add("Başlangıç ayarı değiştirilemedi");
            }
        }

        if (!string.Equals(SelectedHotkey, _settings.Hotkey, StringComparison.OrdinalIgnoreCase))
        {
            if (SelectedHotkey == SelectedQuickAsk)
            {
                notes.Add("İki kısayol aynı olamaz");
                SelectedHotkey = _settings.Hotkey;
            }
            else if (_actions.ApplyHotkey(SelectedHotkey) is { } problem)
            {
                notes.Add(problem);
                SelectedHotkey = _settings.Hotkey;
            }
            else
            {
                _settings.Hotkey = SelectedHotkey;
                Hotkey = SelectedHotkey;
                HotkeyNote = "";
            }
        }

        var quickAsk = SelectedQuickAsk == "Kapalı" ? "" : SelectedQuickAsk;
        if (!string.Equals(quickAsk, _settings.QuickAskHotkey, StringComparison.OrdinalIgnoreCase))
        {
            if (quickAsk.Length > 0 && quickAsk == _settings.Hotkey)
            {
                notes.Add("İki kısayol aynı olamaz");
                SelectedQuickAsk = _settings.QuickAskHotkey.Length == 0 ? "Kapalı" : _settings.QuickAskHotkey;
            }
            else if (_actions.ApplyQuickAskHotkey(quickAsk) is { } problem)
            {
                notes.Add(problem);
                SelectedQuickAsk = _settings.QuickAskHotkey.Length == 0 ? "Kapalı" : _settings.QuickAskHotkey;
            }
            else
            {
                _settings.QuickAskHotkey = quickAsk;
            }
        }

        _settingsNote = notes.Count == 0 ? "" : " · " + string.Join(" · ", notes);
    }

    private string _settingsNote = "";

    private void LoadMoreSettings()
    {
        SpeedSlow = _settings.SpeechSpeed < 0.95;
        SpeedFast = _settings.SpeechSpeed is >= 1.08 and < 1.22;
        SpeedFaster = _settings.SpeechSpeed >= 1.22;
        SpeedNormal = !SpeedSlow && !SpeedFast && !SpeedFaster;
        ListenQuick = _settings.SilenceMilliseconds < 1000;
        ListenPatient = _settings.SilenceMilliseconds > 1500;
        ListenNormal = !ListenQuick && !ListenPatient;
        ChatAfter10 = _settings.ConversationMemoryMinutes <= 15;
        ChatAfter120 = _settings.ConversationMemoryMinutes >= 60;
        ChatAfter30 = !ChatAfter10 && !ChatAfter120;
        BreakLength10 = _settings.BreakMinutes is >= 8 and < 13;
        BreakLength15 = _settings.BreakMinutes >= 13;
        BreakLength5 = !BreakLength10 && !BreakLength15;
        Gpu1 = _settings.GpuIdleMinutes <= 1;
        Gpu5 = _settings.GpuIdleMinutes is >= 4 and < 10;
        Gpu15 = _settings.GpuIdleMinutes >= 10;
        Gpu2 = !Gpu1 && !Gpu5 && !Gpu15;
        RoomDry = _settings.VoiceRoom < 0.02;
        RoomWarm = _settings.VoiceRoom > 0.1;
        RoomLight = !RoomDry && !RoomWarm;
        SelectedPiperVoice = PiperVoices.FirstOrDefault(voice => string.Equals(voice.Name, _settings.VoiceModelPath, StringComparison.OrdinalIgnoreCase))
            ?? PiperVoices.FirstOrDefault(voice => voice.Name.EndsWith("tr_TR-dfki-medium.onnx", StringComparison.OrdinalIgnoreCase))
            ?? (PiperVoices.Count > 0 ? PiperVoices[0] : null);
        KeepHistory = _settings.KeepHistory;
        AddressAs = _settings.AddressAs;
        CalendarUrl = _settings.CalendarUrl;
        ClipboardHistoryEnabled = _settings.ClipboardHistory;
        BrainSmart = _settings.OllamaModel == FridaySettings.SmartTextModel;
        BrainFast = !BrainSmart;
        HabitSuggestionsEnabled = _settings.HabitSuggestions;
        AmbientLow = _settings.AmbientVolume < 0.18;
        AmbientHigh = _settings.AmbientVolume > 0.35;
        AmbientMedium = !AmbientLow && !AmbientHigh;
        StartWithWindows = _settings.StartWithWindows;
        SelectedHotkey = HotkeyOptions.FirstOrDefault(option => string.Equals(option, _settings.Hotkey, StringComparison.OrdinalIgnoreCase)) ?? _settings.Hotkey;
        SelectedQuickAsk = _settings.QuickAskHotkey.Length == 0 ? "Kapalı" : _settings.QuickAskHotkey;
    }

    /// <summary>"tr_TR-dfki-medium.onnx" → "Dfki (orta)".</summary>
    public static VoiceOption PiperVoiceOption(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var parts = name.Split('-');
        var speaker = parts.Length > 1 ? parts[1] : name;
        var quality = parts.Length > 2 ? parts[2] switch { "low" => " (düşük)", "medium" => " (orta)", "high" => " (yüksek)", _ => "" } : "";
        return new VoiceOption(file, char.ToUpper(speaker[0], CultureInfo.InvariantCulture) + speaker[1..] + quality);
    }
}
