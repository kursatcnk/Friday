using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Konuşulan zaman ifadelerini kesin bir ana çevirir: "yarın 9'da", "saat 15:30'da", "akşam 8'de", "öğleden sonra
/// 3'te", "pazartesi sabah", "yarın sabah", "2 saat sonra". Saat belirsizse ("3'te") bir sonraki uygun an seçilir:
/// öğleden sonra saat 2'de "3'te" demek 15:00 demektir.
/// </summary>
public static partial class TurkishTime
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Dictionary<string, DayOfWeek> Days = new()
    {
        ["pazartesi"] = DayOfWeek.Monday, ["salı"] = DayOfWeek.Tuesday, ["çarşamba"] = DayOfWeek.Wednesday,
        ["perşembe"] = DayOfWeek.Thursday, ["cuma"] = DayOfWeek.Friday, ["cumartesi"] = DayOfWeek.Saturday, ["pazar"] = DayOfWeek.Sunday
    };

    /// <summary>İfadedeki anı bulur; zaman yoksa null.</summary>
    public static DateTime? Parse(string phrase, DateTime now)
    {
        var text = Spaces().Replace(phrase.ToLower(Turkish).Trim(), " ");
        if (Relative().Match(text) is { Success: true } relative && QuickIntents.ParseNumber(relative.Groups["n"].Value) is { } amount)
        {
            return relative.Groups["u"].Value switch
            {
                "saniye" => now.AddSeconds(amount),
                "saat" => now.AddHours(amount),
                "gün" => now.AddDays(amount),
                _ => now.AddMinutes(amount)
            };
        }

        var match = Absolute().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var day = match.Groups["day"].Value;
        var part = match.Groups["part"].Value;
        var hasHour = int.TryParse(match.Groups["h"].Value, CultureInfo.InvariantCulture, out var hour);
        var minute = int.TryParse(match.Groups["m"].Value, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        if (!hasHour && day.Length == 0 && part.Length == 0)
        {
            return null;
        }

        if (!hasHour)
        {
            // Yalnızca gün ya da gün bölümü: "yarın" → 09:00, "akşam" → 20:00.
            hour = part switch { "öğle" or "öğlen" => 12, "öğleden sonra" => 15, "akşam" => 20, "gece" => 22, _ => 9 };
        }
        else
        {
            hour = part switch
            {
                "öğleden sonra" or "akşam" when hour < 12 => hour + 12,
                "gece" when hour is >= 6 and < 12 => hour + 12,
                "öğle" or "öğlen" when hour < 6 => hour + 12,
                _ => hour
            };
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return null;
        }

        if (day.Length > 0)
        {
            var date = day switch
            {
                "bugün" => now.Date,
                "yarın" => now.Date.AddDays(1),
                "öbür gün" or "ertesi gün" => now.Date.AddDays(2),
                _ => NextWeekday(now.Date, Days[day])
            };
            return date.AddHours(hour).AddMinutes(minute);
        }

        // Gün söylenmediyse bir sonraki uygun an: bugün, saat 12'den küçükse öğleden sonrası, değilse yarın.
        var candidates = new List<DateTime> { now.Date.AddHours(hour).AddMinutes(minute) };
        if (part.Length == 0 && hour < 12)
        {
            candidates.Add(now.Date.AddHours(hour + 12).AddMinutes(minute));
        }

        candidates.Add(now.Date.AddDays(1).AddHours(hour).AddMinutes(minute));
        return candidates.Where(candidate => candidate > now).Min();
    }

    /// <summary>"bugün 15:30", "yarın 09:00", "pazartesi 10:00", "23 Kasım 14:00".</summary>
    public static string Describe(DateTime when, DateTime now)
    {
        var clock = when.ToString("HH:mm", CultureInfo.InvariantCulture);
        var days = (when.Date - now.Date).Days;
        return days switch
        {
            0 => $"bugün {clock}",
            1 => $"yarın {clock}",
            > 1 and < 7 => $"{when.ToString("dddd", Turkish).ToLower(Turkish)} {clock}",
            _ => $"{when.ToString("d MMMM", Turkish)} {clock}"
        };
    }

    private static DateTime NextWeekday(DateTime today, DayOfWeek day)
    {
        var ahead = ((int)day - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(ahead == 0 ? 7 : ahead);
    }

    [GeneratedRegex(@"(?<n>[\p{L}\d,.]+(?: buçuk| [\p{L}]+)?) (?<u>saniye|dakika|saat|gün) (?:sonra|içinde)")]
    private static partial Regex Relative();

    // "yarın sabah 9'da", "pazartesi 10:30'da", "akşam 8'de", "saat 15.30'da", "öğleden sonra 3'te", "yarın"
    [GeneratedRegex(@"(?:^|\s)(?<day>bugün|yarın|öbür gün|ertesi gün|pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar)?(?: ?(?<part>sabah|öğleden sonra|öğlen|öğle|akşam|gece))?(?: ?(?:saat )?(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?(?:['’]?(?:de|da|te|ta|ye|ya|e|a))?)?(?=\s|$)")]
    private static partial Regex Absolute();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
