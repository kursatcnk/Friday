using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class HabitIntentTests
{
    [Theory]
    [InlineData("alışkanlık ekle: kitap okumak", "kitap okumak")]
    [InlineData("yeni alışkanlık meditasyon", "meditasyon")]
    [InlineData("sabah yürüyüşünü alışkanlıklarıma ekle", "sabah yürüyüşünü")]
    [InlineData("İngilizce çalışma alışkanlığı ekle", "ingilizce çalışma")]
    [InlineData("Friday, kitap okuma alışkanlığını başlat", "kitap okuma")]
    public void AddsHabits(string sentence, string name)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("aliskanlik", command?.Tool);
        Assert.Equal("ekle", command!.Arguments["islem"]);
        Assert.Equal(name, command.Arguments["ad"]);
    }

    [Theory]
    [InlineData("bugün spor yaptım", "spor")]
    [InlineData("kitap okudum", "kitap")]
    [InlineData("ilacımı aldım", "ilacımı")]
    [InlineData("yürüyüşe gittim", "yürüyüşe")]
    [InlineData("az önce meditasyon yaptım", "meditasyon")]
    public void MarksHabitsTentatively(string sentence, string name)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("aliskanlik", command?.Tool);
        Assert.Equal("yaptim", command!.Arguments["islem"]);
        Assert.Equal(name, command.Arguments["ad"]);
        Assert.True(command.Tentative);
    }

    [Theory]
    [InlineData("alışkanlıklarım nasıl gidiyor", "listele")]
    [InlineData("alışkanlıklarım", "listele")]
    [InlineData("serilerim nasıl", "listele")]
    [InlineData("spor alışkanlığını sil", "sil")]
    [InlineData("bugün spor yapmadım", "geri_al")]
    public void ListsRemovesAndUndoes(string sentence, string action) => Assert.Equal(action, QuickIntents.Match(sentence)?.Arguments["islem"]);

    [Theory]
    [InlineData("su içtim", "ritim")]
    [InlineData("bugün ne kadar çalıştım", "ritim")]
    [InlineData("faturayı ödemeyi tamamladım", "yapilacak")]
    public void KeepsOlderCommands(string sentence, string tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool);
}

public class HabitBookTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);
    private static readonly bool[] ExpectedWeek = [false, false, true, true, true, true, false];

    [Fact]
    public void FindsHabitsDespiteSuffixes()
    {
        using var temp = new TempDirectory();
        var book = new HabitBook(Path.Combine(temp.Path, "habits.json"));
        book.Add("Kitap okumak", Today);
        book.Add("spor", Today);
        book.Add("Meditasyon", Today);
        book.Add("İlaç içmek", Today);

        Assert.Equal("Spor", book.Find("spora")?.Name);
        Assert.Equal("Kitap okumak", book.Find("kitabımı")?.Name);
        Assert.Equal("Kitap okumak", book.Find("okudum")?.Name);
        Assert.Equal("Meditasyon", book.Find("meditasyonu")?.Name);
        Assert.Equal("İlaç içmek", book.Find("ilacımı")?.Name);
        Assert.Null(book.Find("bulaşık"));
        Assert.Same(book.Find("spor"), book.Add("SPOR", Today));
        Assert.Equal(4, book.Habits.Count);
    }

    [Fact]
    public void CountsStreaksWeeksAndRates()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "habits.json");
        var book = new HabitBook(file);
        book.Add("Spor", Today.AddDays(-9));
        foreach (var offset in new[] { 9, 8, 7, 4, 3, 2, 1 })
        {
            Assert.True(book.Mark("spor", Today.AddDays(-offset)));
        }

        var habit = book.Find("spor")!;
        // Bugün henüz yapılmadı: seri dünden geriye sayılır.
        Assert.Equal(4, HabitBook.Streak(habit, Today));
        Assert.Equal(4, HabitBook.Best(habit));
        Assert.Equal(ExpectedWeek, HabitBook.Week(habit, Today));
        Assert.Equal(0.7, HabitBook.Rate(habit, Today), 2);
        Assert.Single(book.Pending(Today));

        Assert.True(book.Mark("spor", Today));
        Assert.False(book.Mark("spor", Today));
        Assert.Equal(5, HabitBook.Streak(new HabitBook(file).Find("spor")!, Today));
        Assert.Empty(book.Pending(Today));

        Assert.True(book.Mark("spor", Today, done: false));
        Assert.True(book.Remove("spor"));
        Assert.Empty(new HabitBook(file).Habits);
    }
}

