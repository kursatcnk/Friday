using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;

namespace Friday.Desktop;

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

    [RelayCommand]
    private void ChronoToggle() => _actions.ChronometerControl(ChronoRunning ? "durdur" : "baslat");

    [RelayCommand]
    private void ChronoLap() => _actions.ChronometerControl("tur");

    [RelayCommand]
    private void ChronoReset() => _actions.ChronometerControl("sifirla");
}
