using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class TodoTests
{
    [Theory]
    [InlineData("yapılacaklara ekle: faturayı öde", "faturayı öde")]
    [InlineData("süt almayı yapılacaklara ekle", "süt almayı")]
    [InlineData("yeni görev: sunumu bitir", "sunumu bitir")]
    public void AddsTodosByVoice(string sentence, string text)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("yapilacak", command?.Tool);
        Assert.Equal("ekle", command!.Arguments["islem"]);
        Assert.Equal(text, command.Arguments["metin"]);
    }

    [Theory]
    [InlineData("yapılacaklarım neler", "listele")]
    [InlineData("bugün ne yapmam lazım", "listele")]
    [InlineData("tamamlananları temizle", "temizle")]
    public void ListsAndClears(string sentence, string action) => Assert.Equal(action, QuickIntents.Match(sentence)?.Arguments["islem"]);

    [Fact]
    public void CompletionIsTentative()
    {
        var command = QuickIntents.Match("faturayı ödemeyi tamamladım");
        Assert.Equal("tamamla", command?.Arguments["islem"]);
        Assert.True(command!.Tentative);
    }

    [Fact]
    public void StoreFindsCompletesAndClears()
    {
        using var temp = new TempDirectory();
        var store = new TodoStore(Path.Combine(temp.Path, "todos.json"));
        var now = new DateTime(2026, 11, 18, 10, 0, 0);
        var bill = store.Add("faturayı öde.", now);
        store.Add("Annemi ara", now);
        Assert.Equal("Faturayı öde", bill.Text);
        Assert.Same(bill, store.Add("FATURAYI ÖDE", now));

        Assert.Equal(bill.Id, store.Find("faturayı ödedim")?.Id);
        Assert.Null(store.Find("arabayı yıka"));

        store.Complete(bill.Id, now);
        Assert.Single(store.Open);
        Assert.Equal(2, store.Visible(now.AddHours(2)).Count);
        Assert.Single(store.Visible(now.AddDays(2)));
        Assert.Equal(1, store.ClearDone());
        Assert.Single(new TodoStore(Path.Combine(temp.Path, "todos.json")).Open);
    }
}

public class RecurringReminderTests
{
    [Theory]
    [InlineData("her gün 9'da ilacımı hatırlat", "9'da", "ilacımı", "gunluk")]
    [InlineData("her sabah 8'de su içmeyi hatırlat", "sabah 8'de", "su içmeyi", "gunluk")]
    [InlineData("hafta içi her gün 18'de spora gitmeyi hatırlat", "18'de", "spora gitmeyi", "haftaici")]
    [InlineData("her pazartesi 10'da toplantıyı hatırlat", "pazartesi 10'da", "toplantıyı", "haftalik")]
    public void ParsesRecurringReminders(string sentence, string when, string text, string rule)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("hatirlatici", command?.Tool);
        Assert.Equal(when, command!.Arguments["zaman"]);
        Assert.Equal(text, command.Arguments["metin"]);
        Assert.Equal(rule, command.Arguments["tekrar"]);
    }

    [Fact]
    public void RulesFindTheNextTime()
    {
        var friday = new DateTime(2026, 11, 20, 18, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 23, 18, 0, 0), RepeatRule.Next(friday, "haftaici", friday));
        Assert.Equal(new DateTime(2026, 11, 21, 18, 0, 0), RepeatRule.Next(friday, "gunluk", friday));
        Assert.Equal(new DateTime(2026, 11, 27, 18, 0, 0), RepeatRule.Next(friday, "haftalik", friday));
        Assert.Equal(new DateTime(2026, 11, 21, 18, 0, 0), RepeatRule.First(new DateTime(2026, 11, 21, 18, 0, 0), "haftasonu"));
        Assert.Equal(new DateTime(2026, 11, 23, 18, 0, 0), RepeatRule.First(new DateTime(2026, 11, 21, 18, 0, 0), "haftaici"));
        Assert.Equal("hafta içi her gün 18:00", RepeatRule.Describe(friday, "haftaici"));
    }

    [Fact]
    public void DailyNineMeansMorning()
    {
        var lateMorning = new DateTime(2026, 11, 18, 11, 30, 0);
        Assert.Equal(new DateTime(2026, 11, 19, 9, 0, 0), RepeatRule.Start(new DateTime(2026, 11, 18, 21, 0, 0), "9'da", "gunluk", lateMorning));
        Assert.Equal(new DateTime(2026, 11, 18, 21, 0, 0), RepeatRule.Start(new DateTime(2026, 11, 18, 21, 0, 0), "akşam 9'da", "gunluk", lateMorning));
        Assert.Equal(new DateTime(2026, 11, 23, 10, 0, 0), RepeatRule.Start(new DateTime(2026, 11, 23, 10, 0, 0), "pazartesi 10'da", "haftalik", lateMorning));
    }

    [Fact]
    public void RepeatingRemindersComeBack()
    {
        using var temp = new TempDirectory();
        var store = new ReminderStore(Path.Combine(temp.Path, "reminders.json"));
        var now = new DateTime(2026, 11, 18, 8, 0, 0);
        store.Add(now.AddHours(1), "ilacımı", now, "gunluk");
        store.Add(now.AddHours(1), "çayı", now);

        var due = store.TakeDue(now.AddHours(1).AddMinutes(1));
        Assert.Equal(2, due.Count);
        var left = Assert.Single(store.Upcoming);
        Assert.Equal("ilacımı", left.Text);
        Assert.Equal(new DateTime(2026, 11, 19, 9, 0, 0), left.Due);
    }
}

