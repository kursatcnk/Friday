using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class ChatStoreTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateTimeOffset Start = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstQuestionStartsATitledChatAndFollowUpsJoinIt()
    {
        using var temp = new TempDirectory();
        var chats = new ChatStore(temp.Path, new Clock(Start));
        Assert.Null(chats.CurrentId);

        chats.Append("Hey Friday, yarın İstanbul'da hava nasıl olacak?", "Yarın güneşli.");
        chats.Append("Peki ya cumartesi?", "Cumartesi yağmurlu.");

        var chat = Assert.Single(chats.List());
        Assert.Equal("Yarın İstanbul'da hava nasıl olacak", chat.Title);
        Assert.Equal(2, chat.TurnCount);
        Assert.Equal(chat.Id, chats.CurrentId);
    }

    [Fact]
    public void GreetingTitleIsReplacedByTheFirstRealQuestion()
    {
        using var temp = new TempDirectory();
        var chats = new ChatStore(temp.Path, new Clock(Start));
        chats.Append("Merhaba", "Merhaba!");
        chats.Append("Sunum için üç başlık önerir misin?", "Sorun, çözüm, yol haritası.");

        Assert.Equal("Sunum için üç başlık önerir misin", Assert.Single(chats.List()).Title);
    }

    [Fact]
    public void NewChatOpenAndDeleteSwitchConversations()
    {
        using var temp = new TempDirectory();
        var clock = new Clock(Start);
        var chats = new ChatStore(temp.Path, clock);
        var switches = 0;
        chats.CurrentChanged += (_, _) => switches++;

        var first = chats.Append("Dolar kaç lira?", "41 lira.");
        chats.NewChat();
        clock.Now += TimeSpan.FromHours(1);
        var second = chats.Append("Not al: süt", "Not aldım.");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal([second.Id, first.Id], chats.List().Select(chat => chat.Id));

        Assert.True(chats.Open(first.Id));
        Assert.Equal("Dolar kaç lira?", Assert.Single(chats.CurrentTurns).User);

        Assert.True(chats.Delete(first.Id));
        Assert.Null(chats.CurrentId);
        Assert.Single(chats.List());
        // Yeni sohbet, açma ve açık sohbetin silinmesi geçiş sayılır; ilk soruyla oluşmak sayılmaz.
        Assert.Equal(3, switches);
    }

    [Fact]
    public void PersistsRenamesAndPinsAcrossInstances()
    {
        using var temp = new TempDirectory();
        var clock = new Clock(Start);
        var chats = new ChatStore(temp.Path, clock);
        var old = chats.Append("Arabamın muayenesi ne zaman?", "20 Kasım'da.");
        chats.NewChat();
        clock.Now += TimeSpan.FromHours(2);
        chats.Append("Saat kaç?", "Saat 12:00.");
        chats.Rename(old.Id, "Araba işleri");
        chats.SetPinned(old.Id, true);

        var reloaded = new ChatStore(temp.Path, clock);
        var top = reloaded.List()[0];
        Assert.Equal("Araba işleri", top.Title);
        Assert.True(top.Pinned);
        Assert.Equal(2, reloaded.Count);
    }

    [Fact]
    public void SearchIgnoresCaseAndTurkishLetters()
    {
        using var temp = new TempDirectory();
        var chats = new ChatStore(temp.Path, new Clock(Start));
        chats.Append("Bu hafta sonu İzmir'de hava nasıl?", "Cumartesi güneşli, pazar yağmurlu.");
        chats.NewChat();
        chats.Append("Dolar kaç?", "41 lira.");

        var hit = Assert.Single(chats.Search("izmir"));
        Assert.StartsWith("Bu hafta sonu", hit.Chat.Title, StringComparison.Ordinal);
        Assert.Contains("yağmurlu", Assert.Single(chats.Search("YAGMUR")).Snippet, StringComparison.Ordinal);
        Assert.Empty(chats.Search("bitcoin"));
    }

    [Fact]
    public void ReadsADayAcrossAllChats()
    {
        using var temp = new TempDirectory();
        var clock = new Clock(Start);
        var chats = new ChatStore(temp.Path, clock);
        chats.Append("Birinci", "1");
        chats.NewChat();
        clock.Now += TimeSpan.FromHours(3);
        chats.Append("İkinci", "2");
        clock.Now += TimeSpan.FromDays(1);
        chats.Append("Ertesi gün", "3");

        Assert.Equal(["Birinci", "İkinci"], chats.Read(DateOnly.FromDateTime(Start.DateTime)).Select(turn => turn.User));
        Assert.Contains("**Sen**", chats.ToMarkdown(chats.CurrentId!, "Friday"), StringComparison.Ordinal);
    }

    [Fact]
    public void ImportsTheOldDailyHistoryOnce()
    {
        using var temp = new TempDirectory();
        var history = Path.Combine(temp.Path, "history");
        var log = new ConversationLog(history, new Clock(Start));
        log.Append("Merhaba", "Merhaba!");
        log.Append("Yarın hava nasıl?", "Güneşli.");

        var chats = new ChatStore(Path.Combine(temp.Path, "chats"), new Clock(Start));
        Assert.Equal(1, chats.ImportLegacy(history));
        Assert.Equal("Yarın hava nasıl", Assert.Single(chats.List()).Title);
        Assert.Equal(0, chats.ImportLegacy(history));
    }

    [Theory]
    [InlineData("Bana bugünün en önemli teknoloji haberlerini kısaca anlatır mısın lütfen?", "Bana bugünün en önemli teknoloji haberlerini…")]
    [InlineData("friday, saat kaç", "Saat kaç")]
    [InlineData("   ", "Yeni sohbet")]
    public void TitlesAreShortAndClean(string text, string title) => Assert.Equal(title, ChatStore.TitleFrom(text));
}

