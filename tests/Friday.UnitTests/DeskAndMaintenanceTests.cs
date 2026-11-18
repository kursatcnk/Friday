using System.IO.Compression;
using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class DeskIntentTests
{
    [Theory]
    [InlineData("seçili metni düzelt", "duzelt", "")]
    [InlineData("Seçili metni İngilizceye çevir", "cevir", "İngilizce")]
    [InlineData("seçili paragrafı almancaya çevir", "cevir", "Almanca")]
    [InlineData("seçili yazıyı resmileştir", "resmilestir", "")]
    [InlineData("seçili metni özetle", "ozetle", "")]
    [InlineData("seçili e-postayı kısalt", "kisalt", "")]
    public void RecognisesSelectionCommands(string sentence, string operation, string language)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("secili", command?.Tool);
        Assert.Equal(operation, command!.Arguments["islem"]);
        Assert.Equal(language, command.Arguments.TryGetValue("dil", out var value) ? value : "");
    }

    [Theory]
    [InlineData("Şunu yaz: Merhaba, yarın 10'da görüşelim.", "Merhaba, yarın 10'da görüşelim.")]
    [InlineData("Hey Friday, bunu yaz toplantı ertelendi", "toplantı ertelendi")]
    [InlineData("yaz: Deneme 1 2 3", "Deneme 1 2 3")]
    public void TypesTheExactWords(string sentence, string text)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("yaz", command?.Tool);
        Assert.Equal(text, command!.Arguments["metin"]);
    }

    [Fact]
    public void CoinTossIsNotTyping() => Assert.Equal("sans", QuickIntents.Match("yazı tura at")?.Tool);

    [Theory]
    [InlineData("dikte modunu aç")]
    [InlineData("dikte modunu başlat")]
    [InlineData("ben söyleyeyim sen yaz")]
    public void StartsDictation(string sentence) => Assert.Equal("dikte", QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("dikte bitti", true)]
    [InlineData("Dikteyi bitir.", true)]
    [InlineData("tamam bitti", true)]
    [InlineData("toplantı bitti", false)]
    public void EndsDictation(string sentence, bool end) => Assert.Equal(end, QuickIntents.IsDictationEnd(sentence));

    [Theory]
    [InlineData("Yeni satır.", "enter")]
    [InlineData("geri al", "geri_al")]
    [InlineData("merhaba dünya", null)]
    public void DictationKeys(string sentence, string? key) => Assert.Equal(key, QuickIntents.DictationKey(sentence));

    [Theory]
    [InlineData("pencereyi sola yasla", "sol")]
    [InlineData("bu pencereyi sağa al", "sag")]
    [InlineData("tam ekran yap", "buyut")]
    [InlineData("pencereyi küçült", "kucult")]
    [InlineData("ekranı ikiye böl", "bol")]
    public void ArrangesWindows(string sentence, string action)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("pencere", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
    }

    [Fact]
    public void AllWindowsStillMeansShowDesktop() => Assert.Equal("masaustu", QuickIntents.Match("tüm pencereleri küçült")?.Arguments["islem"]);

    [Theory]
    [InlineData("gündem ne", "gundem")]
    [InlineData("haberleri oku", "gundem")]
    [InlineData("gündemde ne var", "gundem")]
    [InlineData("spor haberleri", "spor")]
    [InlineData("teknoloji haberleri neler", "teknoloji")]
    public void ReadsNews(string sentence, string category)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("haber", command?.Tool);
        Assert.Equal(category, command!.Arguments["kategori"]);
    }

    [Fact]
    public void ParsesRssAndAtomHeadlines()
    {
        const string rss = "<rss><channel><title>Kaynak</title><item><title>Birinci &amp; haber</title></item><item><title>İkinci</title></item></channel></rss>";
        const string atom = "<feed xmlns=\"http://www.w3.org/2005/Atom\"><title>NTV</title><entry><title>Atom başlığı</title></entry></feed>";
        Assert.Equal(["Birinci & haber", "İkinci"], AssistantTools.ParseHeadlines(rss));
        Assert.Equal(["Atom başlığı"], AssistantTools.ParseHeadlines(atom));
        Assert.Equal("Neler biliniyor?", AssistantTools.EndSentence("Neler biliniyor?"));
        Assert.Equal("Deprem oldu.", AssistantTools.EndSentence("Deprem oldu"));
    }
}

