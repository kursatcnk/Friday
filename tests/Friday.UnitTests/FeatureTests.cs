using Friday.Core;

namespace Friday.UnitTests;

public sealed class TempDirectory : IDisposable
{
    // Sistemin TEMP klasörü C: üzerinde; testler kendi çıktı klasörlerinde çalışır.
    public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "tmp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

public class MemoryStoreTests
{
    [Fact]
    public void RemembersAcrossInstancesAndSkipsDuplicates()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "memory.json");
        var store = new MemoryStore(file);
        store.Remember("Arabanın muayenesi Kasım'da.");
        store.Remember("arabanın muayenesi kasım'da");

        var reloaded = new MemoryStore(file);
        var item = Assert.Single(reloaded.Items);
        Assert.Equal("Arabanın muayenesi Kasım'da.", item.Text);
    }

    [Fact]
    public void ForgetsByTurkishCaseInsensitiveMatch()
    {
        using var temp = new TempDirectory();
        var store = new MemoryStore(Path.Combine(temp.Path, "memory.json"));
        store.Remember("İzmir'de yaşıyorum.");
        store.Remember("Kahveyi şekersiz içerim.");

        var removed = store.Forget("izmir");
        Assert.Single(removed);
        Assert.Equal("Kahveyi şekersiz içerim.", Assert.Single(store.Items).Text);
    }
}

public class PrivacyLedgerTests
{
    [Fact]
    public void RecordsAndReloadsTodaysEntries()
    {
        using var temp = new TempDirectory();
        var ledger = new PrivacyLedger(temp.Path);
        ledger.Record(DataFlow.Local, "Mikrofon", "3,2 sn dinlendi");
        ledger.Record(DataFlow.Outbound, "Hava durumu", "open-meteo.com · şehir: Ankara");

        var reloaded = new PrivacyLedger(temp.Path);
        Assert.Equal(2, reloaded.TodayEntries.Count);
        Assert.Equal(DataFlow.Outbound, reloaded.TodayEntries[1].Flow);
    }
}

public class ConversationLogTests
{
    [Fact]
    public void ReadsBackTodaysTurns()
    {
        using var temp = new TempDirectory();
        var log = new ConversationLog(temp.Path);
        log.Append("Saat kaç?", "Saat 11:05.");
        var turn = Assert.Single(log.Read(log.Today));
        Assert.Equal("Saat kaç?", turn.User);
        Assert.Empty(log.Read(log.Today.AddDays(-1)));
    }
}

public class RoutineStepTests
{
    [Theory]
    [InlineData("uygulama:Visual Studio", RoutineAction.App, "Visual Studio")]
    [InlineData("site: github.com ", RoutineAction.Website, "github.com")]
    [InlineData("söyle:Hazırım", RoutineAction.Say, "Hazırım")]
    [InlineData("ozet", RoutineAction.Briefing, "")]
    public void ParsesSteps(string text, RoutineAction action, string value) =>
        Assert.Equal(new RoutineStep(action, value), RoutineStep.Parse(text));

    [Theory]
    [InlineData("uç:mars")]
    [InlineData("site:")]
    public void RejectsBadSteps(string text) => Assert.Throws<FormatException>(() => RoutineStep.Parse(text));
}

