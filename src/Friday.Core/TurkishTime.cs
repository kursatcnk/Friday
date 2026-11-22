using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Konuşulan zaman ifadelerini kesin bir ana çevirir: "yarın 9'da", "saat 15:30'da", "akşam 8'de", "öğleden sonra
/// 3'te", "pazartesi sabah", "yarın sabah", "2 saat sonra", "üç buçukta", "beşe çeyrek kala", "23 Kasım'da",
/// "ayın 15'inde", "hafta sonu". Saat belirsizse ("3'te") bir sonraki uygun an seçilir: öğleden sonra saat 2'de "3'te"
/// demek 15:00 demektir.
/// </summary>
public static partial class TurkishTime
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Dictionary<string, DayOfWeek> Days = new()
    {
        ["pazartesi"] = DayOfWeek.Monday, ["salı"] = DayOfWeek.Tuesday, ["çarşamba"] = DayOfWeek.Wednesday,
        ["perşembe"] = DayOfWeek.Thursday, ["cuma"] = DayOfWeek.Friday, ["cumartesi"] = DayOfWeek.Saturday, ["pazar"] = DayOfWeek.Sunday
    };

    private static readonly string[] Months =
        ["ocak", "şubat", "mart", "nisan", "mayıs", "haziran", "temmuz", "ağustos", "eylül", "ekim", "kasım", "aralık"];

    /// <summary>Sayı sözcükleri ve yumuşamış halleri ("dörde", "dördü").</summary>
    private static readonly (string Stem, int Value)[] NumberWords =
    [
        ("sıfır", 0), ("dokuz", 9), ("sekiz", 8), ("yirmi", 20), ("otuz", 30), ("dört", 4), ("dörd", 4), ("altı", 6), ("yedi", 7),
        ("elli", 50), ("kırk", 40), ("bir", 1), ("iki", 2), ("beş", 5), ("üç", 3), ("on", 10)
    ];

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

        text = Spoken(text);
        DateTime? fixedDate = null;
        if (DateIn(text, now) is var (date, remainder))
        {
            fixedDate = date;
            text = Spaces().Replace(remainder, " ").Trim();
        }

        var match = Absolute().Match(text);
        if (!match.Success && fixedDate is null)
        {
            return null;
        }

        var day = match.Success ? match.Groups["day"].Value : "";
        var part = match.Success ? match.Groups["part"].Value : "";
        var hasHour = int.TryParse(match.Groups["h"].Value, CultureInfo.InvariantCulture, out var hour);
        var minute = int.TryParse(match.Groups["m"].Value, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        if (!hasHour && day.Length == 0 && part.Length == 0 && fixedDate is null)
        {
            return null;
        }

        if (!hasHour)
        {
            // Yalnızca gün ya da gün bölümü: "yarın" → 09:00, "akşam" → 20:00, "hafta sonu" → 10:00.
            hour = part switch
            {
                "öğle" or "öğlen" => 12, "öğleden sonra" => 15, "akşamüstü" or "akşam üstü" => 17, "akşam" => 20, "gece" => 22,
                _ => day == "hafta sonu" ? 10 : 9
            };
        }
        else
        {
            hour = part switch
            {
                "öğleden sonra" or "akşam" or "akşamüstü" or "akşam üstü" when hour < 12 => hour + 12,
                "gece" when hour is >= 6 and < 12 => hour + 12,
                "öğle" or "öğlen" when hour < 6 => hour + 12,
                _ => hour
            };
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return null;
        }

        if (fixedDate is { } on)
        {
            return on.AddHours(hour).AddMinutes(minute);
        }

        if (day == "hafta sonu")
        {
            // Şimdiden sonraki ilk cumartesi ya da pazar.
            return Enumerable.Range(0, 8).Select(ahead => now.Date.AddDays(ahead).AddHours(hour).AddMinutes(minute))
                .First(candidate => candidate > now && candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        }

        if (day.Length > 0)
        {
            var date2 = day switch
            {
                "bugün" => now.Date,
                "yarın" => now.Date.AddDays(1),
                "öbür gün" or "ertesi gün" => now.Date.AddDays(2),
                _ => NextWeekday(now.Date, Days[day])
            };
            return date2.AddHours(hour).AddMinutes(minute);
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

    /// <summary>
    /// Konuşmadaki saat söylenişini rakama çevirir: "dokuzda" → "9'da", "on bir buçukta" → "11:30", "beşe çeyrek kala" →
    /// "4:45", "dokuzu on geçe" → "9:10", "dörde beş var" → "3:55". Ölçümde bunların hiçbiri anlaşılmıyordu;
    /// "yarın on bir buçukta" sessizce 09:00'a kuruluyordu.
    /// </summary>
    internal static string Spoken(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        for (var index = 0; index < words.Length; index++)
        {
            if (Number(words[index]) is not { } first)
            {
                result.Add(words[index]);
                continue;
            }

            var start = index;
            var value = first.Value;
            var suffix = first.Suffix;
            // "on bir", "yirmi beş": onlar basamağından sonra gelen birler basamağı.
            if (suffix.Length == 0 && value >= 10 && value % 10 == 0 && index + 1 < words.Length && Number(words[index + 1]) is { Value: > 0 and < 10 } unit)
            {
                value += unit.Value;
                suffix = unit.Suffix;
                index++;
            }

            // Yalnızca saat gibi kullanılan sayı çevrilir: ekli ("dokuzda"), ardından "buçuk/çeyrek/geçe/kala/var" gelen ya da
            // önünde "saat", "sabah", gün adı olan. "Bir ara" saat 1 değildir.
            var next = index + 1 < words.Length ? words[index + 1] : "";
            var previous = start > 0 ? words[start - 1] : "";
            if (suffix.Length > 0 || ClockNext().IsMatch(next) || ClockPrevious().IsMatch(previous))
            {
                result.Add(value.ToString(CultureInfo.InvariantCulture) + (suffix.Length > 0 ? "'" + suffix : ""));
            }
            else
            {
                result.AddRange(words[start..(index + 1)]);
            }
        }

        var spoken = string.Join(' ', result);
        spoken = HalfPast().Replace(spoken, match => Clock(Int(match, "h"), 30));
        spoken = QuarterTo().Replace(spoken, match => Clock(Int(match, "h") - 1, 45));
        spoken = QuarterPast().Replace(spoken, match => Clock(Int(match, "h"), 15));
        spoken = MinutesPast().Replace(spoken, match => Clock(Int(match, "h"), Int(match, "m")));
        spoken = MinutesTo().Replace(spoken, match => Clock(Int(match, "h") - 1, 60 - Int(match, "m")));
        return spoken;
    }

    private static int Int(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private static string Clock(int hour, int minute) =>
        string.Create(CultureInfo.InvariantCulture, $"{(hour + 24) % 24}:{minute:00}");

    /// <summary>Sayı sözcüğü ve eki ("dokuzda" → 9, "da"); sayı değilse null.</summary>
    private static (int Value, string Suffix)? Number(string word)
    {
        foreach (var (stem, value) in NumberWords)
        {
            if (word.StartsWith(stem, StringComparison.Ordinal) && NumberSuffix().IsMatch(word[stem.Length..]))
            {
                return (value, word[stem.Length..].TrimStart('\'', '’'));
            }
        }

        return null;
    }

    /// <summary>"23 Kasım", "2 Aralık 2027", "ayın 15'i": söylenen tarih ve ifadenin geri kalanı; tarih yoksa null.</summary>
    private static (DateTime Date, string Remainder)? DateIn(string text, DateTime now)
    {
        // Dil modeli bazen tarihi sayıyla yazar: "2026-11-23 09:00", "23.11.2026 09:00".
        if (NumericDate().Match(text) is { Success: true } numeric)
        {
            var (year, month, day) = numeric.Groups["iy"].Success
                ? (Int(numeric, "iy"), Int(numeric, "im"), Int(numeric, "id"))
                : (Int(numeric, "y"), Int(numeric, "mo"), Int(numeric, "d"));
            return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
                ? (new DateTime(year, month, day), text.Remove(numeric.Index, numeric.Length))
                : null;
        }

        if (MonthDate().Match(text) is { Success: true } named)
        {
            var month = Array.IndexOf(Months, named.Groups["mon"].Value) + 1;
            var dayOfMonth = Int(named, "d");
            var year = named.Groups["y"].Success ? Int(named, "y") : now.Year;
            if (dayOfMonth < 1 || dayOfMonth > DateTime.DaysInMonth(year, month))
            {
                return null;
            }

            var date = new DateTime(year, month, dayOfMonth);
            if (!named.Groups["y"].Success && date < now.Date)
            {
                date = date.AddYears(1);
            }

            return (date, text.Remove(named.Index, named.Length));
        }

        if (MonthDay().Match(text) is { Success: true } monthly)
        {
            var dayOfMonth = Int(monthly, "d");
            for (var ahead = 0; ahead < 3; ahead++)
            {
                var first = new DateTime(now.Year, now.Month, 1).AddMonths(ahead);
                if (dayOfMonth >= 1 && dayOfMonth <= DateTime.DaysInMonth(first.Year, first.Month) && first.AddDays(dayOfMonth - 1) is var date && date > now.Date)
                {
                    return (date, text.Remove(monthly.Index, monthly.Length));
                }
            }
        }

        return null;
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

    // "yarın sabah 9'da", "pazartesi 10:30'da", "akşam 8'de", "saat 15.30'da", "öğleden sonra 3'te", "yarın", "hafta sonu"
    [GeneratedRegex(@"(?:^|\s)(?<day>bugün|yarın|öbür gün|ertesi gün|hafta sonu|pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar)?(?: ?(?<part>sabah|öğleden sonra|öğlen|öğle|akşamüstü|akşam üstü|akşam|gece))?(?: ?(?:saat )?(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?(?:['’]?(?:de|da|te|ta|ye|ya|e|a))?)?(?=\s|$)")]
    private static partial Regex Absolute();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Sayı sözcüğünün arkasına gelebilen ekler: "dokuzda", "dörde", "beşi", "on beşinde", "altıya".</summary>
    [GeneratedRegex(@"^(?:['’]?(?:de|da|te|ta|e|a|ye|ya|i|ı|u|ü|yi|yı|yu|yü|inde|ında|unda|ünde|nde|nda|ine|ına|une|üne|ni|nı|nu|nü))?$")]
    private static partial Regex NumberSuffix();

    [GeneratedRegex(@"^(?:buçuk\p{L}*|çeyrek|geçe|kala|var|\d{1,2}|on|yirmi|otuz|kırk|elli|beş|on beş|yirmi beş)$")]
    private static partial Regex ClockNext();

    [GeneratedRegex(@"^(?:saat|sabah|öğle|öğlen|sonra|akşam|akşamüstü|gece|bugün|yarın|gün|pazartesi|salı|çarşamba|perşembe|cuma|cumartesi|pazar)$")]
    private static partial Regex ClockPrevious();

    [GeneratedRegex(@"\b(?<h>\d{1,2}) buçuk(?:['’]?(?:ta|da|a|e))?(?!\s+(?:saat|dakika|gün|saniye))")]
    private static partial Regex HalfPast();

    [GeneratedRegex(@"\b(?<h>\d{1,2})['’]?(?:e|a|ye|ya) çeyrek (?:kala|var)")]
    private static partial Regex QuarterTo();

    [GeneratedRegex(@"\b(?<h>\d{1,2})['’]?(?:i|ı|u|ü|yi|yı|yu|yü) çeyrek geçe")]
    private static partial Regex QuarterPast();

    [GeneratedRegex(@"\b(?<h>\d{1,2})['’]?(?:i|ı|u|ü|yi|yı|yu|yü) (?<m>\d{1,2}) geçe")]
    private static partial Regex MinutesPast();

    [GeneratedRegex(@"\b(?<h>\d{1,2})['’]?(?:e|a|ye|ya) (?<m>\d{1,2}) (?:kala|var)")]
    private static partial Regex MinutesTo();

    [GeneratedRegex(@"\b(?<d>\d{1,2})\s+(?<mon>ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık)(?:['’]?\p{L}*)?(?:\s+(?<y>\d{4}))?")]
    private static partial Regex MonthDate();

    [GeneratedRegex(@"\bayın\s+(?<d>\d{1,2})(?:['’]?\p{L}*)?")]
    private static partial Regex MonthDay();

    [GeneratedRegex(@"\b(?:(?<iy>\d{4})-(?<im>\d{1,2})-(?<id>\d{1,2})|(?<d>\d{1,2})[./](?<mo>\d{1,2})[./](?<y>\d{4}))\b")]
    private static partial Regex NumericDate();
}
