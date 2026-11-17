using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Hafızadaki tarihleri bulur: "arabanın muayenesi 20 Kasım'da", "kızının doğum günü 3 Mart". Sıradaki yıldönümüne
/// kaç gün kaldığı panelde rozet olarak görünür ve sabah brifinginde yaklaşanlar söylenir.
/// </summary>
public static partial class MemoryDates
{
    private static readonly string[] Months =
        ["ocak", "şubat", "mart", "nisan", "mayıs", "haziran", "temmuz", "ağustos", "eylül", "ekim", "kasım", "aralık"];

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Metindeki ilk "gün ay" tarihinin bugünden sonraki (bugün dahil) ilk günü; tarih yoksa null.</summary>
    public static DateOnly? NextOccurrence(string text, DateOnly today)
    {
        var match = DayMonth().Match(text.ToLower(Turkish));
        if (!match.Success || !int.TryParse(match.Groups["d"].Value, CultureInfo.InvariantCulture, out var day))
        {
            return null;
        }

        var month = Array.IndexOf(Months, match.Groups["m"].Value) + 1;
        if (month == 0 || day < 1 || day > 31)
        {
            return null;
        }

        int? year = int.TryParse(match.Groups["y"].Value, CultureInfo.InvariantCulture, out var explicitYear) ? explicitYear : null;
        foreach (var candidate in (year is { } fixedYear ? [fixedYear] : new[] { today.Year, today.Year + 1 }))
        {
            if (day <= DateTime.DaysInMonth(candidate, month) && new DateOnly(candidate, month, day) is var date && date >= today)
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>Panel rozeti: "Bugün", "Yarın", "3 gün kaldı"; 60 günden uzaksa ya da tarih yoksa null.</summary>
    public static string? Countdown(string text, DateOnly today)
    {
        if (NextOccurrence(text, today) is not { } date)
        {
            return null;
        }

        var days = date.DayNumber - today.DayNumber;
        return days switch
        {
            0 => "Bugün",
            1 => "Yarın",
            <= 60 => $"{days} gün kaldı",
            _ => null
        };
    }

    [GeneratedRegex(@"(?<!\d)(?<d>\d{1,2})\s*(?<m>ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık)(?:\s+(?<y>20\d\d))?")]
    private static partial Regex DayMonth();
}
