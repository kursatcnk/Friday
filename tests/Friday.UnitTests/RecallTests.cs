using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class TodoDueTests
{
    private static readonly DateTime Thursday = new(2026, 11, 19, 10, 0, 0);

    [Theory]
    [InlineData("cumaya kadar raporu bitir", "raporu bitir", "2026-11-20", false)]
    [InlineData("raporu yarına kadar gönder", "raporu gönder", "2026-11-20", false)]
    [InlineData("yarın bankaya git", "bankaya git", "2026-11-20", false)]
    [InlineData("acil: faturayı öde", "faturayı öde", null, true)]
    [InlineData("vergi beyannamesini 25 kasıma kadar ver", "vergi beyannamesini ver", "2026-11-25", false)]
    [InlineData("ay sonuna kadar kirayı yatır, önemli", "kirayı yatır", "2026-11-30", true)]
    [InlineData("hafta sonuna kadar odayı topla", "odayı topla", "2026-11-21", false)]
    [InlineData("perşembeye kadar sunumu hazırla", "sunumu hazırla", "2026-11-19", false)]
    [InlineData("süt al", "süt al", null, false)]
    [InlineData("deneme sunumunu bugün hazırla", "deneme sunumunu hazırla", "2026-11-19", false)]
    [InlineData("raporu yarın gönder", "raporu gönder", "2026-11-20", false)]
    public void ExtractsDeadlines(string text, string task, string? due, bool important)
    {
        var (clean, until, urgent) = TodoDue.Extract(text, Thursday);
        Assert.Equal(task, clean);
        Assert.Equal(due is null ? null : DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture), until);
        Assert.Equal(important, urgent);
    }

    [Theory]
    [InlineData(-1, "Gecikti")]
    [InlineData(0, "Bugün")]
    [InlineData(1, "Yarın")]
    [InlineData(2, "Cumartesi")]
    [InlineData(10, "29 Kas")]
    public void LabelsDeadlines(int days, string label) =>
        Assert.Equal(label, TodoDue.Label(DateOnly.FromDateTime(Thursday).AddDays(days), DateOnly.FromDateTime(Thursday)));

    [Fact]
    public void SortsByDeadlineThenImportance()
    {
        using var temp = new TempDirectory();
        var store = new TodoStore(Path.Combine(temp.Path, "todos.json"));
        var today = DateOnly.FromDateTime(Thursday);
        store.Add("Süt al", Thursday);
        store.Add("Faturayı öde", Thursday.AddMinutes(1), important: true);
        store.Add("Raporu bitir", Thursday.AddMinutes(2), today.AddDays(1));
        store.Add("Sunumu hazırla", Thursday.AddMinutes(3), today);
        Assert.Equal(["Sunumu hazırla", "Raporu bitir", "Faturayı öde", "Süt al"], store.Open.Select(item => item.Text));

        // Aynı iş yeniden söylenirse son tarihi güncellenir; yeni iş eklenmez.
        store.Add("süt al", Thursday, today.AddDays(3));
        Assert.Equal(4, store.Open.Count);
        Assert.Equal(today.AddDays(3), new TodoStore(Path.Combine(temp.Path, "todos.json")).Open.Single(item => item.Text == "Süt al").Due);
    }
}

public class RecallIntentTests
{
    [Theory]
    [InlineData("her yerde kira ara", "kira")]
    [InlineData("kayıtlarımda ara: doktor randevusu", "doktor randevusu")]
    [InlineData("kira hakkında ne kaydetmiştim", "kira")]
    [InlineData("ahmet ile ilgili ne not almıştım", "ahmet")]
    public void SearchesEverywhere(string sentence, string query)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("her_yerde_ara", command?.Tool);
        Assert.Equal(query, command!.Arguments["sorgu"]);
    }

    [Theory]
    [InlineData("ben yokken ne dedin", "kacirdiklarim")]
    [InlineData("kaçırdıklarım neler", "kacirdiklarim")]
    [InlineData("seçili kelimeyi desteye ekle", "kelime_cevir")]
    [InlineData("resilient kelimesini desteye ekle", "kelime_cevir")]
    [InlineData("ekrandaki yazıyı kopyala", "ekran_metni")]
    [InlineData("panodakini yapılacaklara ekle", "pano_kaydet")]
    [InlineData("kopyaladığımı not al", "pano_kaydet")]
    [InlineData("panodakini market listesine ekle", "pano_kaydet")]
    public void RecognizesRecallCommands(string sentence, string tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("süt almayı not al", "not")]
    [InlineData("market listesine süt ekle", "liste")]
    [InlineData("yapılacaklara ekle: cumaya kadar raporu bitir", "yapilacak")]
    public void KeepsOlderCommands(string sentence, string tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool);
}

