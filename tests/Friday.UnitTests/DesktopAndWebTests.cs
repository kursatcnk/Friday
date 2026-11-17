using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public sealed class ConfirmationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;

    public ConfirmationTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")));
    }

    [Fact]
    public void FirstRequestOnlyAsksForConfirmation()
    {
        _tools.LastUserText = "YouTube sekmesini kapat, evet eminim";
        Assert.False(_tools.Confirmed("kapat|YouTube", "YouTube kapatılacak", out var ask));
        Assert.Contains("ONAY GEREKİYOR", ask, StringComparison.Ordinal);
    }

    [Fact]
    public void RunsOnlyAfterTheUserSaysYesNextTurn()
    {
        _tools.LastUserText = "YouTube sekmesini kapat";
        Assert.False(_tools.Confirmed("kapat|YouTube", "YouTube kapatılacak", out _));

        _tools.LastUserText = "Evet, kapat.";
        Assert.True(_tools.Confirmed("kapat|YouTube", "YouTube kapatılacak", out _));

        // Onay bir kez kullanılır; aynı işlem tekrar istenirse yeniden sorulur.
        Assert.False(_tools.Confirmed("kapat|YouTube", "YouTube kapatılacak", out _));
    }

    [Fact]
    public void ConfirmationDoesNotCarryOverToADifferentAction()
    {
        _tools.LastUserText = "YouTube sekmesini kapat";
        Assert.False(_tools.Confirmed("kapat|YouTube", "YouTube kapatılacak", out _));

        _tools.LastUserText = "evet";
        Assert.False(_tools.Confirmed("kapat|Gmail", "Gmail kapatılacak", out _));
    }

    [Fact]
    public void NoMeansNo()
    {
        _tools.LastUserText = "Gmail sekmesini kapat";
        Assert.False(_tools.Confirmed("kapat|Gmail", "Gmail kapatılacak", out _));

        _tools.LastUserText = "hayır vazgeçtim";
        Assert.False(_tools.Confirmed("kapat|Gmail", "Gmail kapatılacak", out _));
    }

    public void Dispose()
    {
        _http.Dispose();
        _temp.Dispose();
    }
}

public class WebReaderTests
{
    [Fact]
    public void DecodesDuckDuckGoRedirects() =>
        Assert.Equal("https://www.tcmb.gov.tr/kurlar/kurlar_tr.html",
            WebReader.DecodeRedirect("//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.tcmb.gov.tr%2Fkurlar%2Fkurlar_tr.html&rut=abc"));

    [Fact]
    public void KeepsDirectLinks() =>
        Assert.Equal("https://example.com/a", WebReader.DecodeRedirect("https://example.com/a"));

    [Fact]
    public void CleansHtmlToText() =>
        Assert.Equal("Dolar 41,2 & Euro", WebReader.Clean("<b>Dolar</b>  41,2 &amp; <i>Euro</i>"));
}

public class TabTitleTests
{
    [Theory]
    [InlineData("kursatcnk (Kürşatcan Çankaroğlu) - Memory usage - 403 MB", "kursatcnk (Kürşatcan Çankaroğlu)")]
    [InlineData("Trendyol - Bellek kullanımı - 1,2 GB", "Trendyol")]
    [InlineData("YouTube - Ses çalıyor", "YouTube - Ses çalıyor")]
    public void RemovesChromeMemorySuffix(string title, string expected) =>
        Assert.Equal(expected, Desktop.CleanTitle(title));
}

public class FocusedExtractionTests
{
    [Fact]
    public void PicksParagraphsRelevantToTheQuestion()
    {
        var html = "<title>Döviz</title>" +
                   string.Concat(Enumerable.Range(0, 30).Select(index => $"<p>Genel haber paragrafı numara {index}, piyasalar hakkında uzun bir yazı.</p>")) +
                   "<p>Dolar kuru bugün 49,55 lira seviyesinde işlem görüyor.</p>";

        var text = WebReader.Extract(html, 300, "Bugün dolar kaç lira?");

        Assert.Contains("49,55", text, StringComparison.Ordinal);
    }
}

public class VideoSearchTests
{
    [Fact]
    public void FindsTheFirstVideoAndDecodesItsTitle()
    {
        const string html = """{"contents":[{"videoRenderer":{"videoId":"rFZHOHl-L8A","thumbnail":{"x":1},"title":{"runs":[{"text":"lofi hip hop radio \u0026 beats \"live\""}]}}},{"videoRenderer":{"videoId":"n61ULEU7CO0","title":{"runs":[{"text":"Best of lofi"}]}}}]}""";
        var video = AssistantTools.FirstVideo(html);
        Assert.NotNull(video);
        Assert.Equal("rFZHOHl-L8A", video.Value.Id);
        Assert.Equal("lofi hip hop radio & beats \"live\"", video.Value.Title);
    }

    [Fact]
    public void ReturnsNothingWhenThereIsNoVideo() =>
        Assert.Null(AssistantTools.FirstVideo("<html>consent</html>"));
}

public class VideoSearchEdgeTests
{
    [Fact]
    public void SkipsChannelCardsThatAreNotRealVideos()
    {
        const string html = """{"universalWatchCardRenderer":{"videoRenderer":{"title":{"dynamicTextViewModel":{"text":{"content":"Kanal - Konu"}}}}},"videoRenderer":{"videoId":"3NycM9lYdRI","x":1,"title":{"runs":[{"text":"Rahatlatıcı Piyano Müziği"}]}}}""";
        var video = AssistantTools.FirstVideo(html);
        Assert.Equal(("3NycM9lYdRI", "Rahatlatıcı Piyano Müziği"), video);
    }

    [Fact]
    public void NeverBorrowsTheTitleOfAnotherVideo()
    {
        var html = "\"videoRenderer\":{\"videoId\":\"AAAAAAAAAAA\"" + new string('x', 5000) +
                   "\"videoRenderer\":{\"videoId\":\"BBBBBBBBBBB\",\"title\":{\"runs\":[{\"text\":\"İkinci video\"}]}";
        Assert.Equal(("AAAAAAAAAAA", ""), AssistantTools.FirstVideo(html));
    }
}
