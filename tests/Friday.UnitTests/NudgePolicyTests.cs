using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class NudgePolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 21, 0, 0);

    [Fact]
    public void BacksOffAfterThreeDeclinesInARow()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "nudges.json");
        var policy = new NudgePolicy(file);
        Assert.False(policy.Respond("gun_sonu", false, Now));
        Assert.False(policy.Respond("gun_sonu", false, Now));
        // Bir "evet" sayacı sıfırlar.
        Assert.False(policy.Respond("gun_sonu", true, Now));
        Assert.False(policy.Respond("gun_sonu", false, Now));
        Assert.False(policy.Respond("gun_sonu", false, Now));
        Assert.True(policy.Respond("gun_sonu", false, Now));

        var reloaded = new NudgePolicy(file);
        Assert.False(reloaded.Allowed("gun_sonu", Now.AddDays(6)));
        Assert.True(reloaded.Allowed("gun_sonu", Now.AddDays(7)));
        Assert.True(reloaded.Allowed("seri", Now));
    }

    [Fact]
    public void SilencesForGoodAndResets()
    {
        using var temp = new TempDirectory();
        var policy = new NudgePolicy(Path.Combine(temp.Path, "nudges.json"));
        policy.Silence("goz");
        Assert.False(policy.Allowed("goz", Now.AddYears(5)));
        Assert.Equal([("goz", (DateTime?)null)], policy.Silenced(Now));
        Assert.Equal(1, policy.Reset());
        Assert.True(policy.Allowed("goz", Now));
    }

    [Theory]
    [InlineData("bunu bir daha sorma", "sustur")]
    [InlineData("artık söyleme", "sustur")]
    [InlineData("bu hatırlatmayı kapat", "sustur")]
    [InlineData("kapattığın hatırlatmaları aç", "ac")]
    [InlineData("susturduğun şeyler neler", "listele")]
    public void RecognizesVoiceControl(string sentence, string action)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("durtme", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
    }

    [Fact]
    public void KeepsRepeatRequests() => Assert.Equal("tekrar", QuickIntents.Match("bir daha söyle")?.Tool);

    [Fact]
    public async Task LearnsFromAnswersAndSilencesTheLastNudge()
    {
        using var temp = new TempDirectory();
        using var http = new HttpClient();
        var paths = new FridayPaths(temp.Path);
        using var tools = new AssistantTools(http, new OllamaChatClient(http, new OllamaServer(paths, http)),
            new MemoryStore(Path.Combine(temp.Path, "memory.json")), new PrivacyLedger(temp.Path), new ChatStore(Path.Combine(temp.Path, "chats")),
            new ReminderStore(Path.Combine(temp.Path, "reminders.json")), new NoteStore(Path.Combine(temp.Path, "notes.md")))
        {
            Journal = new JournalStore(Path.Combine(temp.Path, "journal")),
            Nudges = new NudgePolicy(Path.Combine(temp.Path, "nudges.json"))
        };

        Task<QuickReply?> Answer(string text) =>
            tools.RunQuickAsync(new QuickCommand("serbest_cevap", new Dictionary<string, string> { ["metin"] = text }), CancellationToken.None);

        // Gün sonu sorusuna üç gün üst üste "şimdi değil": tür bir hafta susar.
        for (var day = 0; day < 3; day++)
        {
            Assert.NotNull(tools.JournalQuestion(DateTime.Now.AddDays(day)));
            var reply = await Answer("şimdi değil");
            Assert.Equal(day == 2, reply!.Text.Contains("bir hafta söylemeyeceğim", StringComparison.Ordinal));
        }

        Assert.False(tools.AllowsNudge("gun_sonu"));

        // "Bunu bir daha söyleme" son kendiliğinden söylenen türe uygulanır.
        tools.MarkNudge("goz");
        var silenced = await tools.RunQuickAsync(QuickIntents.Match("bunu bir daha söyleme")!, CancellationToken.None);
        Assert.Contains("göz molası artık söylemeyeceğim", silenced!.Text, StringComparison.Ordinal);
        Assert.False(tools.AllowsNudge("goz"));
        Assert.Contains("2 türü yeniden açtım", (await tools.RunQuickAsync(QuickIntents.Match("kapattığın hatırlatmaları aç")!, CancellationToken.None))!.Text, StringComparison.Ordinal);
        Assert.True(tools.AllowsNudge("goz"));
    }
}
