using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;

namespace Friday.Desktop;

/// <summary>Çalışan bir zamanlayıcı: ne için, kalan süre ve dolmuşluk (0–1).</summary>
public sealed record CountdownView(string Id, string Label, string Left, double Progress);

/// <summary>Kronometre kartında bir tur satırı ("3. tur · 1:05 · 3:12").</summary>
public sealed record LapView(string Number, string Split, string Total);

public sealed partial class PanelViewModel
{
    /// <summary>Kronometrenin gösterdiği süre ("12:34", "1:02:09").</summary>
    [ObservableProperty]
    private string _chronoClock = "00:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChronoToggleLabel), nameof(ChronoState))]
    private bool _chronoRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChronoToggleLabel), nameof(ChronoState))]
    private bool _chronoHasTime;

    /// <summary>Tek düğme: başlat, duraklat ya da kaldığı yerden sürdür.</summary>
    public string ChronoToggleLabel => ChronoRunning ? "Duraklat" : ChronoHasTime ? "Devam et" : "Başlat";

    public string ChronoState => ChronoRunning ? "çalışıyor" : ChronoHasTime ? "durdu" : "hazır";

    /// <summary>Son turlar, en yenisi üstte.</summary>
    public ObservableCollection<LapView> ChronoLaps { get; } = [];

    [ObservableProperty]
    private bool _hasChronoLaps;

    /// <summary>Saniyede bir (Bugün sayfası açıkken) ve kronometre değişince.</summary>
    public void ShowChronometer(TimeSpan elapsed, bool running, IReadOnlyList<ChronometerLap> laps)
    {
        ChronoClock = Chronometer.Clock(elapsed);
        ChronoRunning = running;
        ChronoHasTime = running || elapsed > TimeSpan.Zero;
        if (laps.Count != ChronoLaps.Count || (laps.Count > 0 && ChronoLaps[0].Number != $"{laps[^1].Number}."))
        {
            ChronoLaps.Clear();
            foreach (var lap in laps.Reverse().Take(5))
            {
                ChronoLaps.Add(new LapView($"{lap.Number}.", Chronometer.Clock(lap.Split), Chronometer.Clock(lap.Total)));
            }

            HasChronoLaps = ChronoLaps.Count > 0;
        }
    }

    /// <summary>Çalışan zamanlayıcılar, en yakın biteni üstte.</summary>
    public ObservableCollection<CountdownView> Countdowns { get; } = [];

    [ObservableProperty]
    private bool _hasCountdowns;

    [ObservableProperty]
    private string _countdownNote = "";

    /// <summary>Kurulacak zamanlayıcının adı (isteğe bağlı): "çay", "fırındaki kek"; dolunca bu adla haber verilir.</summary>
    [ObservableProperty]
    private string _countdownLabel = "";

    /// <summary>Saniyede bir: zamanlayıcıların kalan süreleri.</summary>
    public void ShowCountdowns(IReadOnlyList<Reminder> active, DateTime now)
    {
        var views = active.Select(item => new CountdownView(item.Id, item.Text.Length > 0 ? TurkishGrammar.Capitalize(item.Text) : "Zamanlayıcı",
            Chronometer.Clock(item.Due - now), Math.Clamp((now - item.Created).TotalSeconds / Math.Max(1, (item.Due - item.Created).TotalSeconds), 0, 1))).ToList();
        if (views.Count != Countdowns.Count || views.Where((view, index) => Countdowns[index].Id != view.Id).Any())
        {
            Countdowns.Clear();
            foreach (var view in views)
            {
                Countdowns.Add(view);
            }
        }
        else
        {
            for (var index = 0; index < views.Count; index++)
            {
                Countdowns[index] = views[index];
            }
        }

        HasCountdowns = Countdowns.Count > 0;
    }

    [RelayCommand]
    private void AddCountdown(string minutes)
    {
        if (double.TryParse(minutes, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            CountdownNote = _actions.StartTimer(value, CountdownLabel);
            CountdownLabel = "";
        }
    }

    [RelayCommand]
    private void CancelCountdown(CountdownView item)
    {
        _actions.DeleteReminder(item.Id);
        CountdownNote = "";
    }

    [RelayCommand]
    private void ChronoToggle() => _actions.ChronometerControl(ChronoRunning ? "durdur" : "baslat");

    [RelayCommand]
    private void ChronoLap() => _actions.ChronometerControl("tur");

    [RelayCommand]
    private void ChronoReset() => _actions.ChronometerControl("sifirla");
}