public class AmbientAndClipboardTests
{
    [Theory]
    [InlineData("yağmur sesi aç", "ac", "yagmur", "")]
    [InlineData("30 dakika beyaz gürültü aç", "ac", "beyaz", "30")]
    [InlineData("dalga sesi çal", "ac", "dalga", "")]
    [InlineData("yağmur sesini kapat", "kapat", null, null)]
    public void RecognisesAmbientSounds(string sentence, string action, string? kind, string? minutes)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("ortam", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        if (kind is not null)
        {
            Assert.Equal(kind, command.Arguments["tur"]);
            Assert.Equal(minutes, command.Arguments.TryGetValue("dakika", out var value) ? value : "");
        }
    }

    [Fact]
    public void MuteStillMutes() => Assert.Equal("medya", QuickIntents.Match("sesi kapat")?.Tool);

    [Theory]
    [InlineData("kopyaladıklarım", "listele", null)]
    [InlineData("pano geçmişini göster", "listele", null)]
    [InlineData("2. kopyaladığımı yapıştır", "yapistir", "2")]
    [InlineData("bir önce kopyaladığımı yapıştır", "yapistir", "2")]
    public void RecognisesClipboardHistory(string sentence, string action, string? index)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("pano_gecmisi", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        if (index is not null)
        {
            Assert.Equal(index, command.Arguments["sira"]);
        }
    }

    [Fact]
    public void HistoryKeepsTheNewestFirstWithoutDuplicates()
    {
        using var history = new ClipboardHistory();
        for (var index = 0; index < 25; index++)
        {
            history.Add($"metin {index}");
        }

        history.Add("metin 10");
        history.Add("   ");
        Assert.Equal(20, history.Items.Count);
        Assert.Equal("metin 10", history.Items[0]);
        Assert.Equal("metin 24", history.Items[1]);
    }

    [Theory]
    [InlineData("tekrar et")]
    [InlineData("ne dedin")]
    [InlineData("Anlamadım, tekrar et.")]
    public void RepeatsTheLastAnswer(string sentence) => Assert.Equal("tekrar", QuickIntents.Match(sentence)?.Tool);
}

public class CloseAndConvertTests
{
    [Fact]
    public void ClosesAppsTentatively()
    {
        var command = QuickIntents.Match("discord'u kapat");
        Assert.Equal("uygulama_kapat", command?.Tool);
        Assert.Equal("discord", command!.Arguments["ad"]);
        Assert.True(command.Tentative);
    }

