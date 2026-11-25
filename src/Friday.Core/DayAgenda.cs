using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Gündemde bir satır: hangi gün, varsa saati, nereden geldiği ve metni.</summary>
/// <param name="Id">Hatırlatmanın ya da işin kimliği (silmek, tamamlamak için); diğerlerinde null.</param>
public sealed record AgendaItem(DateOnly Day, TimeOnly? Time, AgendaKind Kind, string Text, string? Id = null);

public enum AgendaKind
{
    Calendar,
    Reminder,
    Overdue,
    Deadline,
    SpecialDay,
    Bill,
    Weekly,
}

/// <summary>Hafızadaki haftalık tekrar eden bir alışkanlık: "Pazartesi ve çarşamba akşamları yüzmeye gidiyor".</summary>
public sealed record WeeklyFact(string Text, IReadOnlySet<DayOfWeek> Days)
{
    public bool On(DateOnly day) => Days.Contains(day.DayOfWeek);

    /// <summary>Bugünden sonraki (bugün dahil değil) ilk gün.</summary>
    public DateOnly NextAfter(DateOnly day)
    {
        var next = day.AddDays(1);
        while (!On(next))
        {
            next = next.AddDays(1);
        }

        return next;
    }
}

/// <summary>
/// Haftalık tekrar eden bilgiler: hafızada "pazartesi ve çarşamba akşamları yüzmeye gidiyor", "her cuma annemi ararım",
/// "salıları pilates var", "hafta sonları kayak" gibi. Model "bugün yüzmeye gidiyor muyum" sorusunda bugünün salı
/// olduğunu bilse de çoğu kez önce "Evet" deyip sonra kendisiyle çelişiyordu; bu bilgilerin bugüne ve yarına denk gelip
/// gelmediği kodla hesaplanıp soruya eklenir. Tek seferlik bir gün ("pazartesi toplantı var") tekrar sayılmaz: gün adı
/// çoğul ("salıları"), "her" ile ("her cuma") ya da "günleri/akşamları" gibi bir sözcükle birlikte geçmelidir.
/// </summary>
public static partial class WeeklyFacts
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Dictionary<string, DayOfWeek> Names = new(StringComparer.Ordinal)
    {
        ["pazartesi"] = DayOfWeek.Monday, ["salı"] = DayOfWeek.Tuesday, ["çarşamba"] = DayOfWeek.Wednesday,
        ["perşembe"] = DayOfWeek.Thursday, ["cuma"] = DayOfWeek.Friday, ["cumartesi"] = DayOfWeek.Saturday, ["pazar"] = DayOfWeek.Sunday,
    };

    /// <summary>Metin haftalık bir tekrar söylüyorsa günleriyle birlikte; yoksa null.</summary>
    public static WeeklyFact? Parse(string text)
    {
        var lower = text.ToLower(Turkish);
        var days = new HashSet<DayOfWeek>();
        var recurring = false;
        foreach (Match match in Day().Matches(lower))
        {
            days.Add(Names[match.Groups["day"].Value]);
            recurring |= match.Groups["plural"].Success || match.Groups["every"].Success || match.Groups["part"].Success;
        }

        if (WeekPart().Match(lower) is { Success: true } part)
        {
            recurring = true;
            if (part.Value.Contains("son", StringComparison.Ordinal))
            {
                days.UnionWith([DayOfWeek.Saturday, DayOfWeek.Sunday]);
            }
            else
            {
                days.UnionWith([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]);
            }
        }

        return recurring && days.Count is > 0 and < 7 ? new WeeklyFact(text.Trim().TrimEnd('.'), days) : null;
    }

    /// <summary>Hafızadaki bütün haftalık tekrarlar (tarih içeren tek seferlik bilgiler hariç).</summary>
    public static IReadOnlyList<WeeklyFact> From(IEnumerable<MemoryItem> memories, DateOnly today) =>
        [.. memories.Where(item => MemoryDates.NextOccurrence(item.Text, today) is null).Select(item => Parse(item.Text)).OfType<WeeklyFact>()];

    /// <summary>
    /// Soruya eklenecek not: bu tekrar bugüne ve yarına denk geliyor mu, ikisine de gelmiyorsa sıradaki gün.
    /// "\"Pazartesi ve çarşamba akşamları yüzmeye gidiyor\": bugün (salı) yok, yarın (çarşamba) var."
    /// </summary>
    public static string Note(WeeklyFact fact, DateOnly today)
    {
        var tomorrow = today.AddDays(1);
        var note = $"\"{fact.Text}\": bugün ({DayName(today)}) {(fact.On(today) ? "var" : "yok")}, yarın ({DayName(tomorrow)}) {(fact.On(tomorrow) ? "var" : "yok")}";
        if (!fact.On(today) && !fact.On(tomorrow))
        {
            var next = fact.NextAfter(today);
            note += $"; sıradaki {DayName(next)} ({next.DayNumber - today.DayNumber} gün sonra)";
        }

        return note;
    }

    public static string DayName(DateOnly day) => day.ToDateTime(TimeOnly.MinValue).ToString("dddd", Turkish).ToLower(Turkish);

    [GeneratedRegex(@"(?<every>\bher\s+)?\b(?<day>pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar)(?<plural>ları|leri)?(?![a-zçğıöşü])(?:\s+(?:ve|ile|,)?\s*)?(?<part>\s*(?:günleri|akşamları|sabahları|öğlenleri|geceleri|öğleden sonraları))?")]
    private static partial Regex Day();

    [GeneratedRegex(@"\bhafta\s?(?:sonları|içleri|içi her gün|sonu her)")]
    private static partial Regex WeekPart();
}

