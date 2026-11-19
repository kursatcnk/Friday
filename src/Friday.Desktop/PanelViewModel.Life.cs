using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;

namespace Friday.Desktop;

/// <summary>Alışkanlık kartında bir günün noktası: yapıldı mı, bugün mü, üstüne gelince görünen gün adı.</summary>
public sealed record HabitDay(bool Done, bool IsToday, string Tip);

/// <summary>"Bugün" ekranındaki alışkanlık satırı.</summary>
public sealed record HabitView(string Name, string Streak, bool IsDoneToday, IReadOnlyList<HabitDay> Week);

/// <summary>Günün planı kartında bir blok: saat aralığı, başlık, türü (Focus, Event, Habit, Meal) ve şu an mı.</summary>
public sealed record PlanRow(string Time, string Title, string Kind, bool IsNow);

/// <summary>Harcama kartında bir kategori: adı, tutarı ve en büyüğe göre doluluğu (0–1).</summary>
public sealed record SpendingRow(string Label, string Value, double Fraction);

/// <summary>Harcama kartındaki son harcamalardan biri.</summary>
public sealed record ExpenseView(string Id, string Text, string Amount);

public sealed partial class PanelViewModel
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    public ObservableCollection<HabitView> HabitRows { get; } = [];

    public ObservableCollection<SpendingRow> SpendingRows { get; } = [];

    public ObservableCollection<ExpenseView> RecentExpenses { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddHabitCommand))]
    private string _newHabit = "";

    [ObservableProperty]
    private string _habitSummary = "";

    [ObservableProperty]
    private string _spendingTotal = "0 TL";

    [ObservableProperty]
    private string _spendingCaption = "";

    [ObservableProperty]
    private bool _hasSpending;

    [ObservableProperty]
    private bool _hasBudget;

    [ObservableProperty]
    private double _budgetFraction;

    [ObservableProperty]
    private string _budgetText = "";

    [ObservableProperty]
    private bool _isOverBudget;

    [ObservableProperty]
    private string _billsText = "";

    /// <summary>Selam kartında günün tek satırlık özeti; gösterecek bir şey yoksa boş.</summary>
    [ObservableProperty]
    private string _todaySummary = "";

    public void ShowTodaySummary(IReadOnlyList<TodoItem> open, IReadOnlyList<DailyHabit> pending, DeckStats? words, decimal monthSpent, DateOnly today)
    {
        var parts = new List<string>();
        if (open.Count > 0)
        {
            var urgent = open.Count(item => item.Due is { } due && due <= today.AddDays(1));
            parts.Add($"{open.Count} iş" + (urgent > 0 ? $" ({urgent} tanesi acil)" : ""));
        }

        if (pending.Count > 0)
        {
            parts.Add($"{pending.Count} alışkanlık kaldı");
        }

        if (words is { Waiting: > 0 } deck)
        {
            parts.Add($"{deck.Waiting} kelime bekliyor");
        }

        if (monthSpent > 0)
        {
            parts.Add($"bu ay {ExpenseBook.Money(monthSpent)}");
        }

        TodaySummary = string.Join(" · ", parts);
    }

    public ObservableCollection<PlanRow> PlanRows { get; } = [];

    [ObservableProperty]
    private string _planSummary = "";

    [ObservableProperty]
    private bool _hasPlan;

    public void ShowPlan(IReadOnlyList<PlanBlock> blocks, DateTime now)
    {
        PlanRows.Clear();
        var visible = blocks.Where(block => block.Kind != PlanKind.Break && block.End > now).Take(7).ToList();
        HasPlan = visible.Any(block => block.Kind is PlanKind.Focus or PlanKind.Habit);
        if (!HasPlan)
        {
            PlanSummary = "";
            return;
        }

        foreach (var block in visible)
        {
            var title = block.Kind == PlanKind.Focus ? $"Odak: {block.Title}" : block.Title;
            PlanRows.Add(new PlanRow($"{block.Start:HH:mm}–{block.End:HH:mm}", title, block.Kind.ToString(), block.Start <= now && now < block.End));
        }

        var focus = blocks.Count(block => block.Kind == PlanKind.Focus);
        PlanSummary = focus > 0 ? $"{focus} odak bloğu" : "";
    }

    public void ShowHabits(IReadOnlyList<DailyHabit> habits, DateOnly today)
    {
        HabitRows.Clear();
        foreach (var habit in habits)
        {
            var streak = HabitBook.Streak(habit, today);
            var week = HabitBook.Week(habit, today)
                .Select((done, index) => new HabitDay(done, index == 6, today.AddDays(index - 6).ToString("d MMM ddd", TurkishCulture)))
                .ToList();
            var rate = HabitBook.Rate(habit, today);
            var detail = habit.IsWeekly
                ? $"Bu hafta {HabitBook.WeekCount(habit, today)}/{habit.PerWeek}" + (streak > 1 ? $" · {streak} hafta hedefte" : "")
                : streak > 1 ? $"{streak} gün seri · son 30 gün %{Math.Round(rate * 100)}" : $"Son 30 gün %{Math.Round(rate * 100)}";
            HabitRows.Add(new HabitView(habit.Name, detail, habit.IsDoneOn(today), week));
        }

        var done = habits.Count(habit => habit.IsDoneOn(today));
        HabitSummary = habits.Count == 0 ? "" : done == habits.Count ? "Bugün hepsi tamam" : $"Bugün {done}/{habits.Count}";
    }

    private bool CanAddHabit() => NewHabit.Trim().Length > 1;

    [RelayCommand(CanExecute = nameof(CanAddHabit))]
    private void AddHabit()
    {
        _actions.AddHabit(NewHabit.Trim());
        NewHabit = "";
    }

    [RelayCommand]
    private void ToggleHabit(HabitView habit) => _actions.ToggleHabit(habit.Name, !habit.IsDoneToday);

    [RelayCommand]
    private void RemoveHabit(HabitView habit) => _actions.RemoveHabit(habit.Name);

    public void ShowSpending(ExpenseBook book, DateTime now)
    {
        var (from, to, _) = ExpenseBook.Period("ay", now);
        var items = book.Between(from, to);
        var summary = book.Summary(from, to);
        HasSpending = items.Count > 0;
        SpendingTotal = ExpenseBook.Money(summary.Total);
        SpendingCaption = $"{now.ToString("MMMM", TurkishCulture)} · {summary.Count} harcama";
        var budget = book.MonthlyBudget;
        HasBudget = budget > 0;
        BudgetFraction = budget > 0 ? (double)Math.Min(1, summary.Total / budget) : 0;
        IsOverBudget = budget > 0 && summary.Total > budget;
        BudgetText = budget <= 0
            ? ""
            : summary.Total <= budget
                ? $"Bütçe {ExpenseBook.Money(budget)} · {ExpenseBook.Money(budget - summary.Total)} kaldı"
                : $"Bütçe {ExpenseBook.Money(budget)} · {ExpenseBook.Money(summary.Total - budget)} aşıldı";

        var today = DateOnly.FromDateTime(now);
        var bills = book.Bills;
        var unpaid = bills.Where(bill => !bill.IsPaid(today) && bill.DueIn(today.Year, today.Month) >= today).OrderBy(bill => bill.Day).FirstOrDefault();
        BillsText = bills.Count == 0
            ? ""
            : $"Sabit giderler {ExpenseBook.Money(bills.Sum(bill => bill.Amount))}/ay" + (unpaid is null ? "" : $" · sıradaki {unpaid.Name} ({unpaid.Day}.)");

        SpendingRows.Clear();
        var largest = summary.Categories.Count > 0 ? summary.Categories[0].Amount : 1;
        foreach (var (category, amount) in summary.Categories.Take(5))
        {
            SpendingRows.Add(new SpendingRow(category, ExpenseBook.Money(amount), largest == 0 ? 0 : (double)(amount / largest)));
        }

        RecentExpenses.Clear();
        foreach (var item in book.Items.OrderByDescending(item => item.When).Take(3))
        {
            var label = item.Note.Length > 0 ? TurkishGrammar.Capitalize(item.Note) : item.Category;
            RecentExpenses.Add(new ExpenseView(item.Id, $"{item.When.ToString("d MMM", TurkishCulture)} · {label}", ExpenseBook.Money(item.Amount)));
        }
    }

    [RelayCommand]
    private void RemoveExpense(ExpenseView item) => _actions.RemoveExpense(item.Id);

    [ObservableProperty]
    private string _vocabularyDue = "110";

    [ObservableProperty]
    private string _vocabularyDetail = "";

    [ObservableProperty]
    private string _vocabularyCaption = "";

    public void ShowVocabulary(DeckStats stats, bool started)
    {
        if (!started)
        {
            VocabularyDue = "110";
            VocabularyDetail = "sık kullanılan İngilizce kelime hazır. Friday sorar, sen Türkçesini söylersin; bildiklerini aralıklı tekrar eder.";
            VocabularyCaption = "";
            return;
        }

        var waiting = stats.Waiting;
        VocabularyDue = waiting.ToString(TurkishCulture);
        VocabularyDetail = waiting == 0 ? "Bugünlük kelimeler bitti. Yarın yine." : $"kelime bugün seni bekliyor ({stats.DueToday} tekrar).";
        VocabularyCaption = $"{stats.Learned}/{stats.Total} öğrenildi";
    }

    /// <summary>Sohbet sayfasına geçip pratiği başlatır; cevaplar yazılarak ya da sesle verilebilir.</summary>
    [RelayCommand]
    private void StartPractice()
    {
        GoTo("chat");
        _actions.Send("Kelime çalışalım");
    }
}
