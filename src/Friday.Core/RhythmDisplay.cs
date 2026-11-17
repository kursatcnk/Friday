using System.Globalization;

namespace Friday.Core;

/// <summary>Ritim koçunun o anki durumu (panel saniyede bir buna bakıp geri sayımı çizer).</summary>
public sealed record RhythmSnapshot(
    bool Enabled,
    DateTime? StretchStart,
    DateTime? FocusStart,
    DateTime? FocusEnd,
    DateTime? BreakStart,
    TimeSpan BreakEvery,
    TimeSpan BreakLength,
    bool IsAway,
    DateTime? SnoozedUntil);

public enum RhythmMode
{
    Off,
    Ready,
    Working,
    Overdue,
    Focus,
    Break,
    Away
}

/// <summary>Ritim sayfasının büyük halkası: kip, saat, halkanın doluluğu ve sade bir açıklama.</summary>
public sealed record RhythmDisplay(RhythmMode Mode, string Label, string Clock, string ClockCaption, double Progress, string Sentence);

/// <summary>
/// Ritim durumunu insan diline çevirir: "Çalışıyorsun · 32:10 aralıksız · 50 dakikayı geçince mola öneririm". Saf mantık;
/// saat dışarıdan verilir.
/// </summary>
public static class RhythmText
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static RhythmDisplay Describe(RhythmSnapshot state, DateTime now)
    {
        if (!state.Enabled)
        {
            return new RhythmDisplay(RhythmMode.Off, "Kapalı", "—", "", 0,
                "Ritim koçu kapalı. Açarsan uzun süre ara vermeden çalıştığında uygun bir anda mola öneririm.");
        }

        if (state.BreakStart is { } breakStart)
        {
            var spent = now - breakStart;
            var left = state.BreakLength - spent;
            return left > TimeSpan.Zero
                ? new RhythmDisplay(RhythmMode.Break, "Moladasın", Clock(left), "kaldı", Ratio(spent, state.BreakLength),
                    $"{Minutes(state.BreakLength)} mola. Ekrandan uzaklaş; döndüğünde kaldığın yeri söylerim.")
                : new RhythmDisplay(RhythmMode.Break, "Mola bitti", "00:00", "hazırsan devam", 1,
                    "Mola süresi doldu. Hazır olduğunda devam edebilirsin; \"moladan döndüm\" demen yeter.");
        }

        if (state.FocusEnd is { } focusEnd)
        {
            var start = state.FocusStart ?? now;
            var left = focusEnd - now;
            return new RhythmDisplay(RhythmMode.Focus, "Odaktasın", Clock(left < TimeSpan.Zero ? TimeSpan.Zero : left), "kaldı",
                Ratio(now - start, focusEnd - start),
                $"Odak {focusEnd.ToString("HH:mm", Turkish)}'{TimeSuffix(focusEnd)} bitiyor. Bu sırada mola önermem; bitince haber veririm.");
        }

        if (state.IsAway)
        {
            return new RhythmDisplay(RhythmMode.Away, "Uzaktasın", "—", "", 0,
                "Bilgisayar başında değilsin; bu süre mola sayılıyor. Döndüğünde kaldığın yeri hatırlatırım.");
        }

        if (state.StretchStart is not { } stretchStart || now - stretchStart < TimeSpan.FromSeconds(30))
        {
            return new RhythmDisplay(RhythmMode.Ready, "Hazır", "00:00", "aralıksız", 0,
                $"Klavye ya da fareyi kullandıkça aralıksız süreyi sayarım. {Minutes(state.BreakEvery)} dolunca, yazmayı bıraktığın bir anda mola öneririm.");
        }

        var stretch = now - stretchStart;
        if (state.SnoozedUntil is { } snoozed && snoozed > now)
        {
            return new RhythmDisplay(RhythmMode.Working, "Çalışıyorsun", Clock(stretch), "aralıksız", Ratio(stretch, state.BreakEvery),
                $"Mola önerisini {snoozed.ToString("HH:mm", Turkish)}'{TimeSuffix(snoozed)} kadar erteledin.");
        }

        if (stretch >= state.BreakEvery)
        {
            return new RhythmDisplay(RhythmMode.Overdue, "Mola zamanı", Clock(stretch), "aralıksız", 1,
                $"{Minutes(stretch)} ara vermeden çalıştın. Kalkıp biraz dolaşmanın tam zamanı.");
        }

        return new RhythmDisplay(RhythmMode.Working, "Çalışıyorsun", Clock(stretch), "aralıksız", Ratio(stretch, state.BreakEvery),
            $"Molaya {Minutes(state.BreakEvery - stretch)} var. {Minutes(state.BreakEvery)} dolunca, yazmayı bıraktığın bir anda öneririm.");
    }

    /// <summary>"07:05", "1:02:09".</summary>
    public static string Clock(TimeSpan span)
    {
        var seconds = Math.Max(0, (long)Math.Floor(span.TotalSeconds));
        var hours = seconds / 3600;
        var minutes = seconds / 60 % 60;
        var rest = seconds % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{rest:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{rest:00}");
    }

    /// <summary>"45 dk", "1 sa 20 dk" (kartlarda kısa yazım).</summary>
    public static string Compact(double minutes) => minutes < 60
        ? $"{Math.Round(minutes)} dk"
        : Math.Round(minutes % 60) is var rest && rest >= 1 ? $"{(int)(minutes / 60)} sa {rest} dk" : $"{(int)(minutes / 60)} sa";

    /// <summary>"09:30" → "da", "15:00" → "te", "10:40" → "ta": saatin okunuşuna göre bulunma eki.</summary>
    public static string TimeSuffix(DateTime time)
    {
        // Saat "10:45" diye yazılıp "on kırk beş" diye okunur; ek son okunan sayıya uyar.
        var spoken = time.Minute == 0 ? time.Hour : time.Minute;
        return (spoken % 10) switch
        {
            0 => spoken switch { 20 or 50 => "de", 40 => "ta", _ => "da" },
            1 or 2 or 7 or 8 => "de",
            3 or 4 or 5 => "te",
            _ => "da"
        };
    }

    private static string Minutes(TimeSpan span) => TurkishGrammar.Duration(Math.Max(1, Math.Round(span.TotalMinutes)));

    private static double Ratio(TimeSpan part, TimeSpan whole) =>
        whole <= TimeSpan.Zero ? 0 : Math.Clamp(part.TotalSeconds / whole.TotalSeconds, 0, 1);
}
