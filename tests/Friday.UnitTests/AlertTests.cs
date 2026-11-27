using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class AlertIntentTests
{
    [Theory]
    [InlineData("dolar 45 lirayı geçince haber ver", "dolar", "45", "ust")]
    [InlineData("euro 50'nin altına düşünce söyle", "euro", "50", "alt")]
    [InlineData("sterlin 60 olunca uyar", "sterlin", "60", "")]
    [InlineData("dolar kuru 42,5 tl'yi aşınca bana haber ver", "dolar", "42.5", "ust")]
    public void SetsRateAlerts(string sentence, string currency, string level, string direction)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("doviz_alarmi", command?.Tool);
        Assert.Equal("ekle", command!.Arguments["islem"]);
        Assert.Equal(currency, command.Arguments["birim"]);
        Assert.Equal(level, command.Arguments["deger"]);
        Assert.Equal(direction, command.Arguments.GetValueOrDefault("yon", ""));
    }

    [Theory]
    [InlineData("döviz alarmlarım", "doviz_alarmi", "listele")]
    [InlineData("kur alarmını iptal et", "doviz_alarmi", "sil")]
    [InlineData("uyku saatim 23:30", "uyku_saati", "ayarla")]
    [InlineData("her gece 23'te yatmak istiyorum", "uyku_saati", "ayarla")]
    [InlineData("uyku hatırlatmasını kapat", "uyku_saati", "kapat")]
    [InlineData("uyku saatim ne", "uyku_saati", "durum")]
    public void ManagesAlertsAndBedTime(string sentence, string tool, string action)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal(tool, command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
    }

    [Theory]
    [InlineData("dolar kaç", "doviz")]
    [InlineData("100 dolar kaç lira", "doviz")]
    public void KeepsCurrencyQuestions(string sentence, string tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("23:30", 23, 30)]
    [InlineData("00.15", 0, 15)]
    [InlineData("24", 0, 0)]
    [InlineData("gece 1", 1, 0)]
    public void ParsesBedTimes(string text, int hour, int minute) => Assert.Equal(new TimeOnly(hour, minute), BedTime.Parse(text));

    [Fact]
    public void CountsTimeUntilBedAcrossMidnight()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), BedTime.Until(new TimeOnly(0, 15), new DateTime(2026, 11, 19, 23, 45, 0)));
        Assert.Equal(TimeSpan.FromMinutes(-40), BedTime.Until(new TimeOnly(23, 30), new DateTime(2026, 11, 20, 0, 10, 0)));
        Assert.Equal(TimeSpan.FromHours(10), BedTime.Until(new TimeOnly(23, 0), new DateTime(2026, 11, 19, 13, 0, 0)));
    }
}

public sealed class AlertToolTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;

    public AlertToolTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")))
        {
            Expenses = new ExpenseBook(Path.Combine(_temp.Path, "expenses.json"))
        };
    }

    [Fact]
    public async Task NudgesBeforeAndAfterBedTime()
    {
        var reply = await _tools.RunQuickAsync(QuickIntents.Match("uyku saatim 23:30")!, CancellationToken.None);
        Assert.Contains("uyku saatin 23:30", reply!.Text, StringComparison.Ordinal);
        Assert.Null(_tools.BedTimeNudge(new DateTime(2026, 11, 19, 22, 30, 0)));
        Assert.StartsWith("Yarım saat sonra uyku saatin.", _tools.BedTimeNudge(new DateTime(2026, 11, 19, 23, 0, 0)), StringComparison.Ordinal);
        Assert.Null(_tools.BedTimeNudge(new DateTime(2026, 11, 19, 23, 1, 0)));
        Assert.StartsWith("Uyku saatini yarım saati geçti", _tools.BedTimeNudge(new DateTime(2026, 11, 20, 0, 5, 0)), StringComparison.Ordinal);
        Assert.Null(_tools.BedTimeNudge(new DateTime(2026, 11, 20, 0, 6, 0)));

        await _tools.RunQuickAsync(QuickIntents.Match("uyku hatırlatmasını kapat")!, CancellationToken.None);
        Assert.Null(_tools.BedTimeNudge(new DateTime(2026, 11, 20, 23, 0, 0)));
    }

    [Fact]
    public async Task ChecksRatesOnlyWhenAlertsExist() => Assert.Null(await _tools.RateAlertCheckAsync(DateTime.Now, CancellationToken.None));

    [Fact]
    public void ComparesWithTheSameDaysLastMonth()
    {
        var book = _tools.Expenses!;
        var now = new DateTime(2026, 11, 19, 18, 0, 0);
        book.Add(500, "markete", new DateTime(2026, 10, 5, 12, 0, 0));
        book.Add(200, "kahveye", new DateTime(2026, 10, 25, 12, 0, 0));
        book.Add(500, "markete", new DateTime(2026, 11, 3, 12, 0, 0));
        book.Add(250, "kahveye", new DateTime(2026, 11, 10, 12, 0, 0));
        var (from, to, _) = ExpenseBook.Period("ay", now);
        Assert.Equal(" Geçen ayın aynı dönemine göre yüzde 50 fazla; en çok yeme-içme arttı.", AssistantTools.MonthComparison(book, book.Between(from, to), now));
    }

    [Fact]
    public async Task ASummaryNamesTheBiggestSingleExpense()
    {
        var book = _tools.Expenses!;
        var now = DateTime.Now;
        book.Add(850, "market", now.AddSeconds(-3));
        book.Add(2100, "market büyük alışveriş", now.AddSeconds(-2));
        book.Add(430, "eczane", now.AddSeconds(-1));
        var reply = await _tools.RunQuickAsync(new QuickCommand("harcama", new Dictionary<string, string> { ["islem"] = "ozet", ["donem"] = "ay" }), CancellationToken.None);
        Assert.Contains("En büyük tek harcama market büyük alışveriş, 2.100 lira", reply!.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ne kadar kaldı")]
    [InlineData("zamanlayıcıda ne kadar kaldı")]
    [InlineData("kaç dakika kaldı")]
    public void AsksHowMuchTimeIsLeft(string sentence) => Assert.Equal("kalan", QuickIntents.Match(sentence)?.Arguments["islem"]);

    [Fact]
    public async Task SaysTheTimeLeftOnRunningTimers()
    {
        var none = await _tools.RunQuickAsync(QuickIntents.Match("ne kadar kaldı")!, CancellationToken.None);
        Assert.Equal("Çalışan bir zamanlayıcı ya da odak yok.", none!.Text);
        await _tools.RunQuickAsync(QuickIntents.Match("10 dakika sonra haber ver")!, CancellationToken.None);
        var left = await _tools.RunQuickAsync(QuickIntents.Match("ne kadar kaldı")!, CancellationToken.None);
        Assert.StartsWith("Zamanlayıcının bitmesine 9 dakika", left!.Text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
