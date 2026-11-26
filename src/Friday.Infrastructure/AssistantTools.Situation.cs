using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Dil modeline her soruda verilen kısa durum satırı.</summary>
public sealed partial class AssistantTools
{
    /// <summary>Görüşmedeki uygulama (FridayAssistant verir); yoksa null.</summary>
    public Func<string?>? MeetingApp { get; set; }

    /// <summary>
    /// "[Durum: saat 15:42 perşembe · 3 açık iş (bugün son gün: raporu bitir) · sıradaki hatırlatma 17:00 doktor · odakta, 16:10'a
    /// kadar]". Model saat için araç çağırmadan, "akşama kadar ne yetiştiririm", "bugün yoğun muyum" gibi soruları gerçek
    /// duruma göre cevaplar. Yalnızca o soruya eklenir, geçmişe yazılmaz.
    /// </summary>
    public string Situation(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var parts = new List<string> { $"saat {now.ToString("HH:mm", CultureInfo.InvariantCulture)} {now.ToString("dddd", Turkish)}" };
        if (Todos?.Open is { Count: > 0 } open)
        {
            var late = open.Where(item => item.Due < today).Select(item => item.Text).Take(2).ToList();
            var due = open.Where(item => item.Due == today).Select(item => item.Text).Take(2).ToList();
            var notes = new List<string>();
            if (late.Count > 0)
            {
                notes.Add($"gecikmiş: {string.Join(", ", late)}");
            }

            if (due.Count > 0)
            {
                notes.Add($"bugün son gün: {string.Join(", ", due)}");
            }

            parts.Add($"{open.Count} açık iş" + (notes.Count > 0 ? $" ({string.Join("; ", notes)})" : ""));
        }

        if (reminders.Upcoming.Where(item => item.Trigger is null && item.Due > now && item.Due - now < TimeSpan.FromHours(24)).OrderBy(item => item.Due).FirstOrDefault() is { } next)
        {
            parts.Add($"sıradaki hatırlatma {next.Due.ToString(next.Due.Date == now.Date ? "HH:mm" : "ddd HH:mm", Turkish)} {(next.Text.Length > 0 ? next.Text : "")}".TrimEnd());
        }

        if (Rhythm?.Snapshot() is { FocusEnd: { } focusEnd } && focusEnd > now)
        {
            parts.Add($"odakta, {focusEnd.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.ToSuffix(focusEnd)} kadar");
        }

        if (MeetingApp?.Invoke() is { } call)
        {
            parts.Add($"{call} görüşmesinde");
        }

        if (HabitBook?.Pending(today) is { Count: > 0 } habits)
        {
            parts.Add($"bugün yapılmamış alışkanlık: {string.Join(", ", habits.Take(3).Select(habit => habit.Name))}");
        }

        return $"\n[Durum: {string.Join(" · ", parts)}]";
    }

    /// <summary>
    /// Soru bir günün programını soruyorsa ("yarın dişçim var mı", "cuma boş muyum") o günün gündemi kayıtlardan
    /// hesaplanıp eklenir (bkz. <see cref="DayAgenda"/>). Model eskiden yarınki dişçi hatırlatmasını görmeyip "takviminde
    /// yok" diyebiliyordu. Takvim bağlıysa önbellekteki etkinlikler de girer; ağa gidilmez.
    /// </summary>
    public string AgendaContext(string question, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (DayAgenda.Range(question, today) is not { } range)
        {
            return "";
        }

        IReadOnlyList<CalendarEvent> events = Calendar.IsConfigured
            ? Calendar.Cached(range.From == today ? now : range.From.ToDateTime(TimeOnly.MinValue), range.To.ToDateTime(TimeOnly.MinValue))
            : [];
        var agenda = DayAgenda.Build(range.From, range.To, now, events, reminders.Upcoming, Todos?.Open ?? [], memory.Items, Expenses?.Bills ?? []);
        var text = agenda.Count == 0 ? "kayıtlı bir şey yok" : DayAgenda.Describe(agenda, range.From, range.To, today, range.Label);
        return $"\n[Gündem ({range.Label.ToLower(Turkish)}; hatırlatmalar, işler, özel günler{(Calendar.IsConfigured ? ", takvim" : "; takvim bağlı değil")}): {text}]";
    }

    /// <summary>
    /// Soru hafızadaki haftalık bir tekrara değiniyorsa ("bugün yüzmeye gidiyor muyum", "pilates hangi gün") ya da
    /// bugün/yarın soruluyorsa, tekrarın bugüne denk gelip gelmediği kodla hesaplanıp eklenir (bkz. <see cref="WeeklyFacts"/>).
    /// </summary>
    public string WeeklyContext(string question, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var weekly = memory.Items.Where(item => MemoryDates.NextOccurrence(item.Text, today) is null && WeeklyFacts.Parse(item.Text) is not null).ToList();
        if (weekly.Count == 0)
        {
            return "";
        }

        IReadOnlyList<MemoryItem> chosen = MemoryRecall.Relevant(weekly, question, new HashSet<MemoryItem>(), 3);
        if (chosen.Count == 0 && TodayWords().IsMatch(question.ToLower(Turkish)))
        {
            chosen = [.. weekly.Take(3)];
        }

        return chosen.Count == 0 ? ""
            : "\n[Haftalık tekrarlar, bugüne göre hesaplandı: " + string.Join(" | ", chosen.Select(item => WeeklyFacts.Note(WeeklyFacts.Parse(item.Text)!, today))) + "]";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(?:bugün|yarın|bu akşam|bu sabah|bu hafta|yarın akşam|programım)")]
    private static partial System.Text.RegularExpressions.Regex TodayWords();
}