public sealed class RecallToolTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly MemoryStore _memory;
    private readonly NoteStore _notes;
    private readonly AssistantTools _tools;

    public RecallToolTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _memory = new MemoryStore(Path.Combine(_temp.Path, "memory.json"));
        _notes = new NoteStore(Path.Combine(_temp.Path, "notes.md"));
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)), _memory, new PrivacyLedger(_temp.Path),
            new ChatStore(Path.Combine(_temp.Path, "chats")), new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), _notes)
        {
            Todos = new TodoStore(Path.Combine(_temp.Path, "todos.json")),
            Expenses = new ExpenseBook(Path.Combine(_temp.Path, "expenses.json")),
            Journal = new JournalStore(Path.Combine(_temp.Path, "journal")),
            Lists = new ListStore(Path.Combine(_temp.Path, "lists.json"))
        };
    }

    private Task<QuickReply?> Say(string sentence) => _tools.RunQuickAsync(QuickIntents.Match(sentence)!, CancellationToken.None);

    [Fact]
    public async Task AddsTodosWithDeadlines()
    {
        var reply = await Say("yapılacaklara ekle: acil: cumaya kadar raporu bitir");
        Assert.StartsWith("Yapılacaklara ekledim: Raporu bitir. Son gün ", reply!.Text, StringComparison.Ordinal);
        Assert.EndsWith("Önemli olarak işaretledim.", reply.Text, StringComparison.Ordinal);
        var todo = Assert.Single(_tools.Todos!.Open);
        Assert.True(todo.Important);
        Assert.Equal(DayOfWeek.Friday, todo.Due!.Value.DayOfWeek);
        var list = await Say("yapılacaklarım neler");
        Assert.StartsWith("Tek bir iş var: Raporu bitir (", list!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchesAllRecords()
    {
        var now = DateTime.Now;
        _memory.Remember("Kullanıcının kira sözleşmesi Mart'ta yenilenecek.");
        _notes.Add("Ev sahibiyle kira artışını konuştuk", now);
        _tools.Expenses!.Add(15000, "kiraya", now);
        _tools.Todos!.Add("Kira dekontunu sakla", now);
        _tools.Lists!.Add("market", "süt, ekmek");
        var reply = await Say("her yerde kira ara");
        Assert.StartsWith("\"kira\" için 4 kayıt buldum:", reply!.Text, StringComparison.Ordinal);
        Assert.Contains("hafızada 1", reply.Text, StringComparison.Ordinal);
        Assert.Contains("harcamalarda 1", reply.Text, StringComparison.Ordinal);
        Assert.Equal(4, reply.Card!.Rows.Count);
        Assert.Contains("hiçbir kaydında geçmiyor", (await Say("her yerde zürafa ara"))!.Text, StringComparison.Ordinal);
        Assert.Contains("market listesinde 1", (await Say("her yerde ekmek ara"))!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatsRecentAnnouncements()
    {
        Assert.Equal("Son saatlerde kendiliğimden bir şey söylemedim.", (await Say("ben yokken ne dedin"))!.Text);
        var now = DateTime.Now;
        _tools.Announcements = () => [(now.AddHours(-8), "Eski"), (now.AddMinutes(-30), "Çamaşırları asmayı hatırlatıyorum."), (now.AddMinutes(-5), "Göz molası.")];
        var reply = await Say("ben yokken ne dedin");
        Assert.StartsWith("Söylediklerim: ", reply!.Text, StringComparison.Ordinal);
        Assert.Contains("Çamaşırları asmayı hatırlatıyorum", reply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Eski", reply.Text, StringComparison.Ordinal);
        Assert.Equal(2, reply.Card!.Rows.Count);
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
