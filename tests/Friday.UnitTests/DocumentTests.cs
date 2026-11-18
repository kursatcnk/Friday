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

public class TriggerAndListTests
{
    [Theory]
    [InlineData("bilgisayara dönünce çamaşırları hatırlat", "donus", "çamaşırları")]
    [InlineData("döndüğümde annemi aramayı hatırlat", "donus", "annemi aramayı")]
    [InlineData("chrome'u açınca faturayı hatırlat", "uygulama:chrome", "faturayı")]
    [InlineData("spotify açılınca podcasti hatırlat", "uygulama:spotify", "podcasti")]
    public void RecognisesConditionalReminders(string sentence, string condition, string text)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("kosullu_hatirlatici", command?.Tool);
        Assert.Equal(condition, command!.Arguments["kosul"]);
        Assert.Equal(text, command.Arguments["metin"]);
    }

    [Theory]
    [InlineData("ertele", "")]
    [InlineData("10 dakika ertele", "10")]
    [InlineData("hatırlatmayı beş dakika ertele", "5")]
    [InlineData("sonra hatırlat", "")]
    public void RecognisesSnooze(string sentence, string minutes)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("ertele", command?.Tool);
        Assert.Equal(minutes, command!.Arguments.GetValueOrDefault("dakika", ""));
    }

    [Fact]
    public void BreakSnoozeStaysWithTheRhythm() => Assert.Equal("ritim", QuickIntents.Match("molayı ertele")?.Tool);

    [Theory]
    [InlineData("market listesine süt ve ekmek ekle", "ekle", "market", "süt ve ekmek")]
    [InlineData("market listesinden sütü çıkar", "cikar", "market", "sütü")]
    [InlineData("market listemde ne var", "oku", "market", null)]
    [InlineData("okunacaklar listesini temizle", "temizle", "okunacaklar", null)]
    public void RecognisesLists(string sentence, string action, string list, string? item)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("liste", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(list, command.Arguments["ad"]);
        if (item is not null)
        {
            Assert.Equal(item, command.Arguments["oge"]);
        }
    }

    [Theory]
    [InlineData("1 saat rahatsız etme", "ac", "60")]
    [InlineData("yarım saat sessiz ol", "ac", "30")]
    [InlineData("beni rahatsız etme", "ac", "60")]
    [InlineData("rahatsız etmeyi kapat", "kapat", null)]
    public void RecognisesDoNotDisturb(string sentence, string action, string? minutes)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("rahatsiz_etme", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        if (minutes is not null)
        {
            Assert.Equal(minutes, command.Arguments["dakika"]);
        }
    }

    [Fact]
    public void MuteIsStillMute() => Assert.Equal("medya", QuickIntents.Match("sesi kapat")?.Tool);

    [Fact]
    public void ListsAddRemoveAndClear()
    {
        using var temp = new TempDirectory();
        var lists = new ListStore(Path.Combine(temp.Path, "lists.json"));
        Assert.Equal(["süt", "ekmek"], lists.Add("Market", "süt ve ekmek"));
        Assert.Empty(lists.Add("market", "Süt"));
        Assert.Equal("süt", lists.Remove("market", "sütü"));
        Assert.Equal(["ekmek"], new ListStore(Path.Combine(temp.Path, "lists.json")).Items("market"));
        Assert.Equal(1, lists.Clear("market"));
        Assert.Empty(lists.Names);
    }

    [Fact]
    public void TriggeredRemindersFireOnTheirEvent()
    {
        using var temp = new TempDirectory();
        var store = new ReminderStore(Path.Combine(temp.Path, "reminders.json"));
        var now = new DateTime(2026, 11, 18, 10, 0, 0);
        store.AddTriggered("donus", "çamaşırları", now);
        store.AddTriggered("uygulama:chrome", "faturayı", now);
        Assert.Empty(store.TakeDue(now.AddDays(30)));
        Assert.Empty(store.TakeTriggered("uygulama", "Spotify"));
        Assert.Equal("faturayı", Assert.Single(store.TakeTriggered("uygulama", "Google Chrome")).Text);
        var back = Assert.Single(store.TakeTriggered("donus"));
        Assert.Equal("Hoş geldin! Çamaşırları unutma.", ReminderStore.Announcement(back));
        Assert.Empty(store.Upcoming);
    }

    [Theory]
    [InlineData("donus", "Bilgisayara dönünce")]
    [InlineData("uygulama:Spotify", "Spotify açılınca")]
    public void SchedulesCanBeEvents(string text, string description)
    {
        var schedule = RoutineSchedule.Parse(text)!;
        Assert.Equal(description, schedule.Describe());
        Assert.Equal(text, schedule.Format());
        Assert.False(schedule.IsDue(DateTime.Now, null));
    }

    [Fact]
    public void AssemblyStyleDescriptionsAreShortened() => Assert.Equal("WhatsApp", ScreenTimeLog.Label("WhatsApp", "WhatsApp.Root", "WhatsApp"));
}

public class IntervalReminderTests
{
    [Theory]
    [InlineData("her 30 dakikada bir su içmeyi hatırlat", "her:30", "su içmeyi")]
    [InlineData("her saat başı esnemeyi hatırlat", "her:60", "esnemeyi")]
    [InlineData("her 2 saatte bir gözlerimi dinlendirmeyi hatırlat", "her:120", "gözlerimi dinlendirmeyi")]
    [InlineData("her yarım saatte bir duruşumu hatırlat", "her:30", "duruşumu")]
    public void RecognisesIntervals(string sentence, string rule, string text)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("hatirlatici", command?.Tool);
        Assert.Equal(rule, command!.Arguments["tekrar"]);
        Assert.Equal(text, command.Arguments["metin"]);
    }

    [Fact]
    public void IntervalsSleepAtNight()
    {
        var evening = new DateTime(2026, 11, 18, 21, 50, 0);
        Assert.Equal(new DateTime(2026, 11, 19, 8, 0, 0), RepeatRule.Next(evening, "her:30", evening));
        var morning = new DateTime(2026, 11, 19, 9, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 19, 10, 0, 0), RepeatRule.Next(morning, "her:60", morning.AddMinutes(5)));
        Assert.Equal("her 2 saatte bir", RepeatRule.Describe(morning, "her:120"));
        Assert.True(RepeatRule.IsKnown("her:45"));
        Assert.False(RepeatRule.IsKnown("her:2"));
    }

    [Fact]
    public void RecognisesWeeklyReview() => Assert.Equal("haftalik_ozet", QuickIntents.Match("bu hafta neler yaptım")?.Tool);
}