public class ConversationReloadTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void BeginTurnReportsAnExpiredConversation()
    {
        var clock = new ManualClock();
        var memory = new ConversationMemory(TimeSpan.FromMinutes(30), clock);
        Assert.False(memory.BeginTurn());
        clock.Now += TimeSpan.FromMinutes(31);
        Assert.True(memory.BeginTurn());
    }

    [Fact]
    public void LoadingAnOldChatRestoresItsLastTurns()
    {
        var memory = new ConversationMemory(TimeSpan.FromMinutes(30));
        var turns = Enumerable.Range(1, 9).Select(index => new ConversationTurn(DateTimeOffset.Now, $"soru {index}", $"cevap {index}")).ToList();
        memory.Load(turns);
        Assert.Equal(12, memory.Messages.Count);
        Assert.Equal("soru 4", memory.Messages[0].Content);
        Assert.Equal(ChatRole.Assistant, memory.Messages[^1].Role);
    }
}

public class ChatIntentTests
{
    [Theory]
    [InlineData("Yeni sohbet", "yeni")]
    [InlineData("yeni bir sohbet başlat", "yeni")]
    [InlineData("Hey Friday, konuyu değiştirelim", "yeni")]
    [InlineData("baştan başlayalım", "yeni")]
    [InlineData("sohbeti temizle", "temizle")]
    [InlineData("bu konuşmayı sil", "temizle")]
    public void RecognisesChatCommands(string sentence, string action)
    {
        var command = QuickIntents.Match(sentence);
        Assert.NotNull(command);
        Assert.Equal("sohbet", command.Tool);
        Assert.Equal(action, command.Arguments["islem"]);
    }

    [Theory]
    [InlineData("moladan döndüm")]
    [InlineData("molayı bitir")]
    [InlineData("geri döndüm")]
    public void RecognisesEndOfBreak(string sentence)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("ritim", command?.Tool);
        Assert.Equal("mola_bitir", command!.Arguments["islem"]);
    }

    [Theory]
    [InlineData("çalışma modu rutinini çalıştır", "çalışma modu")]
    [InlineData("sabah rutinini başlat", "sabah")]
    [InlineData("oyun modu rutini aç", "oyun modu")]
    public void RunsRoutinesByName(string sentence, string routine)
    {
        var command = QuickIntents.Match(sentence, ["çalışma modu", "sabah", "oyun modu"]);
        Assert.Equal("rutin", command?.Tool);
        Assert.Equal(routine, command!.Arguments["ad"]);
    }
}

