using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Geçmiş bir günün Friday'in kayıtlarındaki izleri.</summary>
public sealed record DayFacts(DateOnly Day)
{
    /// <summary>Bilgisayar başında olunan ilk ve son an (gece yarısını geçebilir).</summary>
    public (TimeSpan Start, TimeSpan End)? Span { get; init; }

    public double ActiveMinutes { get; init; }

    public double FocusMinutes { get; init; }

    public IReadOnlyList<(string App, int Seconds)> Apps { get; init; } = [];

    public IReadOnlyList<string> Finished { get; init; } = [];

    public IReadOnlyList<string> Habits { get; init; } = [];

    public decimal Spent { get; init; }

    public string? TopCategory { get; init; }

    public IReadOnlyList<string> Journal { get; init; } = [];

    public IReadOnlyList<string> Notes { get; init; } = [];

    public IReadOnlyList<string> Chats { get; init; } = [];

    public bool IsEmpty => Span is null && ActiveMinutes < 1 && Apps.Count == 0 && Finished.Count == 0 && Habits.Count == 0
        && Spent == 0 && Journal.Count == 0 && Notes.Count == 0 && Chats.Count == 0;
}

/// <summary>
/// "Geçen salı ne yaptım?": geçmiş bir günü Friday'in kendi kayıtlarından (bilgisayar başındaki saatler, en çok
/// kullanılan uygulamalar, biten işler, odak, alışkanlıklar, harcamalar, günlük, notlar, sohbetler) birkaç cümleyle
/// anlatır. Ekran görüntüsü ya da içerik yok; yalnızca zaten tutulan kayıtlar.
/// </summary>
public static partial class DayStory
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly string[] Weekdays = ["pazar", "pazartesi", "salı", "çarşamba", "perşembe", "cuma", "cumartesi"];

    private static readonly string[] Months = ["ocak", "şubat", "mart", "nisan", "mayıs", "haziran", "temmuz", "ağustos", "eylül", "ekim", "kasım", "aralık"];

    /// <summary>"dün", "önceki gün", "3 gün önce", "geçen salı", "salı", "18 kasım" → geçmişteki gün; anlaşılmazsa null.</summary>
    public static DateOnly? Parse(string text, DateOnly today)
    {
        var said = text.Trim().ToLower(Turkish);
        said = said.EndsWith(" günü", StringComparison.Ordinal) ? said[..^5] : said;
        switch (said)
        {
            case "bugün":
                return today;
            case "dün":
                return today.AddDays(-1);
            case "önceki gün" or "evvelsi gün" or "öbür gün" or "dünden önceki gün":
                return today.AddDays(-2);
        }

        if (DaysAgo().Match(said) is { Success: true } ago && int.TryParse(ago.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days is > 0 and <= 400)
        {
            return today.AddDays(-days);
        }

        if (WeekdayWords().Match(said) is { Success: true } named && Array.IndexOf(Weekdays, named.Groups["w"].Value) is var weekday and >= 0)
        {
            var back = ((int)today.DayOfWeek - weekday + 7) % 7;
            // "Salı" bugünse bugündür; "geçen salı" her zaman geçmişteki ilk salı, "geçen hafta salı" bir önceki haftanınki.
            if (named.Groups["gecen"].Success && back == 0)
            {
                back = 7;
            }

            if (named.Groups["hafta"].Success)
            {
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                return monday.AddDays(-7).AddDays((weekday + 6) % 7);
            }

            return today.AddDays(-back);
        }

        if (DayMonth().Match(said) is { Success: true } date && Array.IndexOf(Months, date.Groups["m"].Value) is var month and >= 0
            && int.TryParse(date.Groups["d"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var dayOfMonth)
            && dayOfMonth >= 1 && dayOfMonth <= DateTime.DaysInMonth(today.Year, month + 1))
        {
            var candidate = new DateOnly(today.Year, month + 1, dayOfMonth);
            return candidate > today ? candidate.AddYears(-1) : candidate;
        }

        return null;
    }

    /// <summary>"Dün", "Salı günü (18 Kasım)", "3 Kasım Pazartesi".</summary>
    public static string Label(DateOnly day, DateOnly today)
    {
        var back = today.DayNumber - day.DayNumber;
        var weekday = TurkishGrammar.Capitalize(day.ToString("dddd", Turkish));
        return back switch
        {
            0 => "Bugün",
            1 => "Dün",
            < 7 => $"{weekday} günü ({day.ToString("d MMMM", Turkish)})",
            _ => $"{day.ToString("d MMMM", Turkish)} {weekday}"
        };
    }

    /// <summary>Günün birkaç cümlelik anlatımı: önce nerede ve ne kadar, sonra ne bitti, sonra yaşam (alışkanlık, harcama, günlük).</summary>
    public static string Tell(DayFacts facts, DateOnly today)
    {
        var label = Label(facts.Day, today);
        if (facts.IsEmpty)
        {
            return $"{label} için hiçbir kaydım yok; Friday kapalıydı ya da bilgisayar kullanılmamış.";
        }

        var sentences = new List<string>();
        var presence = facts.Span is { } span
            ? $"{label} {Clock(span.Start)} ile {Clock(span.End)} arasında bilgisayar başındaydın"
            : $"{label}";
        if (facts.ActiveMinutes >= 1)
        {
            presence += facts.Span is null ? $" toplam {TurkishGrammar.Duration(facts.ActiveMinutes)} bilgisayar başındaydın" : $", toplam {TurkishGrammar.Duration(facts.ActiveMinutes)}";
        }

        if (facts.Span is not null || facts.ActiveMinutes >= 1)
        {
            sentences.Add(presence + ".");
        }

        var apps = facts.Apps.Where(app => app.Seconds >= 300).Take(3).ToList();
        if (apps.Count > 0)
        {
            sentences.Add("En çok kullandıkların: " + string.Join(", ", apps.Select(app => $"{app.App} {TurkishGrammar.Duration(app.Seconds / 60.0)}")) + ".");
        }

        var work = new List<string>();
        if (facts.Finished.Count > 0)
        {
            var named = string.Join(", ", facts.Finished.Take(3).Select(task => task.ToLower(Turkish)));
            work.Add(facts.Finished.Count == 1 ? $"\"{named}\" işini bitirdin" : $"{facts.Finished.Count} iş bitirdin ({named}{(facts.Finished.Count > 3 ? "…" : "")})");
        }

        if (facts.FocusMinutes >= 10)
        {
            work.Add($"{TurkishGrammar.Duration(facts.FocusMinutes)} odaklandın");
        }

        if (work.Count > 0)
        {
            sentences.Add(TurkishGrammar.Capitalize(TurkishGrammar.Join(work)) + ".");
        }

        var life = new List<string>();
        if (facts.Habits.Count > 0)
        {
            life.Add($"{TurkishGrammar.Join([.. facts.Habits.Select(habit => habit.ToLower(Turkish))])} alışkanlığını yaptın");
        }

        if (facts.Spent > 0)
        {
            life.Add($"{ExpenseBook.Money(facts.Spent)} harcadın{(facts.TopCategory is { Length: > 0 } category ? $", en çok {category.ToLower(Turkish)}" : "")}");
        }

        if (life.Count > 0)
        {
            sentences.Add(TurkishGrammar.Capitalize(TurkishGrammar.Join(life)) + ".");
        }

        if (facts.Journal.Count > 0)
        {
            sentences.Add($"Günlüğüne şunu yazmışsın: {Short(facts.Journal[0])}.");
        }
        else if (facts.Notes.Count > 0)
        {
            sentences.Add(facts.Notes.Count == 1 ? $"Bir not almışsın: {Short(facts.Notes[0])}." : $"{facts.Notes.Count} not almışsın; ilki: {Short(facts.Notes[0])}.");
        }

        if (facts.Chats.Count > 0 && sentences.Count < 5)
        {
            sentences.Add(facts.Chats.Count == 1
                ? $"Benimle \"{Short(facts.Chats[0])}\" üzerine konuştun."
                : $"Benimle {facts.Chats.Count} kez konuştun; biri \"{Short(facts.Chats[0])}\" üzerineydi.");
        }

        if (sentences.Count == 0)
        {
            sentences.Add($"{label} için az kayıt var.");
        }
        else if (!sentences[0].StartsWith(label, StringComparison.Ordinal))
        {
            sentences.Insert(0, $"{label}:");
        }

        return string.Join(' ', sentences).Replace(": En", ": en", StringComparison.Ordinal);
    }

    /// <summary>Gece yarısını geçen saatler 01:30 diye okunur.</summary>
    private static string Clock(TimeSpan time) =>
        TimeSpan.FromMinutes((((time.TotalMinutes % 1440) + 1440) % 1440)).ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    private static string Short(string text)
    {
        var plain = text.Trim().TrimEnd('.');
        return plain.Length > 90 ? plain[..90].TrimEnd() + "…" : plain;
    }

    [GeneratedRegex(@"^(?<n>\d{1,3}) gün önce$")]
    private static partial Regex DaysAgo();

    [GeneratedRegex(@"^(?:(?<gecen>geçen) (?:(?<hafta>hafta) )?|(?<hafta>geçen haftanın) )?(?<w>pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar)$")]
    private static partial Regex WeekdayWords();

    [GeneratedRegex(@"^(?<d>\d{1,2}) (?<m>ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık)$")]
    private static partial Regex DayMonth();
}
