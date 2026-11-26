using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;

namespace Friday.Desktop;

/// <summary>Ritim sayfasındaki haftalık grafikte bir gün.</summary>
public sealed record WeekBarView(string Day, double Height, string Label, bool IsToday);

public sealed partial class PanelViewModel
{
    // ---------- Büyük halka (saniyede bir) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkMode), nameof(IsFocusMode), nameof(IsBreakMode), nameof(IsOffMode), nameof(CanSnooze))]
    private RhythmMode _rhythmMode = RhythmMode.Ready;

    [ObservableProperty]
    private string _dialLabel = "Hazır";

    [ObservableProperty]
    private string _dialClock = "00:00";

    [ObservableProperty]
    private string _dialCaption = "aralıksız";

    [ObservableProperty]
    private double _dialProgress;

    [ObservableProperty]
    private string _dialSentence = "";

    [ObservableProperty]
    private Color _dialColor = Color.FromRgb(0xF2, 0xCC, 0x8F);

    public bool IsWorkMode => RhythmMode is RhythmMode.Ready or RhythmMode.Working or RhythmMode.Overdue or RhythmMode.Away;

    public bool IsFocusMode => RhythmMode == RhythmMode.Focus;

    public bool IsBreakMode => RhythmMode == RhythmMode.Break;

    public bool IsOffMode => RhythmMode == RhythmMode.Off;

    public bool CanSnooze => RhythmMode is RhythmMode.Working or RhythmMode.Overdue;

    // ---------- Bugün ----------

    [ObservableProperty]
    private string _rhythmActive = "0 dk";

    [ObservableProperty]
    private string _rhythmFocus = "0 dk";

    [ObservableProperty]
    private string _rhythmBreaks = "0";

    [ObservableProperty]
    private string _rhythmLongest = "0 dk";

    [ObservableProperty]
    private string _rhythmWater = "0";

    [ObservableProperty]
    private string _rhythmSummary = "";

    [ObservableProperty]
    private IReadOnlyList<RhythmSegment> _rhythmSegments = [];

    [ObservableProperty]
    private DateTime _rhythmNow = DateTime.Now;

    [ObservableProperty]
    private string _rhythmRule = "";

    // ---------- Hafta ----------

    public System.Collections.ObjectModel.ObservableCollection<WeekBarView> WeekBars { get; } = [];

    [ObservableProperty]
    private string _weekSummary = "";

    [ObservableProperty]
    private string _weekAverage = "";

    /// <summary>Ritim düğmelerinin son sonucu ("Odak başladı, 25 dakika").</summary>
    [ObservableProperty]
    private string _rhythmNote = "";

    /// <summary>Mola önerileri bir süreliğine kapatıldı.</summary>
    [ObservableProperty]
    private bool _suggestionsPaused;

    /// <summary>Odak duraklatıldı ("Devam et" görünür).</summary>
    [ObservableProperty]
    private bool _focusPaused;

    /// <summary>Pomodoro serisi sürüyor ("Seriyi bitir" görünür).</summary>
    [ObservableProperty]
    private bool _seriesActive;

    /// <summary>Düğmeler ritim koçunu doğrudan yönetir: sohbete yazılmaz, sesli cevap verilmez.</summary>
    [RelayCommand]
    private void Control(string key) => RhythmNote = _actions.RhythmControl(key);

    private static Color ThemeColor(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) is SolidColorBrush brush ? brush.Color : Color.FromRgb(0x7C, 0x79, 0x73);

    public void TickRhythm(RhythmSnapshot snapshot)
    {
        var display = RhythmText.Describe(snapshot, DateTime.Now);
        RhythmMode = display.Mode;
        DialLabel = display.Label;
        DialClock = display.Clock;
        DialCaption = display.ClockCaption;
        SuggestionsPaused = snapshot.SnoozedUntil is { } until && until > DateTime.Now;
        FocusPaused = snapshot.FocusPausedAt is not null;
        SeriesActive = snapshot.SeriesRound > 0;
        DialProgress = display.Progress;
        DialSentence = display.Sentence;
        // Renkler temadan: odakta vurgu, molada sakin yeşil, uzun çalışmada uyarı, olağan çalışmada yazı rengi.
        DialColor = ThemeColor(display.Mode switch
        {
            RhythmMode.Working => "TextBrush",
            RhythmMode.Overdue => "WarningBrush",
            RhythmMode.Focus => "AccentBrush",
            RhythmMode.Break => "CalmBrush",
            _ => "FaintBrush"
        });
        RhythmRule = $"Her {TurkishGrammar.Duration(snapshot.BreakEvery.TotalMinutes)} aralıksız çalışmada mola önerir · mola {TurkishGrammar.Duration(snapshot.BreakLength.TotalMinutes)}";
    }

    /// <summary>Ritim koçundan gelen günün özeti.</summary>
    public void ShowRhythm(RhythmDay today, RhythmSnapshot snapshot)
    {
        RhythmActive = RhythmText.Compact(today.ActiveMinutes);
        RhythmFocus = RhythmText.Compact(today.FocusMinutes);
        RhythmBreaks = today.Breaks.ToString(CultureInfo.InvariantCulture);
        RhythmLongest = RhythmText.Compact(today.LongestStretchMinutes);
        RhythmWater = today.Water.ToString(CultureInfo.InvariantCulture);
        RhythmSegments = today.Segments;
        RhythmNow = DateTime.Now;
        var stretch = snapshot.StretchStart is { } start ? DateTime.Now - start : TimeSpan.Zero;
        RhythmSummary = RhythmCoachLines.Status(today, stretch);
        TickRhythm(snapshot);
    }

