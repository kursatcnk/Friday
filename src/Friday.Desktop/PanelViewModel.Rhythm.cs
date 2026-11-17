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

    /// <summary>Saniyede bir (Ritim ya da Bugün sayfası açıkken): halka, saat ve açıklama.</summary>
    public void TickRhythm(RhythmSnapshot snapshot)
    {
        var display = RhythmText.Describe(snapshot, DateTime.Now);
        RhythmMode = display.Mode;
        DialLabel = display.Label;
        DialClock = display.Clock;
        DialCaption = display.ClockCaption;
        DialProgress = display.Progress;
        DialSentence = display.Sentence;
        DialColor = display.Mode switch
        {
            RhythmMode.Working => Color.FromRgb(0xF2, 0xCC, 0x8F),
            RhythmMode.Overdue => Color.FromRgb(0xFF, 0xB8, 0x6B),
            RhythmMode.Focus => Color.FromRgb(0xFF, 0x8A, 0x5B),
            RhythmMode.Break => Color.FromRgb(0x7F, 0xD4, 0xB0),
            _ => Color.FromRgb(0x6E, 0x6C, 0x67)
        };
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

    [RelayCommand]
    private void EnableRhythm()
    {
        RhythmEnabled = true;
        _settings.Rhythm = true;
        _actions.Save(_settings);
    }
}
