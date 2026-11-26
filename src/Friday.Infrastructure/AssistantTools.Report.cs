using System.Globalization;
using System.Text;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Haftalık rapor dosyası ve son tarih hatırlatmaları.</summary>
public sealed partial class AssistantTools
{
    private DateOnly _deadlineNudged;

    /// <summary>
    /// Son 7 günün raporu Kaydedilenler klasörüne Markdown olarak yazılır: odak ve ekran süresi, alışkanlıklar, harcamalar,
    /// işler ve günlük. Dil modeli kullanılmaz; yalnızca kayıtlardan derlenir.
    /// </summary>
    private QuickReply WeeklyReportFile()
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var from = now.Date.AddDays(-6);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# Haftalık rapor · {from.ToString("d MMMM", Turkish)} – {now.ToString("d MMMM yyyy", Turkish)}");
        text.AppendLine();
        text.AppendLine(WeeklyReview());
        text.AppendLine();

        if (Rhythm is { } coach)
        {
            text.AppendLine("## Odak ve çalışma");
            foreach (var day in coach.History(7))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {day.Date.ToString("dddd d MMMM", Turkish)}: {RhythmText.Compact(day.ActiveMinutes)} bilgisayar başında, {RhythmText.Compact(day.FocusMinutes)} odak, {day.Breaks} mola");
            }

            // Odak hangi işlere gitti (odak başlatılırken söylenen işler).
            var week = coach.History(7);
            var byTask = FocusLog.ByTask(week.SelectMany(day => day.Sessions)).Where(group => group.Task.Length > 0).Take(6).ToList();
            if (byTask.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Odak hangi işlere gitti:");
                foreach (var (task, minutes, count) in byTask)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"- {task}: {RhythmText.Compact(minutes)} ({count} oturum)");
                }
            }

            text.AppendLine();
        }

        if (Activity is { } activity)
        {
            var apps = Enumerable.Range(0, 7).SelectMany(offset => activity.Day(now.AddDays(-offset)))
                .GroupBy(item => item.App).Select(group => (App: group.Key, Seconds: group.Sum(item => item.Seconds)))
                .OrderByDescending(item => item.Seconds).Take(8).ToList();
            if (apps.Count > 0)
            {
                text.AppendLine("## En çok kullanılanlar");
                foreach (var (app, seconds) in apps)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"- {app}: {ScreenTimeLog.Compact(seconds)}");
                }

                text.AppendLine();
            }
        }

        if (HabitBook is { } habits && habits.Habits.Count > 0)
        {
            text.AppendLine("## Alışkanlıklar");
            foreach (var habit in habits.Habits)
            {
                var week = HabitBook.Week(habit, today);
                text.AppendLine(CultureInfo.InvariantCulture, $"- {habit.Name}: {string.Concat(week.Select(done => done ? "●" : "○"))} {week.Count(done => done)}/7, seri {HabitBook.Streak(habit, today)} gün");
            }

            text.AppendLine();
        }

        if (Expenses is { } expenses && expenses.Summary(from, now.Date.AddDays(1)) is { Count: > 0 } spent)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"## Harcamalar · {ExpenseBook.Money(spent.Total)}");
            foreach (var (category, amount) in spent.Categories)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {category}: {ExpenseBook.Money(amount)}");
            }

            text.AppendLine();
        }

        if (Todos is { } todos)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"## İşler · bu hafta {todos.DoneSince(from)} tamamlandı");
            foreach (var item in todos.Open.Take(12))
            {
                var due = TodoDue.Label(item.Due, today);
                text.AppendLine(CultureInfo.InvariantCulture, $"- [ ] {(item.Important ? "★ " : "")}{item.Text}{(due.Length > 0 ? $" ({due})" : "")}");
            }

            text.AppendLine();
        }

        if (Journal is { } journal && journal.Between(from, now) is { Count: > 0 } entries)
        {
            text.AppendLine("## Günlük");
            foreach (var (when, entry) in entries)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {when.ToString("ddd HH:mm", Turkish)} · {entry}");
            }

            text.AppendLine();
        }

        var result = SaveDocument($"Haftalık rapor {now:yyyy-MM-dd}", text.ToString());
        ledger.Record(DataFlow.Local, "Haftalık rapor", "Kaydedilenler klasörüne yazıldı");
        return new QuickReply(result.StartsWith("Kaydedilecek", StringComparison.Ordinal) ? result : $"Haftalık raporunu hazırladım. {result}");
    }

    /// <summary>Akşam bir kez: son günü yarın olan işler ("Yarın son günü olan: raporu bitir."). Yoksa null.</summary>
    public string? DeadlineNudge(DateTime now)
    {
        var tomorrow = DateOnly.FromDateTime(now).AddDays(1);
        if (Todos is not { } todos || _deadlineNudged == tomorrow)
        {
            return null;
        }

        var due = todos.Open.Where(item => item.Due == tomorrow).Select(item => item.Text.ToLower(Turkish)).ToList();
        if (due.Count == 0)
        {
            return null;
        }

        _deadlineNudged = tomorrow;
        return $"Yarın son günü olan {(due.Count == 1 ? "iş" : "işler")}: {TurkishGrammar.Join(due)}. Bu akşam biraz ilerlemek ister misin?";
    }
}