/// <summary>
/// Bir günün (ya da birkaç günün) gündemi: takvim etkinlikleri, hatırlatmalar (tekrarlananlar açılarak), son günü gelen
/// ve gecikmiş işler, doğum günleri, ödeme günü gelen sabit giderler ve hafızadaki haftalık tekrarlar. Eskiden "yarın
/// neler var" takvim bağlı değilse "takvimini bağlamadık" diye bitiyordu; yarın 9'daki dişçi hatırlatması ve son günü
/// gelen iş hiç söylenmiyordu.
/// </summary>
public static partial class DayAgenda
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>[from, to) günlerinin gündemi, gün ve saate göre sıralı (saatsizler günün başında).</summary>
    public static IReadOnlyList<AgendaItem> Build(
        DateOnly from,
        DateOnly to,
        DateTime now,
        IEnumerable<CalendarEvent> events,
        IEnumerable<Reminder> reminders,
        IEnumerable<TodoItem> open,
        IEnumerable<MemoryItem> memories,
        IEnumerable<RecurringBill> bills)
    {
        var today = DateOnly.FromDateTime(now);
        var items = new List<AgendaItem>();
        foreach (var item in events)
        {
            var day = DateOnly.FromDateTime(item.Start);
            if (day >= from && day < to)
            {
                items.Add(new AgendaItem(day, item.AllDay ? null : TimeOnly.FromDateTime(item.Start), AgendaKind.Calendar, item.Title));
            }
        }

        var end = to.ToDateTime(TimeOnly.MinValue);
        foreach (var reminder in reminders.Where(item => item.Trigger is null))
        {
            var due = reminder.Due;
            for (var guard = 0; guard < 62 && due < end; guard++)
            {
                if (DateOnly.FromDateTime(due) >= from && due >= now.AddMinutes(-1))
                {
                    items.Add(new AgendaItem(DateOnly.FromDateTime(due), TimeOnly.FromDateTime(due), AgendaKind.Reminder,
                        reminder.Text.Length > 0 ? reminder.Text : "zamanlayıcı", reminder.Id));
                }

                if (reminder.Repeat is not { } rule)
                {
                    break;
                }

                due = RepeatRule.Next(due, rule, due);
            }
        }

        foreach (var todo in open.Where(item => !item.IsDone && item.Due is not null))
        {
            var due = todo.Due!.Value;
            if (due >= from && due < to)
            {
                items.Add(new AgendaItem(due, null, AgendaKind.Deadline, todo.Text, todo.Id));
            }
            else if (due < today && from <= today && today < to)
            {
                items.Add(new AgendaItem(today, null, AgendaKind.Overdue, todo.Text, todo.Id));
            }
        }

        var memoryList = memories.ToList();
        foreach (var special in SpecialDays.Upcoming(memoryList, from, to.DayNumber - from.DayNumber - 1))
        {
            if (special.Date >= from && special.Date < to)
            {
                items.Add(new AgendaItem(special.Date, null, AgendaKind.SpecialDay, special.Title));
            }
        }

        for (var day = from; day < to; day = day.AddDays(1))
        {
            foreach (var bill in bills.Where(bill => bill.DueIn(day.Year, day.Month) == day && !bill.IsPaid(day)))
            {
                items.Add(new AgendaItem(day, null, AgendaKind.Bill, $"{bill.Name} ödemesi"));
            }
        }

        var weekly = WeeklyFacts.From(memoryList, today);
        for (var day = from; day < to && to.DayNumber - from.DayNumber <= 7; day = day.AddDays(1))
        {
            items.AddRange(weekly.Where(fact => fact.On(day)).Select(fact => new AgendaItem(day, null, AgendaKind.Weekly, fact.Text)));
        }

        return [.. items.OrderBy(item => item.Day).ThenBy(item => item.Kind is AgendaKind.Overdue ? 0 : 1)
            .ThenBy(item => item.Time ?? TimeOnly.MinValue).ThenBy(item => item.Kind)];
    }

    /// <summary>
    /// Sesli özet: "Yarın 09:00'da dişçi randevusu var. Son günü gelen iş: Ela'nın okul formunu imzala. Ayrıca her
    /// hafta: pazartesi ve çarşamba akşamları yüzmeye gidiyor." Birden çok gün varsa gün gün.
    /// </summary>
    public static string Describe(IReadOnlyList<AgendaItem> items, DateOnly from, DateOnly to, DateOnly today, string label)
    {
        if (items.Count == 0)
        {
            return "";
        }

        var days = items.GroupBy(item => item.Day).ToList();
        if (days.Count == 1 && to.DayNumber - from.DayNumber == 1)
        {
            return label + " " + DescribeDay(days[0].ToList(), first: true);
        }

        return string.Join(" ", days.Take(5).Select(group => TurkishGrammar.Capitalize(DayLabel(group.Key, today)) + ": " + DescribeDay(group.ToList(), first: false)))
               + (days.Count > 5 ? $" Sonraki {days.Count - 5} günde de kayıt var." : "");
    }

    /// <summary>
    /// Soru hangi günün programını soruyor: "yarın dişçim var mı" → yarın, "cuma boş muyum" → bu cuma, "bu hafta neler
    /// var" → 7 gün. Programla ilgili bir söz yoksa ("bugün hava nasıl") ya da geçmiş soruluyorsa ("dün") null.
    /// </summary>
    public static (DateOnly From, DateOnly To, string Label)? Range(string question, DateOnly today)
    {
        var text = question.ToLower(Turkish);
        if (!Schedule().IsMatch(text) || Past().IsMatch(text))
        {
            return null;
        }

        if (Week().IsMatch(text))
        {
            return (today, today.AddDays(7), "Önümüzdeki 7 günde");
        }

        if (AfterTomorrow().IsMatch(text))
        {
            return (today.AddDays(2), today.AddDays(3), TurkishGrammar.Capitalize(WeeklyFacts.DayName(today.AddDays(2))));
        }

        if (Tomorrow().IsMatch(text))
        {
            return (today.AddDays(1), today.AddDays(2), "Yarın");
        }

        if (Today().IsMatch(text))
        {
            return (today, today.AddDays(1), "Bugün");
        }

        if (Weekday().Match(text) is { Success: true } named)
        {
            var target = WeeklyFacts.Parse("her " + named.Groups["d"].Value)!.Days.First();
            var day = today;
            while (day.DayOfWeek != target)
            {
                day = day.AddDays(1);
            }

            return (day, day.AddDays(1), day == today ? "Bugün" : TurkishGrammar.Capitalize(DayLabel(day, today)));
        }

        return null;
    }

    [GeneratedRegex(@"ne var|neler var|planım|programım|program|işim var|işlerim|randevu|toplantı|boş muyum|müsait|meşgul|yapacağım|yapmam|gidiyor muyum|gideceğim|var mı|hatırlatma|son gün|ne yapıyorum|ajandam|takvimim")]
    private static partial Regex Schedule();

    [GeneratedRegex(@"\b(?:dün|geçen|evvelsi gün)")]
    private static partial Regex Past();

    [GeneratedRegex(@"\b(?:bu hafta|hafta sonu|önümüzdeki günler|haftaya kadar)")]
    private static partial Regex Week();

    [GeneratedRegex(@"\b(?:öbür gün|ertesi gün|yarından sonra)")]
    private static partial Regex AfterTomorrow();

    [GeneratedRegex(@"\byarın")]
    private static partial Regex Tomorrow();

    [GeneratedRegex(@"\b(?:bugün|bu akşam|bu sabah|bu öğle|bu gece)")]
    private static partial Regex Today();

    [GeneratedRegex(@"\b(?<d>pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar)(?:'?[yn]?[ıiuüae])?(?: günü)?(?![a-zçğıöşü])")]
    private static partial Regex Weekday();

    /// <summary>"Bugün", "yarın", "perşembe", "3 Aralık cumartesi".</summary>
    public static string DayLabel(DateOnly day, DateOnly today) => (day.DayNumber - today.DayNumber) switch
    {
        0 => "bugün",
        1 => "yarın",
        > 1 and < 7 => WeeklyFacts.DayName(day),
        _ => day.ToDateTime(TimeOnly.MinValue).ToString("d MMMM dddd", Turkish)
    };

    private static string DescribeDay(List<AgendaItem> items, bool first)
    {
        var parts = new List<string>();
        var timed = items.Where(item => item.Kind is AgendaKind.Calendar or AgendaKind.Reminder).Select(Timed).ToList();
        if (timed.Count > 0)
        {
            parts.Add(TurkishGrammar.Join(timed) + (first ? " var" : ""));
        }

        void Add(AgendaKind kind, string single, string many)
        {
            var texts = items.Where(item => item.Kind == kind).Select(item => SentenceCase(item.Text.TrimEnd('.'))).ToList();
            if (texts.Count > 0)
            {
                parts.Add((texts.Count == 1 ? single : many) + ": " + TurkishGrammar.Join(texts));
            }
        }

        Add(AgendaKind.Overdue, "gecikmiş iş", "gecikmiş işler");
        Add(AgendaKind.Deadline, "son günü gelen iş", "son günü gelen işler");
        Add(AgendaKind.SpecialDay, "özel gün", "özel günler");
        Add(AgendaKind.Bill, "ödeme", "ödemeler");
        Add(AgendaKind.Weekly, "haftalık düzenin", "haftalık düzenin");
        return string.Join("; ", parts) + ".";
    }

    /// <summary>Cümle içinde gün adıyla başlayan bir bilgi küçük harfle sürer: "…; haftalık düzenin: pazartesi ve çarşamba …".</summary>
    private static string SentenceCase(string text) =>
        text.Length > 1 && WeeklyFacts.Parse(text) is not null && text.Split(' ')[0].ToLower(Turkish) is "pazartesi" or "salı" or "çarşamba" or "perşembe" or "cuma" or "cumartesi" or "pazar" or "her" or "hafta"
            ? char.ToLower(text[0], Turkish) + text[1..]
            : text;

    private static string Timed(AgendaItem item) =>
        item.Time is { } time
            ? $"{time.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.TimeSuffix(DateTime.Today.Add(time.ToTimeSpan()))} {GrammarText(item.Text)}"
            : $"tüm gün {GrammarText(item.Text)}";

    private static string GrammarText(string text) => TurkishGrammar.ToYou(text.TrimEnd('.'));
}
