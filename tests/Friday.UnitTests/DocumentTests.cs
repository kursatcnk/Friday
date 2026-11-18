using System.IO.Compression;
using System.Text;
using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class DocumentIndexTests
{
    [Fact]
    public void StemsTurkishWordsAndDropsStopWords()
    {
        var terms = DocumentIndex.Terms("Sözleşmesinde iptal şartı neydi ve 18.500 TL").ToList();
        Assert.Contains("sozle", terms);
        Assert.Contains("iptal", terms);
        Assert.Contains("sarti", terms);
        Assert.Contains("18", terms);
        Assert.DoesNotContain("neydi", terms);
        Assert.DoesNotContain("ve", terms);
    }

    [Fact]
    public void FindsTheRightChunk()
    {
        var index = new DocumentIndex();
        index.AddRange(
        [
            (0, 0, "Kira sözleşmesi: kiracı iki ay önceden bildirerek sözleşmeyi feshedebilir. Erken çıkışta bir aylık kira ceza olarak ödenir."),
            (1, 0, "Bütçe raporu: üçüncü çeyrekte pazarlama harcaması 240 bin lira oldu."),
            (2, 0, "Alışveriş listesi: süt, ekmek, yumurta.")
        ]);
        Assert.Equal(0, index.Search("kira sözleşmesinde iptal ve ceza şartı neydi")[0].Document);
        Assert.Equal(1, index.Search("raporda pazarlama bütçesi kaçtı")[0].Document);
        Assert.Empty(index.Search("uzay mekiği"));
    }

    [Fact]
    public void SplitsLongTextWithOverlap()
    {
        var text = string.Join(" ", Enumerable.Range(1, 400).Select(number => $"Cümle {number} burada bitiyor."));
        var chunks = DocumentIndex.Split(text);
        Assert.True(chunks.Count > 5);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 900));
    }
}

public class DocumentTextTests
{
    [Fact]
    public void ReadsABrowserPdfWithTurkishLetters()
    {
        var text = PdfText.Extract(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kira.pdf")));
        Assert.Contains("Kira Sözleşmesi", text);
        Assert.Contains("18.500", text);
        Assert.Contains("İptal şartı", text);
        Assert.Contains("Şule", text);
    }

    [Fact]
    public void ReadsWordDocuments()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Path);
        var path = Path.Combine(temp.Path, "not.docx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
            writer.Write("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Toplantı perşembe</w:t></w:r><w:r><w:t xml:space="preserve"> saat 14'te.</w:t></w:r></w:p><w:p><w:r><w:t>İkinci paragraf</w:t></w:r></w:p></w:body></w:document>""");
        }

        var text = DocumentText.Extract(path);
        Assert.Contains("Toplantı perşembe saat 14'te.", text);
        Assert.Contains("\nİkinci paragraf", text);
    }
}

public class AgentPlanTests
{
    [Theory]
    [InlineData("dün indirdiğim pdf'i bul, özetle ve notlarıma ekle", true)]
    [InlineData("önce sesi kıs sonra müziği aç", true)]
    [InlineData("raporu özetleyip masaüstüne kaydet", true)]
    [InlineData("hava nasıl", false)]
    [InlineData("spotify'ı aç", false)]
    [InlineData("bu hafta ne kadar çalıştım", false)]
    public void RecognisesMultiStepRequests(string question, bool multiStep) => Assert.Equal(multiStep, AgentPlan.IsMultiStep(question));
}

public class RemotePanelTests
{
    [Fact]
    public async Task ServesThePageAndAnswersOnlyWithTheKey()
    {
        using var panel = new RemotePanel((text, speak, _) => Task.FromResult($"cevap: {text} ({speak})"), () => ["çalışma modu"]);
        panel.Start(0, "abc123def456", System.Net.IPAddress.Loopback);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{panel.Port}") };

        Assert.Contains("Friday", await http.GetStringAsync("/"));

        using var denied = new HttpRequestMessage(HttpMethod.Post, "/ask") { Content = new StringContent("""{"text":"merhaba"}""") };
        denied.Headers.Add("X-Friday-Key", "yanlis");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await http.SendAsync(denied)).StatusCode);

        using var allowed = new HttpRequestMessage(HttpMethod.Post, "/ask") { Content = new StringContent("""{"text":"merhaba","speak":true}""") };
        allowed.Headers.Add("X-Friday-Key", "abc123def456");
        var reply = await (await http.SendAsync(allowed)).Content.ReadAsStringAsync();
        Assert.Contains("cevap: merhaba (True)", reply);

        using var routines = new HttpRequestMessage(HttpMethod.Get, "/routines");
        routines.Headers.Add("X-Friday-Key", "abc123def456");
        Assert.Contains("çalışma modu", await (await http.SendAsync(routines)).Content.ReadAsStringAsync());
    }
}
