using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public sealed class NextActionTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;

    public NextActionTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")))
        {
            Todos = new TodoStore(Path.Combine(_temp.Path, "todos.json")),
            Expenses = new ExpenseBook(Path.Combine(_temp.Path, "expenses.json"))
        };
    }

    private Task<QuickReply?> Say(string sentence) => _tools.RunQuickAsync(QuickIntents.Match(sentence)!, CancellationToken.None);

    [Theory]
    [InlineData("şimdi ne yapayım")]
    [InlineData("ne yapmalıyım")]
    [InlineData("nereden başlayayım")]
    [InlineData("bir öneri ver")]
    public void RecognizesTheQuestion(string sentence) => Assert.Equal("ne_yapayim", QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("panodaki fişi harcamaya ekle")]
    [InlineData("fişi kaydet")]
    public void RecognizesReceipts(string sentence) => Assert.Equal("fis", QuickIntents.Match(sentence)?.Tool);

    [Fact]
    public async Task PutsLateWorkFirst()
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        _tools.Todos!.Add("Süt al", now);
        _tools.Todos.Add("Raporu bitir", now, today.AddDays(-1));
        _tools.Todos.Add("Sunumu hazırla", now, today);
        var reply = await Say("şimdi ne yapayım");
        Assert.StartsWith("\"Raporu bitir\" gecikti;", reply!.Text, StringComparison.Ordinal);
        Assert.Contains("Ondan sonra: \"sunumu hazırla\"", reply.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestsSomethingCalmWhenNothingIsWaiting()
    {
        var reply = await Say("ne yapayım");
        Assert.False(string.IsNullOrWhiteSpace(reply!.Text));
        Assert.DoesNotContain("gecikti", reply.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TUTAR=1.234,50;YER=Migros", 1234.50, "Migros")]
    [InlineData("TUTAR=89,90 TL; YER=Kahve Dünyası", 89.90, "Kahve Dünyası")]
    [InlineData("tutar=123.45;yer=Shell", 123.45, "Shell")]
    [InlineData("TUTAR=2.450;YER=BİM", 2450, "BİM")]
    public void ReadsReceiptAnswers(string answer, double amount, string place)
    {
        var receipt = AssistantTools.ParseReceipt(answer);
        Assert.Equal((decimal)amount, receipt?.Amount);
        Assert.Equal(place, receipt?.Place);
    }

    [Fact]
    public void RejectsUnreadableReceipts()
    {
        Assert.Null(AssistantTools.ParseReceipt("TUTAR=yok;YER=Migros"));
        Assert.Null(AssistantTools.ParseReceipt("Bu fiş okunmuyor."));
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
