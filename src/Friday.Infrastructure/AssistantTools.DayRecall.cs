using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>"Geçen salı ne yaptım?" (bkz. <see cref="DayStory"/>): geçmiş bir gün, Friday'in kendi kayıtlarından.</summary>
public sealed partial class AssistantTools
{
    /// <summary>Gün anlaşılmazsa null.</summary>
    private QuickReply? DayRecall(string when)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        if (DayStory.Parse(when, today) is not { } day)
        {
            return null;
        }

        var facts = Facts(day, now);
        var spoken = DayStory.Tell(facts, today);
        var rows = new List<CardRow>();
        if (facts.Span is { } span)
        {
            rows.Add(new CardRow("Bilgisayar başında", $"{Clock(span.Start)}–{Clock(span.End)}"));
        }

        rows.AddRange(facts.Apps.Take(4).Select(app => new CardRow(app.App, TurkishGrammar.Duration(app.Seconds / 60.0))));
        rows.AddRange(facts.Finished.Take(4).Select(task => new CardRow("Biten iş", task)));
        if (facts.FocusMinutes >= 10)
        {
            rows.Add(new CardRow("Odak", TurkishGrammar.Duration(facts.FocusMinutes)));
        }

        if (facts.Spent > 0)
        {
            rows.Add(new CardRow("Harcama", ExpenseBook.Money(facts.Spent)));
        }

        rows.AddRange(facts.Journal.Take(2).Select(line => new CardRow("Günlük", Short(line, 60))));
        if (rows.Count > 0)
        {
            ShowListCard(DayStory.Label(day, today), "", rows.Take(MaxCardRows + 2));
        }

        ledger.Record(DataFlow.Local, "Geçmiş", $"{day:yyyy-MM-dd} günü kayıtlardan anlatıldı");
        return new QuickReply(spoken);
    }

    private DayFacts Facts(DateOnly day, DateTime now)
    {
        var start = day.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var rhythm = Rhythm is { } coach ? coach.History(Math.Max(1, now.Date.Subtract(start).Days + 1)).FirstOrDefault(item => item.Date == day) : null;
        var expenses = Expenses?.Between(start, end) ?? [];
        return new DayFacts(day)
        {
            // Ritim kaydı günün tamamını tutar; yoksa yarım saatlik etkinlik dilimleri.
            Span = rhythm is { Segments.Count: > 0 } segments
                ? (segments.Segments.Min(segment => segment.Start) - start, segments.Segments.Max(segment => segment.End) - start)
                : Tracker?.DaySpan(day),
            ActiveMinutes = rhythm?.ActiveMinutes ?? 0,
            FocusMinutes = rhythm?.FocusMinutes ?? 0,
            Apps = Tracker?.Day(start) ?? [],
            Finished = [.. (Todos?.CompletedBetween(start, end) ?? []).Select(item => item.Text)],
            Habits = [.. (HabitBook?.Habits ?? []).Where(habit => habit.IsDoneOn(day)).Select(habit => habit.Name)],
            Spent = expenses.Sum(item => item.Amount),
            TopCategory = expenses.GroupBy(item => item.Category).OrderByDescending(group => group.Sum(item => item.Amount)).Select(group => group.Key).FirstOrDefault(),
            Journal = [.. (Journal?.Between(start, end.AddTicks(-1)) ?? []).Select(entry => entry.Text)],
            // Not defteri ilk açılınca Friday'in yazdığı açıklama satırı kullanıcının notu değildir.
            Notes = [.. notes.All.Where(note => note.Time >= start && note.Time < end && !note.Text.StartsWith("Notlarım burada", StringComparison.Ordinal)).Select(note => note.Text)],
            Chats = [.. history.List().Where(chat => chat.Created.LocalDateTime >= start && chat.Created.LocalDateTime < end && chat.TurnCount > 0)
                .OrderByDescending(chat => chat.TurnCount).Select(chat => chat.Title)]
        };
    }
}
