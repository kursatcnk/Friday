using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class DayPlannerTests
{
    private static readonly DateTime Morning = new(2026, 11, 19, 9, 2, 0);

    [Fact]
    public void FitsTasksAroundEventsAndMeals()
    {
        var plan = DayPlanner.Plan(Morning,
            [new FixedItem(Morning.Date.AddHours(10), Morning.Date.AddHours(11), "Ekip toplantısı")],
            ["Raporu bitir", "Faturayı öde", "Sunumu hazırla"], ["Kitap okumak"]);

        Assert.Equal(new DateTime(2026, 11, 19, 9, 5, 0), plan[0].Start);
        Assert.Equal(("Raporu bitir", PlanKind.Focus), (plan[0].Title, plan[0].Kind));
        Assert.Equal(PlanKind.Break, plan[1].Kind);

        // İkinci iş toplantıya sığmadığı için toplantıdan sonra başlar.
        var meeting = plan.Single(block => block.Kind == PlanKind.Event);
        var second = plan.Single(block => block.Title == "Faturayı öde");
        Assert.Equal(meeting.End, second.Start);
        Assert.Contains(plan, block => block.Kind == PlanKind.Meal && block.Title == "Öğle yemeği" && block.Start.Hour == 12);
        Assert.Contains(plan, block => block.Kind == PlanKind.Habit && block.Start >= Morning.Date.AddHours(17));

        // Bloklar çakışmaz ve sıralıdır; son blok mola değildir.
        for (var index = 1; index < plan.Count; index++)
        {
            Assert.True(plan[index - 1].End <= plan[index].Start, $"{plan[index - 1].Title} / {plan[index].Title}");
        }

        Assert.NotEqual(PlanKind.Break, plan[^1].Kind);
        Assert.StartsWith("09:05'te raporu bitir için odak, 10:00'da ekip toplantısı", DayPlanner.Describe(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnsNothingLateAtNight() =>
        Assert.Empty(DayPlanner.Plan(new DateTime(2026, 11, 19, 21, 50, 0), [], ["Raporu bitir"], []));
}

public class NetworkDiagnosisTests
{
    [Theory]
    [InlineData("", null, null, 1.0, null, "hiçbir ağa bağlı")]
    [InlineData("Wi-Fi", null, null, 1.0, null, "modeme ulaşamıyorum")]
    [InlineData("Wi-Fi", 3.0, null, 1.0, null, "servis sağlayıcıda")]
    [InlineData("Wi-Fi", 3.0, 20.0, 0.0, null, "DNS sorunu")]
    [InlineData("kablolu ağ", 1.0, 18.0, 0.0, 22.0, "Bağlantı çok iyi (kablolu ağ): internete 18 ms, modeme 1 ms, ad çözme 22 ms.")]
    [InlineData("Wi-Fi", 45.0, 95.0, 0.0, 30.0, "Wi-Fi sinyali zayıf")]
    [InlineData("Wi-Fi", 5.0, 60.0, 0.4, 30.0, "Paket kaybı var")]
    public void ExplainsWhereTheProblemIs(string adapter, double? gateway, double? internet, double loss, double? dns, string expected) =>
        Assert.Contains(expected, NetworkDiagnosis.Describe(new NetworkProbe(adapter, gateway, internet, loss, dns)), StringComparison.Ordinal);
}

public class PlannerAndBillIntentTests
{
    [Theory]
    [InlineData("günümü planla", "gun_plani")]
    [InlineData("bugün için plan yap", "gun_plani")]
    [InlineData("internetim yavaş mı", "baglanti")]
    [InlineData("bağlantımı kontrol et", "baglanti")]
    [InlineData("internet testi yap", "baglanti")]
    public void RecognizesPlansAndNetworkChecks(string sentence, string tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("faturayı ödemeye 25 dakika odaklan", "faturayı ödeme", "25")]
    [InlineData("sunuma odaklanalım", "sunum", "")]
    [InlineData("rapor için 1 saat odak aç", "rapor", "60")]
    public void FocusesOnATask(string sentence, string task, string minutes)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("isle_odak", command?.Tool);
        Assert.Equal(task, command!.Arguments["is"]);
        Assert.Equal(minutes, command.Arguments.GetValueOrDefault("dakika", ""));
    }

    [Theory]
    [InlineData("25 dakika odaklanacağım")]
    [InlineData("elli dakika odaklanacağım")]
    [InlineData("odak modunu aç")]
    public void KeepsPlainFocus(string sentence) => Assert.Equal("ritim", QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("kira her ayın 5'inde 15 bin lira", "kira", "15000", "5")]
    [InlineData("netflix aboneliğim ayda 230 lira, her ayın 12'si", "netflix", "230", "12")]
    [InlineData("internet faturam aylık 450 tl her ayın 20'sinde", "internet", "450", "20")]
    public void AddsBills(string sentence, string name, string amount, string day)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("sabit_gider", command?.Tool);
        Assert.Equal("ekle", command!.Arguments["islem"]);
        Assert.Equal(name, command.Arguments["ad"]);
        Assert.Equal(amount, command.Arguments["tutar"]);
        Assert.Equal(day, command.Arguments["gun"]);
    }

    [Theory]
    [InlineData("sabit giderlerim", "listele")]
    [InlineData("netflix aboneliğini sil", "sil")]
    [InlineData("kirayı ödedim", "odendi")]
    public void ManagesBills(string sentence, string action) => Assert.Equal(action, QuickIntents.Match(sentence)?.Arguments["islem"]);

    [Theory]
    [InlineData("youtube'a günde 1 saat sınır koy", "ekle", "youtube", "60")]
    [InlineData("instagram için günlük 30 dakika limit", "ekle", "instagram", "30")]
    [InlineData("youtube sınırını kaldır", "sil", "youtube", "")]
    public void SetsAppLimits(string sentence, string action, string app, string minutes)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("sure_siniri", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(app, command.Arguments["ad"]);
        Assert.Equal(minutes, command.Arguments.GetValueOrDefault("dakika", ""));
    }
}

public sealed class PlannerAndBillToolTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly NoteStore _notes;
    private readonly AssistantTools _tools;

    public PlannerAndBillToolTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _notes = new NoteStore(Path.Combine(_temp.Path, "notes.md"));
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), _notes)
        {
            Expenses = new ExpenseBook(Path.Combine(_temp.Path, "expenses.json")),
            Todos = new TodoStore(Path.Combine(_temp.Path, "todos.json"))
        };
    }

    private Task<QuickReply?> Say(string sentence) => _tools.RunQuickAsync(QuickIntents.Match(sentence)!, CancellationToken.None);

    [Fact]
    public async Task TracksBillsAndLogsPayments()
    {
        Assert.Contains("her ayın 5'i 15.000 lira", (await Say("kira her ayın 5'inde 15 bin lira"))!.Text, StringComparison.Ordinal);
        await Say("netflix aboneliğim ayda 230 lira, her ayın 12'si");
        var list = await Say("sabit giderlerim");
        Assert.Contains("ayda toplam 15.230 lira", list!.Text, StringComparison.Ordinal);

        var paid = await Say("kirayı ödedim");
        Assert.Equal("Kira ödendi; 15.000 lira harcamalara işlendi.", paid!.Text);
        Assert.Contains("zaten", (await Say("kirayı ödedim"))!.Text, StringComparison.Ordinal);
        var expense = Assert.Single(_tools.Expenses!.Items);
        Assert.Equal(("Kira", 15000m), (expense.Category, expense.Amount));

        // Sabit gider değilse yapılacaklardaki iş tamamlanır; o da yoksa dil modeline kalır.
        _tools.Todos!.Add("Faturayı öde", DateTime.Now);
        Assert.Contains("tamamlandı", (await Say("faturayı ödedim"))!.Text, StringComparison.Ordinal);
        Assert.Null(await Say("vergiyi ödedim"));
        Assert.Contains("çıkarıldı", (await Say("netflix aboneliğini sil"))!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsksForNotesAfterLongCalls()
    {
        var end = DateTime.Now;
        Assert.Null(_tools.MeetingNoteQuestion("Teams", end.AddMinutes(-5), end));
        Assert.StartsWith("Teams görüşmesi bitti.", _tools.MeetingNoteQuestion("Teams", end.AddMinutes(-40), end), StringComparison.Ordinal);
        var reply = await _tools.RunQuickAsync(new QuickCommand("serbest_cevap", new Dictionary<string, string> { ["metin"] = "Cuma günü demo var, Ayşe raporu gönderecek." }), CancellationToken.None);
        Assert.Equal("Notlarına ekledim.", reply!.Text);
        Assert.Contains(_notes.All, note => note.Text.Contains("Teams görüşmesi", StringComparison.Ordinal) && note.Text.Contains("Ayşe raporu gönderecek", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlansTheDayFromTodos()
    {
        _tools.Todos!.Add("Raporu bitir", DateTime.Now);
        var reply = await Say("günümü planla");
        if (DateTime.Now.Hour < 21)
        {
            Assert.StartsWith("Günün planı:", reply!.Text, StringComparison.Ordinal);
            Assert.Equal("Bugünün planı", reply.Card?.Title);
            Assert.True(_tools.HasPendingConfirmation);
        }
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