public class FileSearchQueryTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0);

    [Fact]
    public void FiltersByKindDateAndFolder()
    {
        var sql = FileSearchQuery.Build("fatura", "pdf", 1, @"C:\Users\me\Downloads", Now);
        Assert.Contains("System.FileName LIKE '%fatura%'", sql, StringComparison.Ordinal);
        Assert.Contains("System.FileExtension = '.pdf'", sql, StringComparison.Ordinal);
        Assert.Contains("System.DateModified >= '2026-10-18 00:00:00'", sql, StringComparison.Ordinal);
        Assert.Contains("SCOPE='file:C:/Users/me/Downloads'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void StripsCharactersThatCouldBreakTheQuery()
    {
        var sql = FileSearchQuery.Build("o'reilly\" OR 1=1 --", "", 0, "", Now);
        Assert.DoesNotContain("'reilly", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\" OR", sql, StringComparison.Ordinal);
        Assert.Contains("%oreilly%", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsTurkishKindNames() =>
        Assert.Contains(".mp3", FileSearchQuery.Build("", "müzik", 0, "", Now), StringComparison.Ordinal);
}

public class SystemPromptTests
{
    [Fact]
    public void IncludesMemoriesRoutinesAndPersonality()
    {
        var settings = new FridaySettings { UserName = "Kürşat", Personality = "resmi" };
        var memories = new[] { new MemoryItem("1", "Kahveyi şekersiz içer.", DateTimeOffset.Now) };
        var prompt = SystemPrompt.Build(settings, memories, new DateTime(2026, 10, 19, 11, 5, 0));

        Assert.Contains("Adın Friday", prompt, StringComparison.Ordinal);
        Assert.Contains("Kürşat", prompt, StringComparison.Ordinal);
        Assert.Contains("Kahveyi şekersiz içer.", prompt, StringComparison.Ordinal);
        Assert.Contains("çalışma modu", prompt, StringComparison.Ordinal);
        Assert.Contains("\"siz\"", prompt, StringComparison.Ordinal);
        Assert.Contains("19 Ekim 2026 Pazartesi", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("11:05", prompt, StringComparison.Ordinal);
    }
}

public class ResampleTests
{
    [Fact]
    public void ConvertsPiperRateToOutputRateKeepingDuration()
    {
        var samples = Enumerable.Range(0, 22_050).Select(index => (short)(Math.Sin(index / 10.0) * 10_000)).ToArray();
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        var result = Friday.Infrastructure.Speaker.Resample(bytes, 22_050, 24_000);
        Assert.Equal(24_000 * 2, result.Length);
    }
}

public class PronunciationTests
{
    [Theory]
    [InlineData("Claude'a sordum.", "Klod'a sordum.")]
    [InlineData("Visual Studio ve Chrome açık.", "Vijual Stüdyo ve Krom açık.")]
    [InlineData("YouTube'dan izledim", "Yutub'dan izledim")]
    [InlineData("4,2 GB bellek", "4,2 gigabayt bellek")]
    [InlineData("Hayırlı akşamlar", "Hayırlı akşamlar")]
    public void RespellsForeignNames(string input, string expected) =>
        Assert.Equal(expected, Pronunciation.Apply(input));

    [Fact]
    public void UserOverridesWin() =>
        Assert.Equal("Kılod geldi", Pronunciation.Apply("Claude geldi", new Dictionary<string, string> { ["Claude"] = "Kılod" }));
}

public class VoiceWarmthTests
{
    [Fact]
    public void AddsShortRoomTailAndKeepsSilenceSilent()
    {
        var silent = new short[22_050];
        var result = VoiceWarmth.Process(silent, 22_050);
        Assert.Equal(22_050 + (int)(22_050 * 0.15), result.Length);
        Assert.All(result, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void LoudSpeechIsNotHardClipped()
    {
        var loud = Enumerable.Range(0, 22_050).Select(index => (short)(Math.Sin(index / 8.0) * 32_000)).ToArray();
        var result = VoiceWarmth.Process(loud, 22_050);
        // Sert kırpma, tepede düz kalan ardışık örnekler demektir.
        var longestFlatTop = 0;
        var run = 0;
        foreach (var sample in result)
        {
            run = Math.Abs((int)sample) >= 32_760 ? run + 1 : 0;
            longestFlatTop = Math.Max(longestFlatTop, run);
        }

        Assert.True(longestFlatTop <= 1);
    }

    [Fact]
    public void NormalLevelSpeechKeepsItsLoudness()
    {
        var normal = Enumerable.Range(0, 22_050).Select(index => (short)(Math.Sin(index / 8.0) * 8_000)).ToArray();
        var result = VoiceWarmth.Process(normal, 22_050, wet: 0, warmth: 0);
        var peak = result.Take(22_050).Skip(2_000).Max(sample => Math.Abs((int)sample));
        Assert.InRange(peak, 7_500, 8_500);
    }
}
