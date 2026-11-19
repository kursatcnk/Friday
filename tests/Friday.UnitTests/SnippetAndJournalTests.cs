using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class SnippetIntentTests
{
    [Theory]
    [InlineData("imza kısaltması: Saygılarımla, Kürşat Çankaroğlu", "imza", "Saygılarımla, Kürşat Çankaroğlu")]
    [InlineData("Friday, adres kısaltması olarak kaydet: Atatürk Cad. No 5", "adres", "Atatürk Cad. No 5")]
    [InlineData("kısaltma ekle IBAN: TR00 0000", "iban", "TR00 0000")]
    public void DefinesSnippetsKeepingCase(string sentence, string name, string text)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("kisaltma", command?.Tool);
        Assert.Equal("ekle", command!.Arguments["islem"]);
        Assert.Equal(name, command.Arguments["ad"]);
        Assert.Equal(text, command.Arguments["metin"]);
    }

    [Theory]
    [InlineData("imzamı yaz", "imza")]
    [InlineData("adresimi yapıştır", "adres")]
    [InlineData("iban'ımı yaz", "iban")]
    public void UsesSnippetsTentatively(string sentence, string name)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("kisaltma", command?.Tool);
        Assert.Equal("kullan", command!.Arguments["islem"]);
        Assert.StartsWith(name, command.Arguments["ad"], StringComparison.Ordinal);
        Assert.True(command.Tentative);
    }

    [Theory]
    [InlineData("panodakini adres olarak kaydet", "panodan")]
    [InlineData("kısaltmalarım neler", "listele")]
    [InlineData("imza kısaltmasını sil", "sil")]
    public void ManagesSnippets(string sentence, string action) => Assert.Equal(action, QuickIntents.Match(sentence)?.Arguments["islem"]);

    [Theory]
    [InlineData("günümü yazalım")]
    [InlineData("bana günümü sor")]
    public void AsksForTheJournalQuestion(string sentence) => Assert.Equal("gunluk_sorusu", QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("şimdi değil", true)]
    [InlineData("boş ver", true)]
    [InlineData("güzel geçti", false)]
    public void RecognizesDeclines(string text, bool decline) => Assert.Equal(decline, QuickIntents.IsDecline(text));

    [Fact]
    public void StillTypesDictatedText() => Assert.Equal("yaz", QuickIntents.Match("şunu yaz: merhaba dünya")?.Tool);
}

public sealed class SnippetAndJournalToolTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;
    private FridaySettings? _saved;

    public SnippetAndJournalToolTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")))
        {
            Journal = new JournalStore(Path.Combine(_temp.Path, "journal")),
            HabitBook = new HabitBook(Path.Combine(_temp.Path, "habit-book.json")),
            SaveSettings = settings => _saved = settings
        };
    }

    private Task<QuickReply?> Run(QuickCommand command) => _tools.RunQuickAsync(command, CancellationToken.None);

    [Fact]
    public async Task SavesListsAndRemovesSnippets()
    {
        var saved = await Run(QuickIntents.Match("imza kısaltması: Saygılarımla, Kürşat")!);
        Assert.Contains("imzamı yaz", saved!.Text, StringComparison.Ordinal);
        Assert.Equal("Saygılarımla, Kürşat", _saved?.Snippets["imza"]);
        Assert.Contains("adresimi yaz", (await Run(QuickIntents.Match("adres kısaltması: Atatürk Cad. No 5")!))!.Text, StringComparison.Ordinal);
        Assert.Equal(2, (await Run(QuickIntents.Match("kısaltmalarım")!))!.Card!.Rows.Count);

        // Kayıtlı olmayan bir kısaltma dil modeline kalır.
        Assert.Null(await Run(QuickIntents.Match("şiirimi yaz")!));
        Assert.Contains("sildim", (await Run(QuickIntents.Match("imza kısaltmasını sil")!))!.Text, StringComparison.Ordinal);
        Assert.False(_tools.Settings.Snippets.ContainsKey("imza"));
    }

    [Fact]
    public async Task WritesTheEveningAnswerToTheJournal()
    {
        _tools.HabitBook!.Add("Spor", DateOnly.FromDateTime(DateTime.Now));
        var now = DateTime.Now;
        var question = _tools.JournalQuestion(now);
        Assert.StartsWith("Günün nasıl geçti?", question, StringComparison.Ordinal);
        Assert.Contains("spor işaretlenmedi", question, StringComparison.Ordinal);
        Assert.True(_tools.AwaitsAnswer);
        Assert.Null(_tools.JournalQuestion(now));

        var reply = await Run(new QuickCommand("serbest_cevap", new Dictionary<string, string> { ["metin"] = "Yoğundu ama sunum iyi geçti, akşam spora da gittim." }));
        Assert.Contains("Günlüğüne yazdım", reply!.Text, StringComparison.Ordinal);
        Assert.Contains("Spor da işaretlendi", reply.Text, StringComparison.Ordinal);
        Assert.False(_tools.AwaitsAnswer);
        var entry = Assert.Single(_tools.Journal!.Between(now.Date, now.AddMinutes(1)));
        Assert.Equal("Gün sonu: Yoğundu ama sunum iyi geçti, akşam spora da gittim.", entry.Text);
    }

    [Fact]
    public async Task SkipsTheJournalWhenDeclined()
    {
        Assert.NotNull(_tools.JournalQuestion(DateTime.Now, force: true));
        var reply = await Run(new QuickCommand("serbest_cevap", new Dictionary<string, string> { ["metin"] = "şimdi değil" }));
        Assert.Equal("Peki, başka zaman.", reply!.Text);
        Assert.Empty(_tools.Journal!.Between(DateTime.Today, DateTime.Now.AddMinutes(1)));
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
