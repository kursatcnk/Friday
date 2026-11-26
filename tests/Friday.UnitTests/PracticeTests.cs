using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class PracticeIntentTests
{
    [Theory]
    [InlineData("spotify'ın sesini kıs", "kis", "spotify", "")]
    [InlineData("discord'un sesini biraz aç", "ac", "discord", "")]
    [InlineData("chrome'un sesini yüzde 30 yap", "ayarla", "chrome", "30")]
    [InlineData("spotify sesini 20'ye indir", "ayarla", "spotify", "20")]
    [InlineData("discord'u sessize al", "sessiz", "discord", "")]
    [InlineData("spotify'ın sesini geri aç", "sesli", "spotify", "")]
    [InlineData("tarayıcının sesini kapat", "sessiz", "tarayıcı", "")]
    public void ControlsAppVolume(string sentence, string action, string app, string level)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("uygulama_sesi", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(app, command.Arguments["ad"]);
        Assert.Equal(level, command.Arguments.GetValueOrDefault("deger", ""));
    }

    [Theory]
    [InlineData("hangi uygulamalar ses çıkarıyor", "uygulama_sesi")]
    [InlineData("sesi kıs", "medya")]
    [InlineData("sessize al", "medya")]
    [InlineData("müziğin sesini aç", null)]
    public void KeepsGeneralVolumeCommands(string sentence, string? tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool is "uygulama_ac" ? null : QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("spotify", "Spotify", true)]
    [InlineData("edge", "msedge", true)]
    [InlineData("tarayıcı", "chrome", true)]
    [InlineData("tarayıcı", "Spotify", false)]
    [InlineData("discord", "Spotify", false)]
    [InlineData("youtube", "chrome", true)]
    [InlineData("netflix", "msedge", true)]
    public void MatchesAudioApps(string spoken, string process, bool match) => Assert.Equal(match, AppAudio.Match(spoken)(process));
}

public sealed class PracticeToolTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;

    public PracticeToolTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")))
        {
            HabitBook = new HabitBook(Path.Combine(_temp.Path, "habit-book.json"))
        };
    }

    [Fact]
    public void NudgesToKeepAStreak()
    {
        var book = _tools.HabitBook!;
        var today = DateOnly.FromDateTime(DateTime.Now);
        book.Add("Kitap okumak", today.AddDays(-5));
        book.Add("Spor", today.AddDays(-5));
        for (var day = 1; day <= 3; day++)
        {
            book.Mark("kitap", today.AddDays(-day));
        }

        Assert.Equal("Bugün henüz kitap okumak işaretlenmedi; 3 günlük serin var, bozma!", _tools.StreakNudge(DateTime.Now));
        Assert.Null(_tools.StreakNudge(DateTime.Now));
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
