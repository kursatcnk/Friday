using System.ComponentModel;
using System.Windows.Threading;

namespace Friday.Desktop;

/// <summary>
/// Ayarlar anında kaydedilir: bir ayar değişince kısa bir beklemeden sonra (art arda değişiklikler tek kayıt olur) kaydedilip
/// uygulanır. Eskiden her değişiklik için sayfanın altındaki "Kaydet ve uygula" düğmesine basmak gerekiyordu.
/// </summary>
public sealed partial class PanelViewModel
{
    /// <summary>Ayarlar sayfasındaki denetimlere bağlı özellikler.</summary>
    private static readonly HashSet<string> SettingNames = new(StringComparer.Ordinal)
    {
        "AddressAs", "AmbientHigh", "AmbientLow", "AmbientMedium", "AutoBackup", "AzureRegion", "BrainFast", "BrainSmart", "BreakEvery30", "BreakEvery50", "BreakEvery90", "BreakLength10", "BreakLength15", "BreakLength5", "CalendarUrl", "ChatAfter10", "ChatAfter120", "ChatAfter30", "City", "ClipboardHistoryEnabled", "DocumentFoldersText", "DocumentMemory", "EngineAzure", "EnginePiper", "EngineXtts", "FocusGuard", "FollowUpChat", "FollowUpLong", "FollowUpOff", "FollowUpShort", "Gpu1", "Gpu15", "Gpu2", "Gpu5", "GpuRendering", "HabitReminders", "HabitSuggestionsEnabled", "JournalPrompt", "KeepHistory", "LastLatency", "ListenNormal", "ListenPatient", "ListenQuick", "MeetingNotes", "MeetingQuiet", "MorningEnabled", "PersonalityFormal", "PersonalityPlain", "PersonalityWitty", "ReadingMemory", "RemoteAccessEnabled", "RhythmEnabled", "RoomDry", "RoomLight", "RoomWarm", "ScreenTimeEnabled", "SelectedAccent", "SelectedHotkey", "SelectedMicrophone", "SelectedPiperVoice", "SelectedQuickAsk", "SelectedScale", "SelectedTheme", "SelectedVoice", "SnippetsText", "SpeedFast", "SpeedFaster", "SpeedNormal", "SpeedSlow", "StartWithWindows", "UserName", "VersionText", "VoiceAhmet", "VoiceEmel", "WakeEnabled", "WakeNormal", "WakeSensitive", "WakeStrict", "WeatherAlerts", "YieldToFullScreen"
    };

    private DispatcherTimer? _autoSave;

    /// <summary>Ayarlar yüklenirken (sayfa açılırken) değişiklikler kayıt sayılmaz.</summary>
    private bool _loadingSettings;

    private void WatchSettings()
    {
        _autoSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _autoSave.Tick += (_, _) =>
        {
            _autoSave.Stop();
            SaveSettingsCommand.Execute(null);
        };
        PropertyChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_autoSave is null || _loadingSettings || !IsSettings || e.PropertyName is null || !SettingNames.Contains(e.PropertyName))
        {
            return;
        }

        _autoSave.Stop();
        _autoSave.Start();
    }
}