    [Theory]
    [InlineData("ekranı kapat", "sistem")]
    [InlineData("bilgisayarı kapat", "sistem")]
    [InlineData("odağı kapat", "ritim")]
    public void SpecificClosingsKeepTheirMeaning(string sentence, string tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("100 dolar kaç lira", "100", "USD", "TRY")]
    [InlineData("1.500 lira kaç euro", "1500", "TRY", "EUR")]
    [InlineData("iki yüz euro kaç dolar eder", "200", "EUR", "USD")]
    [InlineData("50 doları liraya çevir", "50", "USD", "TRY")]
    public void ConvertsAmounts(string sentence, string amount, string from, string to)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("doviz", command?.Tool);
        Assert.Equal(amount, command!.Arguments["miktar"]);
        Assert.Equal(from, command.Arguments["kaynak"]);
        Assert.Equal(to, command.Arguments["hedef"]);
    }

    [Fact]
    public void PlainRateQuestionsStayRates()
    {
        var command = QuickIntents.Match("bugün dolar kaç lira");
        Assert.Equal("doviz", command?.Tool);
        Assert.False(command!.Arguments.ContainsKey("miktar"));
    }

    [Theory]
    [InlineData("250", 250)]
    [InlineData("1.250,5", 1250.5)]
    [InlineData("beş bin", 5000)]
    [InlineData("yüz elli", 150)]
    public void ParsesAmounts(string text, double amount) => Assert.Equal(amount, QuickIntents.ParseAmount(text));
}

public class HabitTests
{
    [Fact]
    public void SuggestsARepeatedHabitAtItsUsualTime()
    {
        using var temp = new TempDirectory();
        var habits = new HabitTracker(Path.Combine(temp.Path, "habits.json"));
        var monday = new DateTime(2026, 11, 16, 9, 5, 0);
        foreach (var offset in new[] { -7, -6, -5 })
        {
            habits.Record("rutin:çalışma modu", "çalışma modu", monday.AddDays(offset));
        }

        Assert.Null(habits.Suggest(monday.AddHours(3)));
        var suggestion = habits.Suggest(monday.AddMinutes(10));
        Assert.Equal("rutin:çalışma modu", suggestion?.Key);
        Assert.Null(habits.Suggest(monday.AddMinutes(12)));
    }

    [Fact]
    public void DoesNotSuggestWhatWasAlreadyDoneToday()
    {
        using var temp = new TempDirectory();
        var habits = new HabitTracker(Path.Combine(temp.Path, "habits.json"));
        var tuesday = new DateTime(2026, 11, 17, 20, 0, 0);
        foreach (var offset in new[] { -3, -2, -1 })
        {
            habits.Record("uygulama:Spotify", "Spotify", tuesday.AddDays(offset));
        }

        habits.Record("uygulama:Spotify", "Spotify", tuesday.AddHours(-2));
        Assert.Null(habits.Suggest(tuesday));
    }

    [Fact]
    public void WeekdayHabitsAreNotSuggestedOnWeekends()
    {
        using var temp = new TempDirectory();
        var habits = new HabitTracker(Path.Combine(temp.Path, "habits.json"));
        var saturday = new DateTime(2026, 11, 21, 9, 0, 0);
        foreach (var offset in new[] { -5, -4, -3 })
        {
            habits.Record("rutin:çalışma modu", "çalışma modu", saturday.AddDays(offset));
        }

        Assert.Null(habits.Suggest(saturday));
    }
}

public class RoutineScheduleTests
{
    [Theory]
    [InlineData("09:00 haftaici", "Hafta içi her gün 09:00")]
    [InlineData("18.30", "Her gün 18:30")]
    [InlineData("10:00 haftasonu", "Hafta sonu 10:00")]
    [InlineData("acilis", "Friday açılınca")]
    public void ParsesAndDescribes(string text, string description) => Assert.Equal(description, RoutineSchedule.Parse(text)?.Describe());

    [Fact]
    public void RejectsNonsense() => Assert.Null(RoutineSchedule.Parse("sabah erken"));

    [Fact]
    public void RunsOncePerDayInsideItsWindow()
    {
        var schedule = RoutineSchedule.Parse("09:00 haftaici")!;
        var monday = new DateTime(2026, 11, 16, 9, 1, 0);
        Assert.True(schedule.IsDue(monday, null));
        Assert.False(schedule.IsDue(monday, monday.AddSeconds(-20)));
        Assert.True(schedule.IsDue(monday, monday.AddDays(-1)));
        Assert.False(schedule.IsDue(monday.AddMinutes(10), null));
        Assert.False(schedule.IsDue(new DateTime(2026, 11, 21, 9, 1, 0), null));
        Assert.Equal("09:00 haftaici", schedule.Format());
    }
}

