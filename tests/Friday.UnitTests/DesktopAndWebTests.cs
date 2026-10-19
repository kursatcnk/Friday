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
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ConversationLog(_temp.Path));
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