public sealed class CardTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;

    public CardTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")));
    }

    [Fact]
    public async Task StopwatchShowsABigClock()
    {
        await _tools.RunQuickAsync(QuickIntents.Match("kronometreyi başlat")!, CancellationToken.None);
        var reply = await _tools.RunQuickAsync(QuickIntents.Match("kronometre kaç oldu")!, CancellationToken.None);
        Assert.Equal("00:00", reply?.Card?.Big);
    }

    [Fact]
    public async Task StatusReportShowsGauges()
    {
        var reply = await _tools.RunQuickAsync(QuickIntents.Match("durum raporu")!, CancellationToken.None);
        Assert.NotNull(reply?.Card);
        Assert.Contains(reply.Card.Gauges, gauge => gauge.Label == "İşlemci");
        Assert.Contains(reply.Card.Gauges, gauge => gauge.Label == "Bellek");
        Assert.All(reply.Card.Gauges, gauge => Assert.InRange(gauge.Value, 0, 1));
    }

    [Fact]
    public async Task ReminderListBecomesACard()
    {
        await _tools.RunQuickAsync(QuickIntents.Match("yarın 9'da doktoru aramayı hatırlat")!, CancellationToken.None);
        var reply = await _tools.RunQuickAsync(QuickIntents.Match("hatırlatmalarım neler")!, CancellationToken.None);
        var row = Assert.Single(reply!.Card!.Rows);
        Assert.Equal("Doktoru aramayı", row.Value);
    }

    [Fact]
    public async Task RepliesWithoutACardStayPlain()
    {
        var reply = await _tools.RunQuickAsync(QuickIntents.Match("yazı tura at")!, CancellationToken.None);
        Assert.Null(reply?.Card);
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}

public class MaintenanceTests
{
    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void BackupHoldsOnlyPersonalData()
    {
        using var temp = new TempDirectory();
        var data = Path.Combine(temp.Path, "data");
        Write(Path.Combine(data, "settings.json"), "{}");
        Write(Path.Combine(data, "memory.json"), "[]");
        Write(Path.Combine(data, "chats", "a.json"), "{}");
        Write(Path.Combine(data, "uv-cache", "torch.whl"), "büyük");
        Write(Path.Combine(data, "logs", "friday.log"), "günlük");
        var zip = Path.Combine(temp.Path, "yedek.zip");

        Assert.Equal(3, DataMaintenance.CreateBackup(data, zip));
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["chats/a.json", "memory.json", "settings.json"], archive.Entries.Select(entry => entry.FullName).Order());
    }

    [Fact]
    public void RestoreWaitsForTheNextStartAndKeepsTheOldData()
    {
        using var temp = new TempDirectory();
        var data = Path.Combine(temp.Path, "data");
        Write(Path.Combine(data, "memory.json"), "eski");
        var source = Path.Combine(temp.Path, "kaynak");
        Write(Path.Combine(source, "memory.json"), "yedek");
        Write(Path.Combine(source, "chats", "b.json"), "{}");
        var zip = Path.Combine(temp.Path, "yedek.zip");
        ZipFile.CreateFromDirectory(source, zip);

        Assert.True(DataMaintenance.StageRestore(zip, data));
        Assert.Equal("eski", File.ReadAllText(Path.Combine(data, "memory.json")));

        Assert.True(DataMaintenance.ApplyPendingRestore(data));
        Assert.Equal("yedek", File.ReadAllText(Path.Combine(data, "memory.json")));
        Assert.True(File.Exists(Path.Combine(data, "chats", "b.json")));
        var kept = Assert.Single(Directory.GetDirectories(data, "before-restore-*"));
        Assert.Equal("eski", File.ReadAllText(Path.Combine(kept, "memory.json")));
        Assert.False(DataMaintenance.ApplyPendingRestore(data));
    }

    [Fact]
    public void RejectsAZipThatIsNotABackup()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "kaynak");
        Write(Path.Combine(source, "foto.jpg"), "x");
        var zip = Path.Combine(temp.Path, "baska.zip");
        ZipFile.CreateFromDirectory(source, zip);
        Assert.False(DataMaintenance.StageRestore(zip, Path.Combine(temp.Path, "data")));
    }

    [Fact]
    public void OffersXttsOnlyWhenItIsNotUsed()
    {
        using var temp = new TempDirectory();
        var paths = new FridayPaths(temp.Path);
        Write(Path.Combine(paths.Tools, "xtts-env", "big.bin"), new string('x', 2 * 1024 * 1024));
        Write(Path.Combine(paths.Data, "uv-cache", "cache.bin"), new string('x', 2 * 1024 * 1024));

        var piper = DataMaintenance.Reclaimable(paths, new FridaySettings { SpeechEngine = "piper" });
        Assert.Contains(piper, item => item.Path.EndsWith("xtts-env", StringComparison.Ordinal));
        Assert.Contains(piper, item => item.Path.EndsWith("uv-cache", StringComparison.Ordinal));
        Assert.DoesNotContain(DataMaintenance.Reclaimable(paths, new FridaySettings { SpeechEngine = "xtts" }),
            item => item.Path.EndsWith("xtts-env", StringComparison.Ordinal));

        var freed = DataMaintenance.Reclaim(piper);
        Assert.True(freed >= 4 * 1024 * 1024);
        Assert.False(Directory.Exists(Path.Combine(paths.Tools, "xtts-env")));
    }

    [Theory]
    [InlineData(8_844_000_000L, "8,2 GB")]
    [InlineData(540L * 1024 * 1024, "540 MB")]
    public void DescribesSizes(long bytes, string text) => Assert.Equal(text, DataMaintenance.Describe(bytes));
}