    /// <summary>Son 7 günün aktif süresi: en uzun gün 110 piksel, bugün kor renginde.</summary>
    public void ShowWeek(IReadOnlyList<RhythmDay> days)
    {
        var longest = Math.Max(60, days.Count == 0 ? 0 : days.Max(day => day.ActiveMinutes));
        var today = DateOnly.FromDateTime(DateTime.Now);
        WeekBars.Clear();
        foreach (var day in days)
        {
            var minutes = day.ActiveMinutes;
            var label = minutes < 1 ? "" : RhythmText.Compact(minutes);
            WeekBars.Add(new WeekBarView(day.Date.ToString("ddd", Turkish), Math.Max(4, 110 * minutes / longest), label, day.Date == today));
        }

        WeekSummary = RhythmCoachLines.Week(days);
        var worked = days.Where(day => day.ActiveMinutes >= 1).ToList();
        WeekAverage = worked.Count == 0 ? "" : $"Çalıştığın günlerde ortalama {RhythmText.Compact(worked.Average(day => day.ActiveMinutes))}";
    }

    [ObservableProperty]
    private string _goalText = "";

    [ObservableProperty]
    private double _goalFraction;

    [ObservableProperty]
    private string _streakText = "";

    [ObservableProperty]
    private string _focusApps = "";

    [ObservableProperty]
    private string _customFocusMinutes = "40";

    [ObservableProperty]
    private bool _showRhythmHelp;

    [ObservableProperty]
    private bool _eyeBreaks;

    partial void OnEyeBreaksChanged(bool value)
    {
        if (_loadingGoal || _settings.EyeBreaks == value)
        {
            return;
        }

        _settings.EyeBreaks = value;
        _actions.Persist(_settings);
    }

    public IReadOnlyList<string> GoalOptions { get; } = ["1 saat", "2 saat", "3 saat", "4 saat", "5 saat"];

    public IReadOnlyList<string> FocusSoundOptions { get; } = ["Sessiz", "Yağmur", "Beyaz gürültü", "Kahverengi gürültü", "Dalga"];

    [ObservableProperty]
    private string _selectedGoal = "2 saat";

    [ObservableProperty]
    private string _selectedFocusSound = "Sessiz";

    private bool _loadingGoal;

    /// <summary>Hedef ve seri kartı: bugünkü odak, hedefe göre çubuk ve kaç gündür üst üste.</summary>
    public void ShowGoal(RhythmDay today, IReadOnlyList<RhythmDay> history)
    {
        _loadingGoal = true;
        var goal = Math.Max(10, _settings.FocusGoalMinutes);
        SelectedGoal = $"{Math.Max(1, (int)Math.Round(goal / 60.0))} saat";
        SelectedFocusSound = _settings.FocusSound switch
        {
            "yagmur" => "Yağmur",
            "beyaz" => "Beyaz gürültü",
            "kahverengi" => "Kahverengi gürültü",
            "dalga" => "Dalga",
            _ => "Sessiz"
        };
        EyeBreaks = _settings.EyeBreaks;
        _loadingGoal = false;
        GoalFraction = Math.Clamp(today.FocusMinutes / goal, 0, 1);
        GoalText = $"{RhythmText.Compact(today.FocusMinutes)} / {RhythmText.Compact(goal)} odak · %{Math.Min(100, Math.Round(100 * today.FocusMinutes / goal)):0}";
        var streak = RhythmGoal.Streak(history, goal);
        StreakText = streak switch
        {
            0 => today.FocusMinutes > 0 ? "Bugün hedefi tamamlarsan seri başlar." : "Henüz seri yok; bugün başlat.",
            1 => "1 gün: seri başladı.",
            _ => $"{streak} gündür üst üste."
        };
    }

    partial void OnSelectedGoalChanged(string value)
    {
        if (_loadingGoal || !int.TryParse(value.Split(' ')[0], out var hours))
        {
            return;
        }

        _settings.FocusGoalMinutes = hours * 60;
        _actions.Persist(_settings);
    }

    partial void OnSelectedFocusSoundChanged(string value)
    {
        if (_loadingGoal)
        {
            return;
        }

        _settings.FocusSound = value switch
        {
            "Yağmur" => "yagmur",
            "Beyaz gürültü" => "beyaz",
            "Kahverengi gürültü" => "kahverengi",
            "Dalga" => "dalga",
            _ => ""
        };
        _actions.Persist(_settings);
    }

    [RelayCommand]
    private void StartCustomFocus()
    {
        if (int.TryParse(CustomFocusMinutes.Trim(), out var minutes) && minutes is >= 5 and <= 240)
        {
            RhythmNote = _actions.RhythmControl($"odak:{minutes}");
        }
    }

    [RelayCommand]
    private void ToggleRhythmHelp() => ShowRhythmHelp = !ShowRhythmHelp;

    [RelayCommand]
    private void EnableRhythm()
    {
        RhythmEnabled = true;
        _settings.Rhythm = true;
        _actions.Save(_settings);
    }
}
