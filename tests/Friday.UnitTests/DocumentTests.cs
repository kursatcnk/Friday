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
    [InlineData("az önce yazdığım harcama yanlış, onu sil", false)]
    [InlineData("10 dakika sonra çayı kontrol etmemi hatırlat", false)]
    [InlineData("raporu bul, sonra masaüstüne kaydet", true)]
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

public class CreativeFeatureTests
{
    [Theory]
    [InlineData("bunu sonra okuyacağım", "sonra_oku", "kaydet")]
    [InlineData("bu sayfayı okuma listeme ekle", "sonra_oku", "kaydet")]
    [InlineData("okuma listem", "sonra_oku", "listele")]
    [InlineData("sıradakini aç", "sonra_oku", "ac")]
    [InlineData("günlüğe yaz: sunum çok iyi geçti", "gunluk", "yaz")]
    [InlineData("bu hafta günlüğüme ne yazmışım", "gunluk", "oku")]
    public void RecognisesCommands(string sentence, string tool, string action)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal(tool, command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
    }

    [Theory]
    [InlineData("bu şarkıyı kaydet", "sarki_kaydet")]
    [InlineData("diskimde ne yer kaplıyor", "disk_analizi")]
    [InlineData("yer açmama yardım et", "disk_analizi")]
    public void RecognisesSimpleCommands(string sentence, string tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool);

    [Fact]
    public void DailyBriefingIsNotTheJournal() => Assert.NotEqual("gunluk", QuickIntents.Match("günlük özet")?.Tool);

    [Fact]
    public void JournalKeepsEntriesByDay()
    {
        using var temp = new TempDirectory();
        var journal = new JournalStore(Path.Combine(temp.Path, "journal"));
        var now = new DateTime(2026, 11, 19, 18, 30, 0);
        journal.Add(now.AddDays(-1).AddHours(-2), "koşuya çıktım");
        journal.Add(now.AddHours(-1), "sunum iyi geçti");
        var week = journal.Between(now.Date.AddDays(-6), now);
        Assert.Equal(2, week.Count);
        Assert.Equal("Dün: 16:30 Koşuya çıktım. Bugün: 17:30 Sunum iyi geçti.", JournalStore.Describe(week, now));
        Assert.Single(journal.Between(now.Date, now));
    }
}

public class RhythmGoalTests
{
    private static RhythmDay Day(int offset, double focus) => new() { Date = new DateOnly(2026, 11, 19).AddDays(offset), FocusMinutes = focus };

    [Fact]
    public void StreakCountsConsecutiveDaysAndKeepsTodayOpen()
    {
        Assert.Equal(3, RhythmGoal.Streak([Day(-4, 10), Day(-3, 130), Day(-2, 125), Day(-1, 200), Day(0, 30)], 120));
        Assert.Equal(4, RhythmGoal.Streak([Day(-3, 130), Day(-2, 125), Day(-1, 200), Day(0, 121)], 120));
        Assert.Equal(0, RhythmGoal.Streak([Day(-1, 50), Day(0, 30)], 120));
    }

    [Fact]
    public void DescribesProgress()
    {
        Assert.Equal("Bugün 45 dakika odaklandın; 2 saatlik hedefe 1 saat 15 dakika kaldı. 3 gündür üst üste hedefini tutturuyorsun.", RhythmGoal.Describe(45, 120, 3));
        Assert.StartsWith("Bugünkü 2 saatlik odak hedefini tamamladın", RhythmGoal.Describe(130, 120, 1));
    }

    [Theory]
    [InlineData("odak hedefim 3 saat", "180")]
    [InlineData("bugünkü odak hedefimi 90 dakika yap", "90")]
    public void SetsTheGoalByVoice(string sentence, string minutes)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("odak_hedefi", command?.Tool);
        Assert.Equal(minutes, command!.Arguments["dakika"]);
    }

    [Fact]
    public void AsksForProgress() => Assert.Equal("odak_hedefi", QuickIntents.Match("hedefime ne kadar kaldı")?.Tool);
}

public class DeepFeatureTests
{
    [Theory]
    [InlineData("4 pomodoro yap", "baslat", "4")]
    [InlineData("pomodoro serisi başlat", "baslat", "4")]
    [InlineData("iki pomodoro yapalım", "baslat", "2")]
    [InlineData("pomodoroyu durdur", "durdur", null)]
    public void RecognisesPomodoro(string sentence, string action, string? count)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("pomodoro", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        if (count is not null)
        {
            Assert.Equal(count, command.Arguments["adet"]);
        }
    }

    [Theory]
    [InlineData("ruh halim 7", "7")]
    [InlineData("bugün kendimi yorgun hissediyorum", "4")]
    [InlineData("ruh halim çok iyi", "9")]
    public void RecognisesMood(string sentence, string score)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("ruh_hali", command?.Tool);
        Assert.Equal(score, command!.Arguments["puan"]);
    }

    [Theory]
    [InlineData("geçici dosyaları temizle")]
    [InlineData("geri dönüşüm kutusunu boşalt")]
    [InlineData("biraz yer aç")]
    public void RecognisesCleanup(string sentence) => Assert.Equal("temizlik", QuickIntents.Match(sentence)?.Tool);

    [Fact]
    public void RecognisesChecklists()
    {
        var command = QuickIntents.Match("seyahat listesini kontrol et");
        Assert.Equal("kontrol_listesi", command?.Tool);
        Assert.Equal("seyahat", command!.Arguments["ad"]);
    }

    [Fact]
    public void MoodAverageReadsJournalLines()
    {
        var now = new DateTime(2026, 11, 19, 20, 0, 0);
        Assert.Equal(6, JournalStore.MoodAverage([(now, "Ruh hali: 8/10"), (now, "Ruh hali: 4/10 (yorgun)"), (now, "Koşuya çıktım")]));
        Assert.Null(JournalStore.MoodAverage([(now, "Koşuya çıktım")]));
    }
}

public class FocusStepTests
{
    [Theory]
    [InlineData("sessiz:90", "90 dk rahatsız etme")]
    [InlineData("pomodoro:4", "4 pomodoro başlat")]
    public void NewStepsRoundTrip(string text, string description)
    {
        var step = RoutineStep.Parse(text);
        Assert.Equal(description, step.Describe());
        Assert.Equal(text, step.Format());
    }

    [Fact]
    public void QuietNeedsMinutes() => Assert.Throws<FormatException>(() => RoutineStep.Parse("sessiz:uzun"));

    [Fact]
    public void DeepWorkTemplateParses() =>
        Assert.All(RoutineStep.Templates.First(template => template.Name == "derin çalışma").Steps, step => RoutineStep.Parse(step));
}

