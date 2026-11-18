using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Kartta bir gösterge: etiket, 0–1 arası doluluk ve yanında yazı ("İşlemci", 0,13, "%13").</summary>
public sealed record CardGauge(string Label, double Value, string Text);

/// <summary>Kartta bir satır: solda etiket, sağda değer.</summary>
public sealed record CardRow(string Label, string Value);

/// <summary>
/// Cevabın görsel hali: kapsülün üstünde beliren küçük cam kart (hava, durum raporu, döviz, hatırlatmalar…). Kart
/// yalnızca gösterir; cevap yine sesli okunur.
/// </summary>
public sealed record AnswerCard(string Title, string Glyph)
{
    /// <summary>Büyük yazılan değer ("15°", "03:12").</summary>
    public string? Big { get; init; }

    public string? BigCaption { get; init; }

    public IReadOnlyList<CardGauge> Gauges { get; init; } = [];

    public IReadOnlyList<CardRow> Rows { get; init; } = [];

    public bool HasBig => Big is not null;

    public bool HasGauges => Gauges.Count > 0;

    public bool HasRows => Rows.Count > 0;
}

public sealed partial class AssistantTools
{
    private const int MaxCardRows = 6;
    private AnswerCard? _card;

    /// <summary>Son araç çalışmasının kartı; bir kez alınır.</summary>
    public AnswerCard? TakeCard() => Interlocked.Exchange(ref _card, null);

    private void ShowCard(AnswerCard card) => _card = card;

    private static string Degrees(double value) => $"{Math.Round(value).ToString(CultureInfo.InvariantCulture)}°";

    private void ShowWeatherCard(Forecast forecast, string focus)
    {
        var glyph = forecast.Code <= 1 ? "" : "";
        if (focus is "hafta" or "haftasonu")
        {
            ShowCard(new AnswerCard(forecast.Place, glyph)
            {
                Rows = [.. forecast.Days.Take(MaxCardRows + 1).Select(day => new CardRow(
                    day.Date.ToString("dddd", Turkish),
                    $"{Degrees(day.Min)} – {Degrees(day.Max)} · {WeatherCodes.Describe(day.Code)}{(day.RainChance >= 30 ? $" · %{Math.Round(day.RainChance)}" : "")}"))]
            });
            return;
        }

        var tomorrow = focus == "yarin";
        var day = forecast.Days[Math.Min(tomorrow ? 1 : 0, forecast.Days.Count - 1)];
        ShowCard(new AnswerCard(tomorrow ? $"{forecast.Place} · yarın" : forecast.Place, glyph)
        {
            Big = tomorrow ? $"{Degrees(day.Max)}" : Degrees(forecast.Temperature),
            BigCaption = WeatherCodes.Describe(tomorrow ? day.Code : forecast.Code),
            Rows =
            [
                new CardRow("En yüksek / en düşük", $"{Degrees(day.Max)} / {Degrees(day.Min)}"),
                new CardRow("Yağış ihtimali", $"%{Math.Round(day.RainChance)}"),
                .. tomorrow ? Array.Empty<CardRow>() : [new CardRow("Hissedilen", Degrees(forecast.Feels))],
            ]
        });
    }

    private void ShowRatesCard(Dictionary<string, double> rates, IEnumerable<string> codes, string source)
    {
        var rows = codes.Where(rates.ContainsKey)
            .Select(code => new CardRow(TurkishGrammar.Capitalize(CurrencyLabel(code)), $"{rates[code].ToString(rates[code] >= 10 ? "0.00" : "0.000", Turkish)} ₺"))
            .ToList();
        if (rows.Count > 0)
        {
            ShowCard(new AnswerCard("Döviz", "") { Rows = rows, BigCaption = source });
        }
    }

    private void ShowStatusCard(double cpu, int memory, GpuReading? gpu)
    {
        var gauges = new List<CardGauge>
        {
            new("İşlemci", cpu / 100, $"%{Math.Round(cpu)}"),
            new("Bellek", memory / 100.0, $"%{memory}"),
        };
        if (gpu is not null)
        {
            gauges.Add(new CardGauge("Ekran kartı", gpu.Temperature / 100.0, $"{gpu.Temperature}°"));
        }

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive is { IsReady: true, DriveType: DriveType.Fixed }).Take(3))
        {
            var used = 1 - (drive.AvailableFreeSpace / (double)drive.TotalSize);
            gauges.Add(new CardGauge($"{drive.Name.TrimEnd('\\')} diski", used, $"{drive.AvailableFreeSpace / 1024 / 1024 / 1024} GB boş"));
        }

        ShowCard(new AnswerCard("Durum raporu", "") { Gauges = gauges });
    }

    private void ShowListCard(string title, string glyph, IEnumerable<CardRow> rows)
    {
        var list = rows.Take(MaxCardRows).ToList();
        if (list.Count > 0)
        {
            ShowCard(new AnswerCard(title, glyph) { Rows = list });
        }
    }
}