public class WorkspaceAndStepTests
{
    [Theory]
    [InlineData("kodlama masasını kaydet", "kaydet", "kodlama")]
    [InlineData("masamı kaydet", "kaydet", "masam")]
    [InlineData("bu düzeni oyun olarak kaydet", "kaydet", "oyun")]
    [InlineData("kodlama masasını kur", "kur", "kodlama")]
    [InlineData("çalışma masamı aç", "kur", "çalışma")]
    [InlineData("oyun masasını sil", "sil", "oyun")]
    public void RecognisesWorkspaceCommands(string sentence, string action, string name)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("masa_duzeni", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(name, command.Arguments["ad"]);
    }

    [Fact]
    public void ListsWorkspaces() => Assert.Equal("listele", QuickIntents.Match("masalarım")?.Arguments["islem"]);

    [Fact]
    public void DesktopStillMeansTheDesktop() => Assert.NotEqual("masa_duzeni", QuickIntents.Match("masaüstünü göster")?.Tool);

    [Theory]
    [InlineData("kapat:Discord", "Discord uygulamasını kapat")]
    [InlineData("dosya:indirilenler", "indirilenler aç")]
    [InlineData("pencere:sol", "Pencereyi sola yasla")]
    [InlineData("masa:kodlama", "\"kodlama\" masa düzenini kur")]
    public void NewStepsRoundTrip(string text, string description)
    {
        var step = RoutineStep.Parse(text);
        Assert.Equal(description, step.Describe());
        Assert.Equal(text, step.Format());
    }
}

public class ScreenTimeTests
{
    [Theory]
    [InlineData("chrome", "Google Chrome", "lofi hip hop - YouTube - Google Chrome", "YouTube")]
    [InlineData("msedge", null, "Yeni sekme - Microsoft Edge", "Edge")]
    [InlineData("Code", "Visual Studio Code", "Program.cs - Friday", "Visual Studio Code")]
    [InlineData("spotify", null, "Spotify Premium", "Spotify")]
    public void LabelsAppsAndKnownSites(string process, string? description, string title, string label) =>
        Assert.Equal(label, ScreenTimeLog.Label(process, description, title));

    [Fact]
    public void SumsAndDescribesTheDay()
    {
        var log = new ScreenTimeLog();
        var now = new DateTime(2026, 11, 18, 15, 0, 0);
        log.Add(now, "Visual Studio Code", 7800);
        log.Add(now, "YouTube", 2700);
        log.Add(now.AddDays(-1), "Steam", 600);
        Assert.Equal("Visual Studio Code", log.Day(now)[0].App);
        Assert.Equal("Bugün 2 saat 55 dakika bilgisayar başındaydın. En çok: Visual Studio Code (2 saat 10 dakika), YouTube (45 dakika).", log.Describe(now, "bugün"));
        Assert.Equal("Bugün YouTube için 45 dakika harcadın.", log.Describe(now, "bugün", "youtube"));
        Assert.Equal("Steam", ScreenTimeLog.FromJson(log.ToJson()).Day(now.AddDays(-1))[0].App);
        Assert.True(ScreenTimeLog.IsDistraction("YouTube"));
        Assert.False(ScreenTimeLog.IsDistraction("Visual Studio Code"));
    }

    [Theory]
    [InlineData("bugün ne kadar bilgisayardaydım", "bugun", null)]
    [InlineData("ekran sürem", "bugun", null)]
    [InlineData("dün en çok ne kullandım", "dun", null)]
    [InlineData("bugün ne kadar youtube izledim", "bugun", "youtube")]
    public void RecognisesScreenTimeQuestions(string sentence, string day, string? app)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("ekran_suresi", command?.Tool);
        Assert.Equal(day, command!.Arguments["gun"]);
        Assert.Equal(app ?? "", command.Arguments.TryGetValue("uygulama", out var value) ? value : "");
    }
}
