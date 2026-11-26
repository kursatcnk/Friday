using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Sabah karşılaması: kısa, önceliklendirilmiş.</summary>
public sealed partial class AssistantTools
{
    /// <summary>Hava, takvim ve kutlamalardan sonra en fazla bu kadar cümle okunur; gerisi "bugün neler var" ile sorulur.</summary>
    private const int MorningItems = 4;

    /// <summary>
    /// Günün ilk oturuşunda söylenen karşılama. Hava, takvimin ilk etkinliği ve kutlamalar hep söylenir; geri kalanlar
    /// (geciken işler, ödemeler, yaklaşan tarihler, bütçe, alışkanlıklar, odak hedefi, kelimeler, okuma listesi) önem
    /// sırasına göre dizilir ve yalnızca ilk dördü okunur. Modelsiz kurulur; sabah GPU'yu uyandırmaz.
    /// </summary>
    public async Task<string> MorningGreetingAsync(string userName, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var head = new List<string> { string.IsNullOrWhiteSpace(userName) ? "Günaydın!" : $"Günaydın {userName.Trim()}!" };
        try
        {
            head.Add(await WeatherSpeechAsync("", "bugun", rainQuestion: false, cancellationToken).ConfigureAwait(false));
            if (Calendar.IsConfigured && await Calendar.EventsAsync(now, now.Date.AddDays(1), cancellationToken).ConfigureAwait(false) is { Count: > 0 } events)
            {
                var first = events[0];
                head.Add($"Bugün takviminde {events.Count} etkinlik var; ilki {(first.AllDay ? "tüm gün" : first.Start.ToString("HH:mm", CultureInfo.InvariantCulture) + "'" + RhythmText.TimeSuffix(first.Start))} {first.Title}.");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // İnternet yoksa karşılama havasız yapılır.
        }

        // Doğum günü, yıldönümü, resmî gün: günün kendisinde hep söylenir (sorusu varsa o da).
        SpecialDayGreetings(head, now);

        var items = new List<(int Priority, string Text)>();
        void Add(int priority, string text) => items.Add((priority, text));

        var open = Todos?.Open ?? [];
        var late = open.Where(item => item.Due < today).Select(item => item.Text.ToLower(Turkish)).ToList();
        var dueToday = open.Where(item => item.Due == today).Select(item => item.Text.ToLower(Turkish)).ToList();
        if (late.Count > 0)
        {
            Add(92, $"Süresi geçen: {TurkishGrammar.Join(late)}.");
        }

        if (dueToday.Count > 0)
        {
            Add(90, $"Bugün son günü olan: {TurkishGrammar.Join(dueToday)}.");
        }

        if (Expenses is { } expenses)
        {
            if (expenses.DueOn(today) is { Count: > 0 } bills)
            {
                Add(85, $"Bugün ödeme günü: {TurkishGrammar.Join([.. bills.Select(bill => $"{bill.Name.ToLower(Turkish)} {ExpenseBook.Spoken(bill.Amount)}")])}. Ödeyince söyle, harcamalara işlerim.");
            }

            if (expenses.DueOn(today.AddDays(1)) is { Count: > 0 } tomorrowBills)
            {
                Add(50, $"Yarın {TurkishGrammar.Join([.. tomorrowBills.Select(bill => bill.Name.ToLower(Turkish))])} ödenecek.");
            }

            var spent = MonthTotal(expenses, now);
            if (expenses.MonthlyBudget > 0 && spent > expenses.MonthlyBudget * 0.8m)
            {
                Add(60, spent > expenses.MonthlyBudget
                    ? $"Bu ay bütçeyi {ExpenseBook.Spoken(spent - expenses.MonthlyBudget)} aştın."
                    : $"Bu ay bütçende {ExpenseBook.Spoken(expenses.MonthlyBudget - spent)} kaldı.");
            }

            var (from, to, _) = ExpenseBook.Period("gecen_ay", now);
            if (now.Day == 1 && expenses.Summary(from, to) is { Count: > 0 } last)
            {
                Add(55, $"Geçen ay toplam {ExpenseBook.Spoken(last.Total)} harcadın; en çok {last.Categories[0].Category.ToLower(Turkish)}.");
            }
        }

        // Hafızadaki tarihler (kutlananlar hariç): bugün olan önde, uzaklaştıkça önemi azalır.
        foreach (var item in memory.Items)
        {
            if (MemoryDates.NextOccurrence(item.Text, today) is { } date && date.DayNumber - today.DayNumber is var left and <= 3
                && !(left == 0 && SpecialDays.KindOf(item.Text) is SpecialKind.OwnBirthday or SpecialKind.Birthday or SpecialKind.Anniversary))
            {
                var when = left switch { 0 => "Bugün", 1 => "Yarın", _ => $"{left} gün sonra" };
                var fact = item.Text.StartsWith("Kullanıcının ", StringComparison.Ordinal) ? item.Text["Kullanıcının ".Length..] : item.Text;
                Add(left == 0 ? 80 : 70 - (left * 5), $"{when}: {fact.TrimEnd('.')}.");
            }
        }

        if (late.Count == 0 && dueToday.Count == 0)
        {
            var stale = new List<string>();
            StaleTodoMorning(stale, now);
            if (stale.Count > 0)
            {
                Add(45, stale[0]);
            }
            else if (open.Count > 0)
            {
                Add(40, open.Count == 1 ? $"Bekleyen bir işin var: {open[0].Text}." : $"{open.Count} bekleyen işin var; ilki {open[0].Text}.");
            }
        }

        if (HabitBook is { } book && book.Pending(today).Count > 0)
        {
            var streak = book.Habits.Where(habit => !habit.IsWeekly).Select(habit => (habit.Name, Streak: HabitBook.Streak(habit, today)))
                .Where(item => item.Streak >= 3).OrderByDescending(item => item.Streak).FirstOrDefault();
            Add(streak.Name is null ? 30 : 42, streak.Name is null
                ? $"Bugün {book.Pending(today).Count} alışkanlığın var."
                : $"{streak.Name} serin {streak.Streak} gün; bugün de bozma.");
        }

        if (Rhythm is { } rhythm && Settings.FocusGoalMinutes > 0)
        {
            var goalStreak = RhythmGoal.Streak(rhythm.History(30), Settings.FocusGoalMinutes);
            Add(goalStreak > 2 ? 35 : 20, goalStreak > 1
                ? $"{goalStreak} gündür odak hedefini tutturuyorsun; bugünkü hedef {TurkishGrammar.Duration(Settings.FocusGoalMinutes)}."
                : $"Bugünkü odak hedefin {TurkishGrammar.Duration(Settings.FocusGoalMinutes)}.");
        }

        if (Lists?.Items("okuma listesi").Count is > 0 and var reading)
        {
            Add(15, $"Okuma listende {reading} sayfa bekliyor.");
        }

        // Dün yarım kalan iş: son odaklanılan iş dünse ve o günden beri bitirilmediyse güne oradan başlamayı önerir.
        LoadFocus();
        if (_focusTask is { Length: > 0 } lastTask && DateOnly.FromDateTime(_focusAt) == today.AddDays(-1)
            && !(Todos?.CompletedBetween(_focusAt, now) ?? []).Any(item => string.Equals(item.Text, lastTask, StringComparison.OrdinalIgnoreCase)))
        {
            Add(45, $"Dün \"{lastTask.ToLower(Turkish)}\" üzerinde çalışıyordun; istersen bugün oradan devam edelim.");
        }

        var chosen = items.OrderByDescending(item => item.Priority).Take(MorningItems).Select(item => item.Text);
        return string.Join(" ", head.Concat(chosen));
    }
}
