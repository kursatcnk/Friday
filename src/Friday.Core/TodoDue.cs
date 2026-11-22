using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Bir işin metninden son tarihi ve önemini çıkarır: "cumaya kadar raporu bitir" → ("Raporu bitir", cuma), "yarın bankaya
/// git" → ("Bankaya git", yarın), "acil: faturayı öde" → önemli. Tarih söylenmediyse null.
/// </summary>
public static partial class TodoDue
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly string[] Weekdays = ["pazar", "pazartesi", "salı", "çarşamba", "perşembe", "cuma", "cumartesi"];

    public static (string Text, DateOnly? Due, bool Important) Extract(string text, DateTime now)
    {
        var clean = text.Trim().TrimEnd('.', '!');
        var important = false;
        if (ImportantMark().Match(clean) is { Success: true } mark)
        {
            important = true;
            clean = clean.Remove(mark.Index, mark.Length).Trim().TrimStart(',', ':').Trim();
        }

        var today = DateOnly.FromDateTime(now);
        DateOnly? due = null;
        if (DeadlinePhrase().Match(clean) is { Success: true } deadline && Day(deadline.Groups["d"].Value, today) is { } until)
        {
            due = until;
            clean = clean.Remove(deadline.Index, deadline.Length);
        }
        else if (OnDate().Match(clean) is { Success: true } dated && Day(dated.Groups["d"].Value, today) is { } onDate)
        {
            // "3 Aralık'ta doktora git", "kirayı ayın 5'inde öde"
            due = onDate;
            clean = clean.Remove(dated.Index, dated.Length);
        }
        else if (Within().Match(clean) is { Success: true } within)
        {
            // "2 gün içinde dönüş yap", "bu hafta içinde bankaya git"
            due = within.Groups["n"].Success && QuickIntents.ParseNumber(within.Groups["n"].Value) is { } days
                ? today.AddDays((int)days)
                : Next(today, DayOfWeek.Sunday);
            clean = clean.Remove(within.Index, within.Length);
        }
        else if (LeadingDay().Match(clean) is { Success: true } lead && Day(lead.Groups["d"].Value, today) is { } on)
        {
            due = on;
            clean = clean[lead.Length..];
        }
        else if (InlineDay().Match(clean) is { Success: true } inline && Day(inline.Groups["d"].Value, today) is { } day)
        {
            // "sunumu bugün hazırla", "raporu yarın gönder"
            due = day;
            clean = clean.Remove(inline.Index, inline.Length);
        }

        clean = Spaces().Replace(clean, " ").Trim().Trim(',').Trim();
        return (clean.Length == 0 ? text.Trim() : clean, due, important);
    }

    /// <summary>"bugün", "yarın", "cuma", "hafta sonu", "ay sonu", "25 kasım" → tarih (bugün ya da sonrası).</summary>
    public static DateOnly? Day(string spoken, DateOnly today)
    {
        var word = spoken.Trim().ToLower(Turkish);
        switch (word)
        {
            case "bugün" or "akşam" or "bu akşam":
                return today;
            case "yarın":
                return today.AddDays(1);
            case "öbür gün" or "ertesi gün":
                return today.AddDays(2);
            case "hafta sonu" or "haftasonu":
                return Next(today, DayOfWeek.Saturday);
            case "haftaya" or "gelecek hafta" or "önümüzdeki hafta":
                return today.AddDays(7);
            case "ay sonu" or "ayın sonu":
                return new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        }

        if (MonthDay().Match(word) is { Success: true } monthly)
        {
            // "ayın 15'i": bu ayın 15'i geçmediyse o, geçtiyse gelecek ayınki.
            var dayOfMonth = int.Parse(monthly.Groups["n"].Value, CultureInfo.InvariantCulture);
            for (var ahead = 0; ahead < 3; ahead++)
            {
                var first = new DateOnly(today.Year, today.Month, 1).AddMonths(ahead);
                if (dayOfMonth >= 1 && dayOfMonth <= DateTime.DaysInMonth(first.Year, first.Month) && first.AddDays(dayOfMonth - 1) is var date && date >= today)
                {
                    return date;
                }
            }

            return null;
        }

        var index = Array.IndexOf(Weekdays, word);
        if (index >= 0)
        {
            return Next(today, (DayOfWeek)index);
        }

        return MemoryDates.NextOccurrence(word, today);
    }

    /// <summary>İş listesinde görünen kısa etiket: "Gecikti", "Bugün", "Yarın", "Cuma", "25 Kas"; tarih yoksa boş.</summary>
    public static string Label(DateOnly? due, DateOnly today)
    {
        if (due is not { } day)
        {
            return "";
        }

        var left = day.DayNumber - today.DayNumber;
        return left switch
        {
            < 0 => "Gecikti",
            0 => "Bugün",
            1 => "Yarın",
            <= 6 => TurkishGrammar.Capitalize(day.ToString("dddd", Turkish)),
            _ => day.ToString("d MMM", Turkish)
        };
    }

    private static DateOnly Next(DateOnly today, DayOfWeek day)
    {
        var offset = ((int)day - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(offset);
    }

    [GeneratedRegex(@"^(?:önemli|acil)\s*[:,]?\s*|\s*[,(]?\s*\b(?:önemli|acil)\)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ImportantMark();

    // "cumaya kadar", "yarına kadar", "25 kasıma kadar", "hafta sonuna kadar", "ay sonuna kadar"
    // "haftaya salıya kadar", "yarın akşama kadar", "ayın 30'una kadar" da: önek ve gün bölümü tarihle birlikte atılır.
    [GeneratedRegex(@"\b(?:(?:haftaya|gelecek) )?(?<d>bugün|yarın|öbür gün|hafta ?sonu|ay sonu|ayın sonu|haftaya|gelecek hafta|pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar|ayın \d{1,2}|\d{1,2} (?:ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık))(?:['’]?(?:ya|ye|na|ne|a|e|ün|un|in|ın|ü|u|i|ı|una|üne|ine|ına|sına|sine))?(?: (?:sabah|öğlen|öğle|akşam|gece)(?:a|e|ya|ye)?)? kadar\b", RegexOptions.IgnoreCase)]
    private static partial Regex DeadlinePhrase();

    // "yarın bankaya git", "cuma günü raporu gönder"
    [GeneratedRegex(@"^(?<d>bugün|yarın|öbür gün|pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar)(?: günü)? ", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingDay();

    [GeneratedRegex(@"(?<=\s)(?<d>bugün|yarın)(?=\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex InlineDay();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Bulunma ekli tarih: "3 aralık'ta", "ayın 5'inde".</summary>
    [GeneratedRegex(@"\b(?<d>ayın \d{1,2}|\d{1,2} (?:ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık))['’]?(?:ta|te|da|de|inde|ında|unda|ünde|nde|nda)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OnDate();

    [GeneratedRegex(@"\b(?:(?<n>\d{1,2}|bir|iki|üç|dört|beş|altı|yedi|on) gün içinde|bu hafta içinde)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Within();

    [GeneratedRegex(@"^ayın (?<n>\d{1,2})$")]
    private static partial Regex MonthDay();
}