public class ExpenseIntentTests
{
    [Theory]
    [InlineData("kahveye 80 lira harcadım", 80, "kahveye")]
    [InlineData("markete 450 lira verdim", 450, "markete")]
    [InlineData("taksiye 120 tl ödedim", 120, "taksiye")]
    [InlineData("market alışverişine iki yüz elli lira verdim", 250, "market alışverişine")]
    [InlineData("80 liralık kahve aldım", 80, "kahve")]
    [InlineData("kiraya 15 bin lira gitti", 15000, "kiraya")]
    [InlineData("1.250 lira harcadım", 1250, "")]
    [InlineData("bugün yemeğe seksen lira harcadım", 80, "yemeğe")]
    [InlineData("harcama ekle: 60 lira otopark", 60, "otopark")]
    public void ParsesSpending(string sentence, double amount, string note)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("harcama", command?.Tool);
        Assert.Equal("ekle", command!.Arguments["islem"]);
        Assert.Equal(amount, double.Parse(command.Arguments["tutar"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(note, command.Arguments.GetValueOrDefault("aciklama", ""));
    }

    [Theory]
    [InlineData("bu ay ne kadar harcadım", "ay", "")]
    [InlineData("bu hafta kahveye ne kadar harcadım", "hafta", "kahve")]
    [InlineData("dün kaç lira harcadım", "dun", "")]
    [InlineData("harcamalarım", "ay", "")]
    [InlineData("geçen ayki harcamalarım", "gecen_ay", "")]
    [InlineData("bütçem ne durumda", "ay", "")]
    public void AsksForSummaries(string sentence, string period, string category)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("harcama", command?.Tool);
        Assert.Equal("ozet", command!.Arguments["islem"]);
        Assert.Equal(period, command.Arguments["donem"]);
        Assert.Equal(category, command.Arguments.GetValueOrDefault("kategori", ""));
    }

    [Theory]
    [InlineData("aylık bütçem 15 bin lira", "butce", "15000")]
    [InlineData("bütçemi 20000 liraya ayarla", "butce", "20000")]
    [InlineData("son harcamayı sil", "sil_son", "")]
    [InlineData("harcamaları excel'e aktar", "disa_aktar", "")]
    public void HandlesBudgetUndoAndExport(string sentence, string action, string amount)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("harcama", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(amount, command.Arguments.GetValueOrDefault("tutar", ""));
    }

    [Theory]
    [InlineData("15 bin", 15000)]
    [InlineData("on beş bin", 15000)]
    [InlineData("iki bin beş yüz", 2500)]
    [InlineData("1,5 bin", 1500)]
    [InlineData("seksen", 80)]
    public void ReadsMoney(string text, double amount) => Assert.Equal(amount, QuickIntents.ParseMoney(text));
}

public class ExpenseBookTests
{
    [Theory]
    [InlineData("kahveye", "kahve", "Yeme-içme")]
    [InlineData("markete", "market", "Market")]
    [InlineData("taksiye", "taksi", "Ulaşım")]
    [InlineData("telefon faturasına", "telefon faturası", "Faturalar")]
    [InlineData("Migros'a", "Migros", "Market")]
    [InlineData("yemeğe", "yemek", "Yeme-içme")]
    [InlineData("benzine", "benzin", "Ulaşım")]
    [InlineData("ilaca", "ilaç", "Sağlık")]
    [InlineData("kiraya", "kira", "Kira")]
    [InlineData("netflix'e", "netflix", "Eğlence")]
    [InlineData("çiçeğe", "çiçek", "Hediye")]
    [InlineData("vergi", "vergi", "Diğer")]
    public void CleansNotesAndGuessesCategories(string spoken, string note, string category)
    {
        Assert.Equal(note, ExpenseBook.CleanNote(spoken));
        Assert.Equal(category, ExpenseBook.Guess(ExpenseBook.CleanNote(spoken)));
    }

    [Fact]
    public void SummarizesPeriodsAndExports()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "expenses.json");
        var book = new ExpenseBook(file);
        var now = new DateTime(2026, 11, 19, 18, 0, 0);
        book.Add(80, "kahveye", now);
        book.Add(450, "markete", now.AddDays(-1));
        book.Add(120, "taksiye", now.AddDays(-25));
        book.Add(60, "kahve", now.AddDays(-3));

        var (from, to, label) = ExpenseBook.Period("ay", now);
        Assert.Equal("Bu ay", label);
        var month = book.Summary(from, to);
        Assert.Equal(590, month.Total);
        Assert.Equal(("Market", 450m), month.Categories[0]);
        Assert.Equal(140, book.Summary(from, to, "Yeme-içme").Total);

        var (weekFrom, weekTo, _) = ExpenseBook.Period("hafta", now);
        Assert.Equal(new DateTime(2026, 11, 16), weekFrom);
        Assert.Equal(590, book.Summary(weekFrom, weekTo).Total);
        var (lastFrom, lastTo, _) = ExpenseBook.Period("gecen_ay", now);
        Assert.Equal(120, book.Summary(lastFrom, lastTo).Total);

