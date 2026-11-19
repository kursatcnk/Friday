using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class MeetingSignalTests
{
    private const string Own = @"E:\Github\Friday\bin\Friday.exe";

    [Fact]
    public void FindsTheAppUsingTheMicrophone()
    {
        DeviceUse[] uses =
        [
            new(@"E:#Github#Friday#bin#Friday.exe", 100, 0),
            new(@"C:#Program Files#Zoom#bin#Zoom.exe", 50, 60),
            new("MSTeams_8wekyb3d8bbwe", 90, 0),
            new(@"C:#Program Files#Google#Chrome#Application#chrome.exe", 80, 0)
        ];
        Assert.Equal("Teams", MeetingSignals.ActiveApp(uses, Own));
        Assert.Null(MeetingSignals.ActiveApp(uses.Where(use => use.Key.Contains("Friday", StringComparison.Ordinal) || use.Key.Contains("Zoom", StringComparison.Ordinal)), Own));
        // Friday'in başka bir kopyası (ör. yayın klasöründen) de sayılmaz.
        Assert.Null(MeetingSignals.ActiveApp([new(@"D:#Apps#Friday#Friday.exe", 10, 0)], Own));
    }

    [Theory]
    [InlineData(@"C:#Program Files#Zoom#bin#Zoom.exe", "Zoom")]
    [InlineData("MSTeams_8wekyb3d8bbwe", "Teams")]
    [InlineData(@"C:#Users#k#AppData#Local#Discord#app-1.0#Discord.exe", "Discord")]
    [InlineData(@"C:#Program Files (x86)#Microsoft#Edge#Application#msedge.exe", "Edge")]
    [InlineData("Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", "Ses Kaydedici")]
    [InlineData(@"C:#Tools#Recorder#MyRec.exe", "MyRec")]
    public void NamesAppsReadably(string key, string name) => Assert.Equal(name, MeetingSignals.FriendlyName(key));
}

public class SpecialDayTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    private static MemoryItem Item(string text) => new(Guid.NewGuid().ToString("N"), text, DateTimeOffset.Now);

    [Theory]
    [InlineData("Kullanıcının annesinin doğum günü 3 Mart'ta.", "annenin doğum günü", SpecialKind.Birthday)]
    [InlineData("Kullanıcının babasının doğum günü 20 Kasım.", "babanın doğum günü", SpecialKind.Birthday)]
    [InlineData("Kullanıcının kızının doğum günü 5 Ocak.", "kızının doğum günü", SpecialKind.Birthday)]
    [InlineData("Kullanıcının doğum günü 19 Kasım.", "doğum günü", SpecialKind.OwnBirthday)]
    [InlineData("Kullanıcının evlilik yıldönümü 12 Haziran'da.", "evlilik yıldönümü", SpecialKind.Anniversary)]
    [InlineData("Kullanıcının arabasının muayenesi 25 Kasım'da.", "arabanın muayenesi", SpecialKind.Other)]
    public void ReadsTitlesAndKinds(string text, string title, SpecialKind kind)
    {
        Assert.Equal(title, SpecialDays.Title(text));
        Assert.Equal(kind, SpecialDays.KindOf(text));
    }

    [Fact]
    public void ListsUpcomingDaysWithHolidays()
    {
        var days = SpecialDays.Upcoming([Item("Kullanıcının babasının doğum günü 20 Kasım."), Item("Kullanıcının annesinin doğum günü 3 Mart.")], Today, 30);
        Assert.Equal(["babanın doğum günü", "Öğretmenler Günü"], days.Select(day => day.Title));
        Assert.Equal("yarın", SpecialDays.When(days[0].Date, Today));
        Assert.Equal("5 gün sonra", SpecialDays.When(days[1].Date, Today));
        Assert.Contains(SpecialDays.Upcoming([], new DateOnly(2026, 12, 30), 3), day => day.Title == "Yılbaşı" && day.Date == new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void GreetsOnTheDay()
    {
        Assert.Equal("İyi ki doğdun Kürşat! Yeni yaşın sağlık, mutluluk ve bol güzel anla dolsun.",
            SpecialDays.Greeting(new SpecialDay(Today, "doğum günü", SpecialKind.OwnBirthday), "Kürşat"));
        Assert.Equal("Bugün annenin doğum günü! Aramayı ya da bir mesaj atmayı unutma.",
            SpecialDays.Greeting(new SpecialDay(Today, "annenin doğum günü", SpecialKind.Birthday), ""));
        Assert.Equal("Bugün Cumhuriyet Bayramı. Cumhuriyet Bayramın kutlu olsun!",
            SpecialDays.Greeting(new SpecialDay(Today, "Cumhuriyet Bayramı", SpecialKind.Holiday), ""));
        Assert.Null(SpecialDays.Greeting(new SpecialDay(Today, "arabanın muayenesi", SpecialKind.Other), ""));
    }

    [Theory]
    [InlineData("yaklaşan özel günler")]
    [InlineData("önemli günlerim neler")]
    [InlineData("kimin doğum günü yaklaşıyor")]
    public void AsksForSpecialDays(string sentence) => Assert.Equal("ozel_gunler", QuickIntents.Match(sentence)?.Tool);
}

public sealed class SpecialDayToolTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly MemoryStore _memory;
    private readonly ReminderStore _reminders;
    private readonly AssistantTools _tools;

    public SpecialDayToolTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _memory = new MemoryStore(Path.Combine(_temp.Path, "memory.json"));
        _reminders = new ReminderStore(Path.Combine(_temp.Path, "reminders.json"));
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)), _memory, new PrivacyLedger(_temp.Path),
            new ChatStore(Path.Combine(_temp.Path, "chats")), _reminders, new NoteStore(Path.Combine(_temp.Path, "notes.md")));
    }

    [Fact]
    public async Task AsksTheEveningBeforeAndSetsAReminder()
    {
        var now = DateTime.Now;
        var tomorrow = now.Date.AddDays(1);
        _memory.Remember($"Kullanıcının annesinin doğum günü {tomorrow.Day} {tomorrow.ToString("MMMM", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))}.");

        var question = _tools.EveReminder(now);
        Assert.Equal("Yarın annenin doğum günü. Hediye ya da bir mesaj hazır mı? İstersen yarın sabah 10'da hatırlatayım.", question);
        Assert.Null(_tools.EveReminder(now));

        var reply = await _tools.RunQuickAsync(new QuickCommand("onay", new Dictionary<string, string> { ["cevap"] = "evet" }), CancellationToken.None);
        Assert.Contains("10:00", reply!.Text, StringComparison.Ordinal);
        var reminder = Assert.Single(_reminders.Upcoming);
        Assert.Equal(tomorrow.AddHours(10), reminder.Due);
    }

    [Fact]
    public async Task ListsSpecialDays()
    {
        var reply = await _tools.RunQuickAsync(QuickIntents.Match("yaklaşan özel günler")!, CancellationToken.None);
        Assert.Equal("Önemli günler", reply!.Card?.Title);
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