public class RhythmDisplayTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 14, 0, 0);

    private static RhythmSnapshot State(DateTime? stretch = null, DateTime? focusStart = null, DateTime? focusEnd = null, DateTime? breakStart = null,
        bool enabled = true, bool away = false, DateTime? snoozed = null) =>
        new(enabled, stretch, focusStart, focusEnd, breakStart, TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5), away, snoozed);

    [Fact]
    public void WorkingShowsTheStretchAndTimeToTheBreak()
    {
        var display = RhythmText.Describe(State(stretch: Now.AddMinutes(-32).AddSeconds(-10)), Now);
        Assert.Equal(RhythmMode.Working, display.Mode);
        Assert.Equal("32:10", display.Clock);
        Assert.Contains("Molaya 18 dakika var", display.Sentence, StringComparison.Ordinal);
        Assert.InRange(display.Progress, 0.64, 0.65);
    }

    [Fact]
    public void LongStretchIsOverdue()
    {
        var display = RhythmText.Describe(State(stretch: Now.AddMinutes(-75)), Now);
        Assert.Equal(RhythmMode.Overdue, display.Mode);
        Assert.Equal("1:15:00", display.Clock);
        Assert.Equal(1, display.Progress);
    }

    [Fact]
    public void FocusCountsDownToItsEnd()
    {
        var display = RhythmText.Describe(State(stretch: Now.AddMinutes(-40), focusStart: Now.AddMinutes(-13), focusEnd: Now.AddMinutes(12)), Now);
        Assert.Equal(RhythmMode.Focus, display.Mode);
        Assert.Equal("12:00", display.Clock);
        Assert.Contains("14:12'de bitiyor", display.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void BreakCountsDownAndThenInvitesBack()
    {
        Assert.Equal("02:30", RhythmText.Describe(State(breakStart: Now.AddMinutes(-2.5)), Now).Clock);
        var over = RhythmText.Describe(State(breakStart: Now.AddMinutes(-7)), Now);
        Assert.Equal("Mola bitti", over.Label);
        Assert.Contains("moladan döndüm", over.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void OffAwayAndReadyAreExplained()
    {
        Assert.Equal(RhythmMode.Off, RhythmText.Describe(State(enabled: false), Now).Mode);
        Assert.Equal(RhythmMode.Away, RhythmText.Describe(State(away: true), Now).Mode);
        Assert.Equal(RhythmMode.Ready, RhythmText.Describe(State(), Now).Mode);
    }

    [Theory]
    [InlineData(9, 30, "da")]
    [InlineData(15, 0, "te")]
    [InlineData(10, 40, "ta")]
    [InlineData(19, 17, "de")]
    [InlineData(12, 0, "de")]
    [InlineData(10, 0, "da")]
    [InlineData(8, 46, "da")]
    public void TimeSuffixFollowsTheSpokenNumber(int hour, int minute, string suffix) =>
        Assert.Equal(suffix, RhythmText.TimeSuffix(new DateTime(2026, 11, 17, hour, minute, 0)));

    [Fact]
    public void EndingABreakRestartsTheStretch()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        tracker.StartBreak(Now);
        Assert.Equal(TimeSpan.FromMinutes(2), tracker.EndBreak(Now.AddMinutes(2)));
        Assert.Null(tracker.BreakStart);
        Assert.Equal(Now.AddMinutes(2), tracker.StretchStart);
        Assert.Equal(TimeSpan.Zero, tracker.EndBreak(Now.AddMinutes(3)));
    }
}

public class RoutineEditorTests
{
    [Theory]
    [InlineData("ses:30", RoutineAction.Volume, 30)]
    [InlineData("bekle:3", RoutineAction.Wait, 3)]
    [InlineData("ses: 45%", RoutineAction.Volume, 45)]
    public void ParsesNumberSteps(string text, RoutineAction action, int number)
    {
        var step = RoutineStep.Parse(text);
        Assert.Equal(action, step.Action);
        Assert.Equal(number, step.Number);
    }

    [Theory]
    [InlineData("ses:yüksek")]
    [InlineData("bekle:")]
    [InlineData("hatirlat:yarın")]
    [InlineData("hatirlat:|çay")]
    public void RejectsInvalidSteps(string text) => Assert.Throws<FormatException>(() => RoutineStep.Parse(text));

    [Fact]
    public void ReminderStepKeepsTimeAndText()
    {
        var step = RoutineStep.Parse("hatırlat:5 dakika sonra|çay demlendi");
        Assert.Equal(RoutineAction.Remind, step.Action);
        Assert.Equal("5 dakika sonra \"çay demlendi\" hatırlat", step.Describe());
        Assert.Equal("hatirlat:5 dakika sonra|çay demlendi", step.Format());
    }

    [Fact]
    public void EveryTemplateRoundTrips()
    {
        foreach (var template in RoutineStep.Templates)
        {
            foreach (var text in template.Steps)
            {
                var step = RoutineStep.Parse(text);
                Assert.Equal(step, RoutineStep.Parse(step.Format()));
                Assert.False(string.IsNullOrWhiteSpace(step.Describe()));
            }
        }
    }

    [Fact]
    public void EveryStepKindHasAUniqueKey() =>
        Assert.Equal(RoutineStep.Kinds.Count, RoutineStep.Kinds.Select(kind => kind.Key).Distinct().Count());

    [Theory]
    [InlineData("ritim:odak 25", "25 dakikalık odak başlat")]
    [InlineData("ritim:ertele 120", "Mola önerisini 120 dakika ertele")]
    [InlineData("medya:oynat_duraklat", "Müziği oynat ya da duraklat")]
    [InlineData("ses:30", "Sesi %30 yap")]
    public void DescribesStepsInPlainTurkish(string text, string description) =>
        Assert.Equal(description, RoutineStep.Parse(text).Describe());
}

public class SettingsMigrationTests
{
    [Fact]
    public void VersionThreeLengthensTheOldChatTimeout()
    {
        var settings = new FridaySettings { SettingsVersion = 2, ConversationMemoryMinutes = 10 };
        JsonSettingsStore.Migrate(settings);
        Assert.Equal(30, settings.ConversationMemoryMinutes);
        Assert.Equal(FridaySettings.CurrentVersion, settings.SettingsVersion);
    }

    [Fact]
    public void DeletedRoutinesStayDeletedAfterAnUpgrade()
    {
        var settings = new FridaySettings { SettingsVersion = 2, ConversationMemoryMinutes = 20 };
        settings.Routines.Remove("toplantı modu");
        JsonSettingsStore.Migrate(settings);
        Assert.DoesNotContain("toplantı modu", settings.Routines.Keys);
        Assert.Equal(20, settings.ConversationMemoryMinutes);
    }

    [Fact]
    public void TheBrainSwitchPicksTheRightSourceModel()
    {
        var settings = new FridaySettings();
        Assert.Equal(FridaySettings.FastTextModel, settings.OllamaModel);
        Assert.Equal("gemma4:e4b", settings.TextModelSource());

        settings.OllamaModel = FridaySettings.SmartTextModel;
        Assert.Equal("gemma4:12b", settings.TextModelSource());
        Assert.Equal("gemma4:e4b", settings.VisionModel);
    }
}

public class UpdateCheckTests
{
    [Theory]
    [InlineData("v0.3.0", true)]
    [InlineData("v0.2.0", false)]
    [InlineData("0.1.5", false)]
    public void ComparesWithTheRunningVersion(string tag, bool available) =>
        Assert.Equal(available, UpdateCheck.Compare(new Version(0, 2, 0, 0), tag, "https://github.com/kursatcnk/Friday/releases").Available);

    [Fact]
    public void ToleratesAMissingRelease() => Assert.False(UpdateCheck.Compare(new Version(0, 2, 0), "", null).Available);
}