        book.MonthlyBudget = 1000;
        Assert.Equal(1000, new ExpenseBook(file).MonthlyBudget);
        Assert.Equal("kahve", book.RemoveLast()?.Note);
        Assert.Equal(3, new ExpenseBook(file).Items.Count);

        var csv = Path.Combine(temp.Path, "out", "harcamalar.csv");
        Assert.Equal(3, book.ExportCsv(csv));
        var lines = File.ReadAllLines(csv);
        Assert.Equal("Tarih;Saat;Tutar (TL);Kategori;Açıklama", lines[0]);
        Assert.Contains(lines, line => line.Contains(";450,00;Market;market", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 100, 900, "")]
    [InlineData(1000, 700, 850, "yüzde 85")]
    [InlineData(1000, 900, 1100, "aştın")]
    [InlineData(1000, 1100, 1200, "üstündesin")]
    [InlineData(1000, 100, 200, "")]
    public void WarnsNearTheBudget(int budget, int before, int after, string expected)
    {
        var note = AssistantTools.BudgetNote(budget, before, after);
        if (expected.Length == 0)
        {
            Assert.Equal("", note);
        }
        else
        {
            Assert.Contains(expected, note, StringComparison.Ordinal);
        }
    }
}

public sealed class LifeToolTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;

    public LifeToolTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")))
        {
            HabitBook = new HabitBook(Path.Combine(_temp.Path, "habit-book.json")),
            Expenses = new ExpenseBook(Path.Combine(_temp.Path, "expenses.json")),
            Todos = new TodoStore(Path.Combine(_temp.Path, "todos.json")),
            SaveFolder = Path.Combine(_temp.Path, "Kaydedilenler")
        };
    }

    private Task<QuickReply?> Say(string sentence) => _tools.RunQuickAsync(QuickIntents.Match(sentence)!, CancellationToken.None);

    [Fact]
    public async Task TracksHabitsByVoice()
    {
        Assert.Null(await Say("bugün spor yaptım"));
        Assert.Contains("kitap okudum", (await Say("alışkanlık ekle: kitap okumak"))!.Text, StringComparison.Ordinal);
        await Say("alışkanlık ekle: spor");

        var done = await Say("kitap okudum");
        Assert.Contains("Kitap okumak", done!.Text, StringComparison.Ordinal);
        Assert.Contains("spor", done.Text, StringComparison.Ordinal);
        Assert.Equal("Alışkanlıklar", done.Card?.Title);

        var both = await Say("spora gittim");
        Assert.Contains("bütün alışkanlıklarını", both!.Text, StringComparison.Ordinal);
        Assert.Contains("zaten", (await Say("bugün spor yaptım"))!.Text, StringComparison.Ordinal);

        var report = await Say("alışkanlıklarım nasıl gidiyor");
        Assert.Contains("2 alışkanlığından bugün 2", report!.Text, StringComparison.Ordinal);
        Assert.Equal(2, report.Card!.Rows.Count);

        Assert.Contains("kaldırdım", (await Say("bugün spor yapmadım"))!.Text, StringComparison.Ordinal);
        Assert.Contains("İşaretledim", (await Say("sporu tamamladım"))!.Text, StringComparison.Ordinal);
        Assert.Contains("takipten", (await Say("spor alışkanlığını sil"))!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogsSpendingAndWarnsAboutTheBudget()
    {
        Assert.Contains("1.000 lira", (await Say("aylık bütçem 1000 lira"))!.Text, StringComparison.Ordinal);
        var coffee = await Say("kahveye 80 lira harcadım");
        Assert.Contains("kahve, 80 lira", coffee!.Text, StringComparison.Ordinal);
        Assert.Equal("80 TL", coffee.Card?.Big);

        var market = await Say("markete 750 lira verdim");
        Assert.Contains("yüzde 83", market!.Text, StringComparison.Ordinal);

        var summary = await Say("bu ay ne kadar harcadım");
        Assert.Contains("830 lira", summary!.Text, StringComparison.Ordinal);
        Assert.Contains("market 750 lira", summary.Text, StringComparison.Ordinal);
        Assert.Contains("kaldı", summary.Text, StringComparison.Ordinal);

        Assert.Contains("80 lira", (await Say("bu ay kahveye ne kadar harcadım"))!.Text, StringComparison.Ordinal);
        Assert.Contains("market", (await Say("son harcamayı sil"))!.Text, StringComparison.Ordinal);
        Assert.Contains("1 harcamayı", (await Say("harcamaları excel'e aktar"))!.Text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
