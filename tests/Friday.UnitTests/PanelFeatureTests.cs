using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

/// <summary>Ajanda ekleme kutusu ve "Öğrendikleri" sayfasının arkasındaki mantık.</summary>
public sealed class PanelFeatureTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly ReminderStore _reminders;
    private readonly TodoStore _todos;
    private readonly AssistantTools _tools;

    public PanelFeatureTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _reminders = new ReminderStore(Path.Combine(_temp.Path, "reminders.json"));
        _todos = new TodoStore(Path.Combine(_temp.Path, "todos.json"));
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)), new MemoryStore(Path.Combine(_temp.Path, "memory.json")),
            new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")), _reminders, new NoteStore(Path.Combine(_temp.Path, "notes.md")))
        {
            Todos = _todos,
            Corrections = new CorrectionBook(Path.Combine(_temp.Path, "corrections.json")),
            Shortcuts = new LearnedShortcuts(Path.Combine(_temp.Path, "shortcuts.json")),
            Nudges = new NudgePolicy(Path.Combine(_temp.Path, "nudges.json")),
            Style = new ReplyStyle(Path.Combine(_temp.Path, "reply-style.json")),
            WritingStyle = new WritingStyle(Path.Combine(_temp.Path, "writing-style.json")),
        };
    }

    [Fact]
    public async Task ATimeMakesAReminder()
    {
        var reply = await _tools.AddToAgendaAsync("yarın 9'da dişçi", CancellationToken.None);
        var reminder = Assert.Single(_reminders.Upcoming);
        Assert.Equal(DateTime.Today.AddDays(1).AddHours(9), reminder.Due);
        Assert.Contains("dişçi", reminder.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("09:00", reply, StringComparison.Ordinal);
        Assert.Empty(_todos.Open);
    }

    [Fact]
    public async Task ADayWithoutTimeMakesATaskWithADeadline()
    {
        await _tools.AddToAgendaAsync("cuma sunumu bitir", CancellationToken.None);
        var todo = Assert.Single(_todos.Open);
        Assert.Equal(DayOfWeek.Friday, todo.Due!.Value.DayOfWeek);
        Assert.Empty(_reminders.Upcoming);
    }

    [Fact]
    public async Task PlainTextIsATask()
    {
        var reply = await _tools.AddToAgendaAsync("faturayı öde", CancellationToken.None);
        Assert.Equal("Faturayı öde", Assert.Single(_todos.Open).Text);
        Assert.Contains("yapılacaklara eklendi", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void LearnedThingsAreListedAndForgotten()
    {
        _tools.Corrections!.Add("ekranı büyüt");
        _tools.Nudges!.Silence("goz");
        _tools.Style!.Prefer(Verbosity.Brief);
        _tools.WritingStyle!.Learn("resmi");
        _tools.WritingStyle.Learn("resmi");
        _tools.Settings.Pronunciations["GPU"] = "ce pe u";

        var groups = _tools.Learned(DateTime.Now).ToDictionary(group => group.Key);
        Assert.Single(groups["duzeltme"].Items);
        Assert.Single(groups["susturulan"].Items);
        Assert.Equal(2, groups["uslup"].Items.Count);
        Assert.Equal("GPU → ce pe u", Assert.Single(groups["okunus"].Items).Text);

        foreach (var (group, items) in groups)
        {
            foreach (var item in items.Items)
            {
                Assert.DoesNotContain("zaten", _tools.ForgetLearned(group, item.Key), StringComparison.Ordinal);
            }
        }

        Assert.All(_tools.Learned(DateTime.Now), group => Assert.Empty(group.Items));
        Assert.Equal(Verbosity.Normal, _tools.Style.Current);
        Assert.True(_tools.Nudges.Allowed("goz", DateTime.Now));
    }

    [Fact]
    public void AgendaItemsCarryIdsForTheirButtons()
    {
        var now = DateTime.Today.AddHours(8);
        var reminder = _reminders.Add(now.AddHours(2), "Mert'i ara", now);
        var todo = _todos.Add("Kirayı öde", now, DateOnly.FromDateTime(now));
        var agenda = DayAgenda.Build(DateOnly.FromDateTime(now), DateOnly.FromDateTime(now).AddDays(1), now, [], _reminders.Upcoming, _todos.Open, [], []);
        Assert.Contains(agenda, item => item.Kind == AgendaKind.Reminder && item.Id == reminder.Id);
        Assert.Contains(agenda, item => item.Kind == AgendaKind.Deadline && item.Id == todo.Id);
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}

/// <summary>Zincirleme hesap: önceki sonucun üzerine yeni işlem.</summary>
public class ChainedMathTests
{
    [Theory]
    [InlineData("bunu ikiye böl", "4.858")]
    [InlineData("sonucu 3 ile çarp", "29.148")]
    [InlineData("100 ekle", "9.816")]
    [InlineData("bundan 716 çıkar", "9.000")]
    [InlineData("yarısı", "4.858")]
    [InlineData("yüzde 10'u", "971,6")]
    [InlineData("dörde böl", "2.429")]
    public void ANewOperationWorksOnThePreviousResult(string said, string expected)
    {
        var next = SpokenMath.Continue(said, "347 çarpı 28 kaç");
        Assert.NotNull(next);
        Assert.Contains(expected, SpokenMath.Answer(next!), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bugün hava nasıl")]
    [InlineData("ikiye böl ama önce bana annemin doğum gününü söyle")]
    public void OtherSentencesAreNotChained(string said) => Assert.Null(SpokenMath.Continue(said, "347 çarpı 28 kaç"));

    [Fact]
    public void TheFollowUpGoesThroughTheQuickPath()
    {
        var last = QuickIntents.Match("347 çarpı 28 kaç")!;
        var follow = FollowUp.Resolve("bunu ikiye böl", "347 çarpı 28 kaç", last, text => QuickIntents.Match(text));
        Assert.Equal("hesap", follow?.Command.Tool);
        Assert.Contains("4.858", SpokenMath.Answer(follow!.Value.Text), StringComparison.Ordinal);
    }
}

/// <summary>Sayfa okuma ve çok adımlı istek ayrımı; sıra sözcükleri liste adı olmaz.</summary>
public class ReadingRequestTests
{
    [Theory]
    [InlineData("haberler.com.tr'deki son ekonomi haberini oku ve kısaca özetle", "haberler.com.tr")]
    [InlineData("github.com/dotnet/runtime sayfasında ne yazıyor", "github.com/dotnet/runtime")]
    [InlineData("https://tr.wikipedia.org/wiki/Ankara oku", "https://tr.wikipedia.org/wiki/ankara")]
    public void ANamedPageIsReadFirst(string question, string address) => Assert.Equal(address, AgentPlan.PageToRead(question));

    [Theory]
    [InlineData("youtube.com'u aç")]
    [InlineData("bugün hava nasıl")]
    [InlineData("bu sayfayı özetle")]
    public void OtherRequestsDoNotPreRead(string question) => Assert.Null(AgentPlan.PageToRead(question));

    [Theory]
    [InlineData("bu makaleyi oku ve özetle", false)]
    [InlineData("PDF'i bul ve göster", false)]
    [InlineData("raporu bul, özetle ve notlarıma ekle", true)]
    [InlineData("önce sesi kıs sonra müziği aç", true)]
    public void ReadingVerbsAloneAreOneStep(string question, bool multi) => Assert.Equal(multi, AgentPlan.IsMultiStep(question));

    [Fact]
    public void ASequenceWordIsNotAListName()
    {
        var command = QuickIntents.Match("önce listeme süt ekle");
        Assert.Equal("market", command?.Arguments["ad"]);
        Assert.Equal("süt", command!.Arguments["oge"]);
    }

    [Theory]
    [InlineData("I'm sorry, but I don't have the ability to browse websites.")]
    [InlineData("Maalesef web sitelerine erişip içerik okuma yetkim bulunmuyor.")]
    public void RefusalsInEitherLanguageAreNoticed(string sentence) => Assert.True(AgentPlan.SoundsUnable(sentence));
}
