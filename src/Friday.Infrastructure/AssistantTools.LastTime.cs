using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>"En son ne zaman koşuya gittim?" (bkz. <see cref="LastTime"/>).</summary>
public sealed partial class AssistantTools
{
    /// <summary>Kayıtlarda iz yoksa null: "Galatasaray en son ne zaman şampiyon oldu" gibi genel sorular dil modeline gider.</summary>
    private QuickReply? LastTimeCommand(string subject)
    {
        var now = DateTime.Now;
        var traces = new List<LastTime.Trace>();
        foreach (var habit in HabitBook?.Habits ?? [])
        {
            traces.AddRange(habit.Done.Select(day => new LastTime.Trace("Alışkanlık", habit.Name, day.ToDateTime(new TimeOnly(12, 0)))));
        }

        traces.AddRange((Expenses?.Items ?? []).Select(item =>
            new LastTime.Trace("Harcama", $"{(item.Note.Length > 0 ? item.Note : item.Category)} ({item.Category}) · {ExpenseBook.Money(item.Amount)}", item.When)));
        traces.AddRange((Todos?.CompletedBetween(DateTime.MinValue, now.AddMinutes(1)) ?? []).Select(item => new LastTime.Trace("İş", item.Text, item.Done ?? item.Created)));
        if (Journal is { } journal)
        {
            traces.AddRange(journal.Between(now.AddYears(-2), now).Select(entry => new LastTime.Trace("Günlük", entry.Text, entry.When)));
        }

        traces.AddRange(notes.All.Select(note => new LastTime.Trace("Not", note.Text, note.Time)));
        if (LastTime.Find(subject, traces) is not { } trace)
        {
            return null;
        }

        ledger.Record(DataFlow.Local, "Geçmiş", "Kayıtlarda en son ne zaman olduğu arandı");
        return new QuickReply(LastTime.Tell(subject, trace, now));
    }
}
