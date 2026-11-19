using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

public enum SpecialKind
{
    /// <summary>Kullanıcının kendi doğum günü.</summary>
    OwnBirthday,

    /// <summary>Bir yakınının doğum günü.</summary>
    Birthday,

    /// <summary>Evlilik, tanışma… yıldönümü.</summary>
    Anniversary,

    /// <summary>Resmî ya da bilinen gün (29 Ekim, yılbaşı…).</summary>
    Holiday,

    /// <summary>Hafızada tarihi geçen başka bir şey (muayene, son başvuru…).</summary>
    Other
}

/// <summary>Yaklaşan özel gün: tarihi, ikinci şahısla okunuşu ("annenin doğum günü") ve türü.</summary>
public sealed record SpecialDay(DateOnly Date, string Title, SpecialKind Kind);

/// <summary>
/// Önemli günler: hafızadaki doğum günleri ve yıldönümleri ile resmî/bilinen günler. Günün kendisinde sabah
/// karşılamasında kutlanır ("İyi ki doğdun!", "Bugün annenin doğum günü"), bir gün önce akşam hatırlatılır.
/// </summary>
public static partial class SpecialDays
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly (int Month, int Day, string Name, string Wish)[] Holidays =
    [
        (1, 1, "Yılbaşı", "Mutlu yıllar!"),
        (2, 14, "Sevgililer Günü", ""),
        (3, 8, "Dünya Kadınlar Günü", ""),
        (4, 23, "23 Nisan Ulusal Egemenlik ve Çocuk Bayramı", "Bayramın kutlu olsun!"),
        (5, 1, "Emek ve Dayanışma Günü", ""),
        (5, 19, "19 Mayıs Atatürk'ü Anma, Gençlik ve Spor Bayramı", "Bayramın kutlu olsun!"),
        (7, 15, "15 Temmuz Demokrasi ve Millî Birlik Günü", ""),
        (8, 30, "30 Ağustos Zafer Bayramı", "Bayramın kutlu olsun!"),
        (10, 29, "Cumhuriyet Bayramı", "Cumhuriyet Bayramın kutlu olsun!"),
        (11, 10, "Atatürk'ü Anma Günü", ""),
        (11, 24, "Öğretmenler Günü", "")
    ];

    /// <summary>Hafızadaki tarihli bilgiler ve resmî günler; bugünden itibaren <paramref name="days"/> gün içinde olanlar, yakından uzağa.</summary>
    public static IReadOnlyList<SpecialDay> Upcoming(IEnumerable<MemoryItem> memories, DateOnly today, int days)
    {
        var list = new List<SpecialDay>();
        foreach (var item in memories)
        {
            if (MemoryDates.NextOccurrence(item.Text, today) is { } date && date.DayNumber - today.DayNumber <= days)
            {
                list.Add(new SpecialDay(date, Title(item.Text), KindOf(item.Text)));
            }
        }

        foreach (var (month, day, name, _) in Holidays)
        {
            var year = new DateOnly(today.Year, month, day) >= today ? today.Year : today.Year + 1;
            var date = new DateOnly(year, month, day);
            if (date.DayNumber - today.DayNumber <= days)
            {
                list.Add(new SpecialDay(date, name, SpecialKind.Holiday));
            }
        }

        return [.. list.OrderBy(item => item.Date).ThenBy(item => item.Kind)];
    }

    public static SpecialKind KindOf(string text)
    {
        var lower = text.ToLower(Turkish);
        if (OwnBirthday().IsMatch(lower))
        {
            return SpecialKind.OwnBirthday;
        }

        return lower.Contains("doğum gün", StringComparison.Ordinal) ? SpecialKind.Birthday
            : lower.Contains("yıldönüm", StringComparison.Ordinal) || lower.Contains("yıl dönüm", StringComparison.Ordinal) ? SpecialKind.Anniversary
            : SpecialKind.Other;
    }

    /// <summary>
    /// Hafıza cümlesinden ikinci şahısla kısa ad: "Kullanıcının annesinin doğum günü 3 Mart'ta." → "annenin doğum günü".
    /// </summary>
    public static string Title(string text)
    {
        var clean = text.Trim().TrimEnd('.');
        foreach (var prefix in new[] { "Kullanıcının ", "Kullanıcı ", "Benim ", "Benim" })
        {
            if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                clean = clean[prefix.Length..];
                break;
            }
        }

        clean = DatePart().Replace(clean, " ").Trim().TrimEnd(',', '-', ':').Trim();
        clean = Whitespace().Replace(clean, " ");
        // "annesinin" → "annenin", "babasının" → "babanın": üçüncü şahıs iyelik eki ikinci şahsa çevrilir.
        clean = ThirdPersonGenitive().Replace(clean, match => match.Groups["stem"].Value + "n" + match.Groups["v"].Value + "n");
        return clean.Length == 0 ? text.Trim().TrimEnd('.') : char.ToLower(clean[0], Turkish) + clean[1..];
    }

    /// <summary>Günün kendisinde söylenecek kutlama; diğer türlerde null (onları sabah karşılaması zaten söyler).</summary>
    public static string? Greeting(SpecialDay day, string userName)
    {
        var name = userName.Trim();
        return day.Kind switch
        {
            SpecialKind.OwnBirthday => $"İyi ki doğdun{(name.Length > 0 ? " " + name : "")}! Yeni yaşın sağlık, mutluluk ve bol güzel anla dolsun.",
            SpecialKind.Birthday => $"Bugün {day.Title}! Aramayı ya da bir mesaj atmayı unutma.",
            SpecialKind.Anniversary => $"Bugün {day.Title}; kutlu olsun!",
            SpecialKind.Holiday => Holidays.FirstOrDefault(item => item.Name == day.Title).Wish is { Length: > 0 } wish
                ? $"Bugün {day.Title}. {wish}"
                : $"Bugün {day.Title}.",
            _ => null
        };
    }

    /// <summary>"3 gün sonra", "yarın", "bugün", "5 Aralık Cuma".</summary>
    public static string When(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "bugün",
        1 => "yarın",
        <= 6 and var left => $"{left} gün sonra",
        _ => date.ToString("d MMMM dddd", Turkish)
    };

    [GeneratedRegex(@"^(?:kullanıcının |benim )?doğum günü")]
    private static partial Regex OwnBirthday();

    [GeneratedRegex(@"(?<!\d)\d{1,2}\s*(?:ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık|Ocak|Şubat|Mart|Nisan|Mayıs|Haziran|Temmuz|Ağustos|Eylül|Ekim|Kasım|Aralık)(?:['’]?(?:da|de|ta|te|dır|dir|tır|tir))?(?:\s+20\d\d)?(?:['’]?(?:da|de|ta|te))?")]
    private static partial Regex DatePart();

    [GeneratedRegex(@"(?<stem>\p{L}+?[aeıioöuü])s(?<v>[ıiuü])n[ıiuü]n\b")]
    private static partial Regex ThirdPersonGenitive();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
