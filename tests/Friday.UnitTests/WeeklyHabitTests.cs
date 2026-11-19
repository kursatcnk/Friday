using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class WeeklyHabitTests
{
    // 19 Kasım 2026 perşembe; haftanın pazartesisi 16 Kasım.
    private static readonly DateOnly Thursday = new(2026, 11, 19);

    [Theory]
    [InlineData("spor, haftada 3", "spor", 3)]
    [InlineData("haftada üç kez yüzmek", "yüzmek", 3)]
    [InlineData("haftada 2 gün koşu", "koşu", 2)]
    [InlineData("kitap okumak", "kitap okumak", 0)]
    public void ParsesWeeklyTargets(string text, string name, int perWeek) => Assert.Equal((name, perWeek), HabitBook.ParseTarget(text));

    [Fact]
    public void CountsWeeksAndPendingByTarget()
    {
        using var temp = new TempDirectory();
        var book = new HabitBook(Path.Combine(temp.Path, "habits.json"));
        var habit = book.Add("Spor", Thursday.AddDays(-30), perWeek: 3);
        Assert.True(habit.IsWeekly);

        // Önceki iki hafta hedef tuttu (3'er gün), bu hafta pazartesi bir kez.
        foreach (var day in new[] { -17, -15, -13, -10, -8, -6, -3 })
        {
            book.Mark("spor", Thursday.AddDays(day));
        }

        habit = book.Find("spor")!;
        Assert.Equal(1, HabitBook.WeekCount(habit, Thursday));
        Assert.Equal(2, HabitBook.Streak(habit, Thursday));
        Assert.Single(book.Pending(Thursday));
        // Perşembe: 4 gün kaldı, 2 gerekiyor; henüz tehlike yok. Cumartesi: 2 gün kaldı, 2 gerekiyor.
        Assert.False(HabitBook.WeekAtRisk(habit, Thursday));
        Assert.True(HabitBook.WeekAtRisk(habit, Thursday.AddDays(2)));

        book.Mark("spor", Thursday.AddDays(-1));
        book.Mark("spor", Thursday);
        habit = book.Find("spor")!;
        Assert.Equal(3, HabitBook.Streak(habit, Thursday));
        Assert.Empty(book.Pending(Thursday.AddDays(1)));
        Assert.Equal("Spor: bu hafta 3/3, 3 haftadır hedefte", HabitBook.Describe(habit, Thursday));
    }

    [Fact]
    public async Task AddsWeeklyHabitsByVoice()
    {
        using var temp = new TempDirectory();
        using var http = new HttpClient();
        var paths = new FridayPaths(temp.Path);
        using var tools = new AssistantTools(http, new OllamaChatClient(http, new OllamaServer(paths, http)),
            new MemoryStore(Path.Combine(temp.Path, "memory.json")), new PrivacyLedger(temp.Path), new ChatStore(Path.Combine(temp.Path, "chats")),
            new ReminderStore(Path.Combine(temp.Path, "reminders.json")), new NoteStore(Path.Combine(temp.Path, "notes.md")))
        {
            HabitBook = new HabitBook(Path.Combine(temp.Path, "habit-book.json"))
        };
        var added = await tools.RunQuickAsync(QuickIntents.Match("haftada 3 kez spor alışkanlığı ekle")!, CancellationToken.None);
        Assert.Contains("haftada 3 kez", added!.Text, StringComparison.Ordinal);
        Assert.Equal(3, tools.HabitBook!.Find("spor")!.PerWeek);
        var done = await tools.RunQuickAsync(QuickIntents.Match("bugün spor yaptım")!, CancellationToken.None);
        Assert.Contains("bu hafta 1/3", done!.Text, StringComparison.Ordinal);
    }
}
