using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public sealed class ReportTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;

    public ReportTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")))
        {
            Todos = new TodoStore(Path.Combine(_temp.Path, "todos.json")),
            HabitBook = new HabitBook(Path.Combine(_temp.Path, "habit-book.json")),
            Expenses = new ExpenseBook(Path.Combine(_temp.Path, "expenses.json")),
            Journal = new JournalStore(Path.Combine(_temp.Path, "journal")),
            SaveFolder = Path.Combine(_temp.Path, "Kaydedilenler")
        };
    }

    [Theory]
    [InlineData("haftalık raporu kaydet")]
    [InlineData("haftamı dosyaya kaydet")]
    [InlineData("bu haftanın raporunu hazırla")]
    public void RecognizesWeeklyReports(string sentence) => Assert.Equal("haftalik_rapor", QuickIntents.Match(sentence)?.Tool);

    [Fact]
    public async Task WritesTheWeekToAFile()
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        _tools.HabitBook!.Add("Kitap okumak", today.AddDays(-3));
        _tools.HabitBook.Mark("kitap", today.AddDays(-1));
        _tools.Expenses!.Add(80, "kahveye", now);
        _tools.Todos!.Add("Raporu bitir", now, today.AddDays(1), important: true);
        _tools.Journal!.Add(now, "Sunum iyi geçti");

        var reply = await _tools.RunQuickAsync(QuickIntents.Match("haftalık raporu kaydet")!, CancellationToken.None);
        Assert.StartsWith("Haftalık raporunu hazırladım.", reply!.Text, StringComparison.Ordinal);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_temp.Path, "Kaydedilenler"), "*.md"));
        var text = await File.ReadAllTextAsync(file);
        Assert.StartsWith("# Haftalık rapor · ", text, StringComparison.Ordinal);
        Assert.Contains("- Kitap okumak: ○○○○○●○ 1/7", text, StringComparison.Ordinal);
        Assert.Contains("## Harcamalar · 80 TL", text, StringComparison.Ordinal);
        Assert.Contains("- [ ] ★ Raporu bitir (Yarın)", text, StringComparison.Ordinal);
        Assert.Contains("Sunum iyi geçti", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MentionsDeadlinesTheEveningBefore()
    {
        var now = DateTime.Now;
        _tools.Todos!.Add("Raporu bitir", now, DateOnly.FromDateTime(now).AddDays(1));
        _tools.Todos.Add("Süt al", now);
        Assert.Equal("Yarın son günü olan iş: raporu bitir. Bu akşam biraz ilerlemek ister misin?", _tools.DeadlineNudge(now));
        Assert.Null(_tools.DeadlineNudge(now));
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
