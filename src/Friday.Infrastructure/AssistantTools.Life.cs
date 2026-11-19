using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Alışkanlık takipçisi ve harcama kaydı.</summary>
public sealed partial class AssistantTools
{
    /// <summary>Seri kutlanan gün sayıları.</summary>
    private static readonly int[] Milestones = [3, 7, 14, 21, 30, 50, 66, 100, 200, 365];

    /// <summary>Kullanıcının takip ettiği günlük alışkanlıklar (App verir).</summary>
    public HabitBook? HabitBook { get; set; }

    /// <summary>Harcama kaydı (App verir).</summary>
    public ExpenseBook? Expenses { get; set; }

    // ---------- Alışkanlıklar ----------

    /// <returns>Cevap; "spor yaptım" için öyle bir alışkanlık yoksa null (soru dil modeline gider).</returns>
    private QuickReply? HabitCommand(string action, string name)
    {
        if (HabitBook is not { } book)
        {
            return new QuickReply("Alışkanlık takipçisi hazır değil.");
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        switch (Simplify(action))
        {
            case "ekle":
                var clean = name.Trim();
                if (clean.Length < 2)
                {
                    return new QuickReply("Hangi alışkanlığı takip edeyim? Ör. \"alışkanlık ekle: kitap okumak\".");
                }

                var before = book.Habits.Count;
                var habit = book.Add(clean, today);
                ledger.Record(DataFlow.Local, "Alışkanlık", $"{habit.Name} takibe alındı");
                return new QuickReply(book.Habits.Count == before
                    ? $"{habit.Name} zaten takip ettiğin alışkanlıklar arasında."
                    : $"Tamam, \"{habit.Name}\" alışkanlığını takip ediyorum. Yaptığın gün söylemen yeter, ör. \"{DoneExample(habit.Name)}\".");
            case "yaptim":
                return MarkHabit(book, name, today);
            case "gerial":
                if (book.Find(name) is not { } undone)
                {
                    return null;
                }

                book.Mark(undone.Name, today, done: false);
                return new QuickReply($"{undone.Name} için bugünkü işareti kaldırdım.");
            case "sil":
                if (book.Find(name) is not { } removed)
                {
                    return new QuickReply($"\"{name}\" diye bir alışkanlık bulamadım.");
                }

                book.Remove(removed.Name);
                ledger.Record(DataFlow.Local, "Alışkanlık", $"{removed.Name} takipten çıkarıldı");
                return new QuickReply($"{removed.Name} alışkanlığını takipten çıkardım.");
            default:
                return HabitReport(book, today);
        }
    }

    private QuickReply? MarkHabit(HabitBook book, string name, DateOnly today)
    {
        if (book.Find(name) is not { } habit)
        {
            return null;
        }

        var changed = book.Mark(habit.Name, today);
        habit = book.Find(habit.Name)!;
        var streak = HabitBook.Streak(habit, today);
        ledger.Record(DataFlow.Local, "Alışkanlık", $"{habit.Name} bugün yapıldı");
        if (!changed)
        {
            return new QuickReply($"{habit.Name} bugün zaten işaretliydi; serin {streak} gün.");
        }

        var praise = Milestones.Contains(streak)
            ? $"Tebrikler, {habit.Name} {streak} gündür üst üste! Bu artık gerçek bir alışkanlık olmaya başladı."
            : streak > 1
                ? $"Harika, {habit.Name} işaretlendi; {streak} gündür üst üste."
                : HabitBook.Best(habit) > 2
                    ? $"İşaretledim. Yeni bir seri başladı; en iyi serin {HabitBook.Best(habit)} gün."
                    : $"İşaretledim: {habit.Name}.";
        var pending = book.Pending(today);
        if (book.Habits.Count > 1)
        {
            praise += pending.Count == 0
                ? " Bugünkü bütün alışkanlıklarını tamamladın!"
                : pending.Count == 1 ? $" Bugün bir tek {pending[0].Name.ToLower(Turkish)} kaldı." : "";
        }

        ShowHabitCard(book, today);
        return new QuickReply(praise);
    }

    private QuickReply HabitReport(HabitBook book, DateOnly today)
    {
        var habits = book.Habits;
        if (habits.Count == 0)
        {
            return new QuickReply("Henüz takip ettiğin bir alışkanlık yok. \"Alışkanlık ekle: kitap okumak\" diyebilirsin.");
        }

        ShowHabitCard(book, today);
        var done = habits.Count(habit => habit.IsDoneOn(today));
        var best = habits.MaxBy(habit => HabitBook.Streak(habit, today))!;
        var pending = book.Pending(today);
        var parts = new List<string>
        {
            habits.Count == 1 ? HabitBook.Describe(habits[0], today) + "." : $"{habits.Count} alışkanlığından bugün {done} tanesi yapıldı."
        };
        if (habits.Count > 1 && HabitBook.Streak(best, today) > 1)
        {
            parts.Add($"En uzun serin {best.Name.ToLower(Turkish)}: {HabitBook.Streak(best, today)} gün.");
        }

        if (habits.Count > 1 && pending.Count > 0)
        {
            parts.Add($"Kalanlar: {TurkishGrammar.Join([.. pending.Select(habit => habit.Name.ToLower(Turkish))])}.");
        }

        return new QuickReply(string.Join(" ", parts));
    }

    private void ShowHabitCard(HabitBook book, DateOnly today) =>
        ShowCard(new AnswerCard("Alışkanlıklar", "")
        {
            Rows = [.. book.Habits.Take(MaxCardRows).Select(habit => new CardRow(habit.Name,
                $"{string.Concat(HabitBook.Week(habit, today).Select(done => done ? "●" : "○"))}  {HabitBook.Streak(habit, today)} gün"))]
        });

    /// <summary>"Kitap okumak" → "kitap okudum"; bilinmeyen biçimde "X yaptım".</summary>
    private static string DoneExample(string habit)
    {
        var lower = habit.ToLower(Turkish);
        foreach (var (infinitive, past) in new[] { ("okumak", "okudum"), ("okuma", "okudum"), ("yürümek", "yürüdüm"), ("koşmak", "koştum"), ("yazmak", "yazdım"), ("içmek", "içtim"), ("almak", "aldım"), ("çalışmak", "çalıştım"), ("yapmak", "yaptım"), ("gitmek", "gittim") })
        {
            if (lower.EndsWith(infinitive, StringComparison.Ordinal))
            {
                return lower[..^infinitive.Length] + past;
            }
        }

        return $"{lower} yaptım";
    }

    // ---------- Harcamalar ----------

    private QuickReply ExpenseCommand(string action, string amountText, string note, string period, string category)
    {
        if (Expenses is not { } book)
        {
            return new QuickReply("Harcama kaydı hazır değil.");
        }

        var now = DateTime.Now;
        var amount = decimal.TryParse(amountText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        switch (Simplify(action))
        {
            case "ekle":
                if (amount <= 0 || amount > 10_000_000)
                {
                    return new QuickReply("Tutarı anlayamadım; ör. \"kahveye 80 lira harcadım\" de.");
                }

                var monthBefore = MonthTotal(book, now);
                var expense = book.Add(amount, note, now);
                ledger.Record(DataFlow.Local, "Harcama", $"{ExpenseBook.Money(expense.Amount)} · {expense.Category}");
                var what = expense.Note.Length > 0 ? expense.Note.ToLower(Turkish) : expense.Category.ToLower(Turkish);
                var monthTotal = monthBefore + expense.Amount;
                ShowExpenseCard(book, "ay", null, now);
                return new QuickReply($"Kaydettim: {what}, {ExpenseBook.Spoken(expense.Amount)}. Bu ay toplam {ExpenseBook.Spoken(monthTotal)}." + BudgetNote(book.MonthlyBudget, monthBefore, monthTotal));
            case "silson":
                if (book.RemoveLast() is not { } last)
                {
                    return new QuickReply("Silinecek bir harcama yok.");
                }

                return new QuickReply($"Son harcamayı sildim: {(last.Note.Length > 0 ? last.Note.ToLower(Turkish) + ", " : "")}{ExpenseBook.Spoken(last.Amount)}.");
            case "butce":
                book.MonthlyBudget = amount;
                if (amount <= 0)
                {
                    return new QuickReply("Aylık bütçeyi kaldırdım.");
                }

                var spent = MonthTotal(book, now);
                return new QuickReply($"Aylık bütçen {ExpenseBook.Spoken(amount)}. Bu ay şimdiye kadar {ExpenseBook.Spoken(spent)} harcadın; {(spent <= amount ? $"{ExpenseBook.Spoken(amount - spent)} kaldı." : $"bütçeyi {ExpenseBook.Spoken(spent - amount)} aştın.")}");
            case "disaaktar":
                var folder = SaveFolder ?? Path.Combine(AppContext.BaseDirectory, "Kaydedilenler");
                var file = Path.Combine(folder, $"harcamalar-{now:yyyy-MM-dd}.csv");
                var count = book.ExportCsv(file);
                ledger.Record(DataFlow.Local, "Harcama", $"{count} kayıt {Path.GetFileName(file)} dosyasına aktarıldı");
                return new QuickReply(count == 0 ? "Aktarılacak harcama yok." : $"{count} harcamayı Kaydedilenler klasörüne {Path.GetFileName(file)} olarak aktardım; Excel'de açılabilir.");
            default:
                return ExpenseReport(book, period, category, now);
        }
    }

    private QuickReply ExpenseReport(ExpenseBook book, string period, string category, DateTime now)
    {
        var (from, to, label) = ExpenseBook.Period(period.Length == 0 ? "ay" : period, now);
        var known = category.Length > 0 ? ExpenseBook.KnownCategory(category) : null;
        // "Kahveye ne kadar": önce açıklamasında kahve geçenler; hiç yoksa kategorinin tamamı (Yeme-içme).
        var all = book.Between(from, to);
        var items = category.Length == 0 ? [.. all] : all.Where(item => AssistantToolsText.Contains(item.Note, ExpenseBook.CleanNote(category))).ToList();
        if (items.Count == 0 && known is not null)
        {
            items = [.. all.Where(item => item.Category == known)];
        }
        else if (category.Length > 0)
        {
            known = null;
        }

        ShowExpenseCard(book, period, category.Length > 0 ? items : null, now);
        if (items.Count == 0)
        {
            return new QuickReply(category.Length > 0
                ? $"{label} {category.ToLower(Turkish)} için kayıtlı harcama yok."
                : $"{label} kayıtlı harcama yok. \"Kahveye 80 lira harcadım\" dersen kaydederim.");
        }

        var total = items.Sum(item => item.Amount);
        if (category.Length > 0)
        {
            return new QuickReply($"{label} {(known ?? ExpenseBook.CleanNote(category)).ToLower(Turkish)} için {items.Count} kez, toplam {ExpenseBook.Spoken(total)} harcadın.");
        }

        var groups = items.GroupBy(item => item.Category).Select(group => (group.Key, Sum: group.Sum(item => item.Amount))).OrderByDescending(group => group.Sum).ToList();
        var top = TurkishGrammar.Join([.. groups.Take(3).Select(group => $"{group.Key.ToLower(Turkish)} {ExpenseBook.Spoken(group.Sum)}")]);
        var text = $"{label} toplam {ExpenseBook.Spoken(total)} harcadın; en çok {top}.";
        if (period is "" or "ay" && book.MonthlyBudget > 0)
        {
            var left = book.MonthlyBudget - total;
            var daysLeft = DateTime.DaysInMonth(now.Year, now.Month) - now.Day + 1;
            text += left >= 0
                ? $" Bütçende {ExpenseBook.Spoken(left)} kaldı; günde yaklaşık {ExpenseBook.Spoken(Math.Floor(left / daysLeft))} harcayabilirsin."
                : $" Aylık bütçeyi {ExpenseBook.Spoken(-left)} aştın.";
        }

        return new QuickReply(text);
    }

    private void ShowExpenseCard(ExpenseBook book, string period, IReadOnlyList<Expense>? items, DateTime now)
    {
        var (from, to, label) = ExpenseBook.Period(period.Length == 0 ? "ay" : period, now);
        var list = items ?? book.Between(from, to);
        var total = list.Sum(item => item.Amount);
        var gauges = new List<CardGauge>();
        if (book.MonthlyBudget > 0 && period is "" or "ay")
        {
            gauges.Add(new CardGauge("Bütçe", (double)Math.Min(1, total / book.MonthlyBudget), $"%{Math.Round(total / book.MonthlyBudget * 100)}"));
        }

        ShowCard(new AnswerCard($"Harcamalar · {label.ToLower(Turkish)}", "")
        {
            Big = ExpenseBook.Money(total),
            BigCaption = $"{list.Count} harcama",
            Gauges = gauges,
            Rows = items is null
                ? [.. list.GroupBy(item => item.Category).Select(group => (group.Key, Sum: group.Sum(item => item.Amount))).OrderByDescending(group => group.Sum)
                    .Take(MaxCardRows).Select(group => new CardRow(group.Key, ExpenseBook.Money(group.Sum)))]
                : [.. list.TakeLast(MaxCardRows).Select(item => new CardRow($"{item.When:dd.MM} {(item.Note.Length > 0 ? item.Note : item.Category)}", ExpenseBook.Money(item.Amount)))]
        });
    }

    private static decimal MonthTotal(ExpenseBook book, DateTime now)
    {
        var (from, to, _) = ExpenseBook.Period("ay", now);
        return book.Summary(from, to).Total;
    }

    /// <summary>Bütçenin %80'i ya da tamamı bu harcamayla aşıldıysa uyarı; değilse boş.</summary>
    internal static string BudgetNote(decimal budget, decimal before, decimal after)
    {
        if (budget <= 0)
        {
            return "";
        }

        if (after > budget)
        {
            return before <= budget
                ? $" Dikkat: aylık bütçeni {ExpenseBook.Spoken(after - budget)} aştın."
                : $" Bütçenin {ExpenseBook.Spoken(after - budget)} üstündesin.";
        }

        return before < budget * 0.8m && after >= budget * 0.8m
            ? $" Aylık bütçenin yüzde {Math.Round(after / budget * 100)}'ine geldin; {ExpenseBook.Spoken(budget - after)} kaldı."
            : "";
    }

    /// <summary>Gün sonu özetine alışkanlıklar ve bugünkü harcamalar.</summary>
    private void LifeDayEnd(List<string> parts, List<CardRow> rows, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (HabitBook is { } book && book.Habits.Count > 0)
        {
            var pending = book.Pending(today);
            var done = book.Habits.Count - pending.Count;
            rows.Add(new CardRow("Alışkanlık", $"{done}/{book.Habits.Count}"));
            parts.Add(pending.Count == 0
                ? "Bugünkü bütün alışkanlıklarını yaptın."
                : $"Alışkanlıklardan {TurkishGrammar.Join([.. pending.Select(habit => habit.Name.ToLower(Turkish))])} henüz işaretlenmedi; yaptıysan söyle, işaretleyeyim.");
        }

        if (Expenses is { } expenses)
        {
            var (from, to, _) = ExpenseBook.Period("bugun", now);
            var summary = expenses.Summary(from, to);
            if (summary.Count > 0)
            {
                rows.Add(new CardRow("Harcama", ExpenseBook.Money(summary.Total)));
                parts.Add($"Bugün {ExpenseBook.Spoken(summary.Total)} harcadın.");
            }
        }
    }

    /// <summary>Sabah karşılamasına: bugünün alışkanlıkları ve bütçe durumu.</summary>
    private void LifeMorning(List<string> parts, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (HabitBook is { } book && book.Habits.Count > 0)
        {
            var streaks = book.Habits.Select(habit => (habit.Name, Streak: HabitBook.Streak(habit, today))).Where(item => item.Streak >= 3).ToList();
            parts.Add(streaks.Count > 0
                ? $"Bugün {book.Habits.Count} alışkanlığın var; {streaks[0].Name.ToLower(Turkish)} serin {streaks[0].Streak} gün, bozma."
                : $"Bugün takip ettiğin {book.Habits.Count} alışkanlık var.");
        }

        if (Expenses is { MonthlyBudget: > 0 } expenses)
        {
            var spent = MonthTotal(expenses, now);
            if (spent > expenses.MonthlyBudget * 0.8m)
            {
                parts.Add(spent > expenses.MonthlyBudget
                    ? $"Bu ay bütçeyi {ExpenseBook.Spoken(spent - expenses.MonthlyBudget)} aştın."
                    : $"Bu ay bütçende {ExpenseBook.Spoken(expenses.MonthlyBudget - spent)} kaldı.");
            }
        }
    }
}

/// <summary>Küçük metin yardımcıları.</summary>
internal static class AssistantToolsText
{
    public static bool Contains(string text, string part) =>
        part.Length > 0 && AssistantTools.Simplify(text).Contains(AssistantTools.Simplify(part), StringComparison.Ordinal);
}
