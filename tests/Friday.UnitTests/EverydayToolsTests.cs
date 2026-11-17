using Friday.Core;

namespace Friday.UnitTests;

public class TurkishTimeTests
{
    // 17 Kasım 2026 Salı, 14:00
    private static readonly DateTime Now = new(2026, 11, 17, 14, 0, 0);

    [Theory]
    [InlineData("yarın 9'da", "2026-11-18 09:00")]
    [InlineData("yarın sabah", "2026-11-18 09:00")]
    [InlineData("akşam 8'de", "2026-11-17 20:00")]
    [InlineData("saat 15:30'da", "2026-11-17 15:30")]
    [InlineData("saat 3'te", "2026-11-17 15:00")]
    [InlineData("öğleden sonra 4'te", "2026-11-17 16:00")]
    [InlineData("10'da", "2026-11-17 22:00")]
    [InlineData("saat 13'te", "2026-11-18 13:00")]
    [InlineData("pazartesi 10:30'da", "2026-11-23 10:30")]
    [InlineData("cuma akşam", "2026-11-20 20:00")]
    [InlineData("salı 9'da", "2026-11-24 09:00")]
    [InlineData("gece 11'de", "2026-11-17 23:00")]
    [InlineData("20 dakika sonra", "2026-11-17 14:20")]
    [InlineData("iki saat sonra", "2026-11-17 16:00")]
    public void UnderstandsSpokenTimes(string phrase, string expected) =>
        Assert.Equal(expected, TurkishTime.Parse(phrase, Now)?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void DescribesTimesRelativeToToday()
    {
        Assert.Equal("bugün 15:30", TurkishTime.Describe(Now.AddMinutes(90), Now));
        Assert.Equal("yarın 09:00", TurkishTime.Describe(new DateTime(2026, 11, 18, 9, 0, 0), Now));
        Assert.Equal("pazartesi 10:00", TurkishTime.Describe(new DateTime(2026, 11, 23, 10, 0, 0), Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("toplantı")]
    public void IgnoresPhrasesWithoutTime(string phrase) => Assert.Null(TurkishTime.Parse(phrase, Now));
}

public class EverydayIntentTests
{
    [Theory]
    [InlineData("Yarın 9'da doktoru hatırlat", "yarın 9'da", "doktoru")]
    [InlineData("Akşam 8'de ilacımı hatırlat bana", "akşam 8'de", "ilacımı")]
    [InlineData("Pazartesi 10:30'da toplantıyı hatırlat", "pazartesi 10:30'da", "toplantıyı")]
    [InlineData("Saat 3'te annemi aramamı hatırlatır mısın", "saat 3'te", "annemi aramamı")]
    public void RecognizesTimedReminders(string sentence, string when, string note)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("hatirlatici", command?.Tool);
        Assert.Equal(when, command!.Arguments["zaman"]);
        Assert.Equal(note, command.Arguments["metin"]);
    }

    [Fact]
    public void RelativeTimersStayTimers() => Assert.Equal("zamanlayici", QuickIntents.Match("10 dakika sonra beni uyar")?.Tool);

    [Theory]
    [InlineData("Not al: süt ve ekmek al", "ekle", "süt ve ekmek al")]
    [InlineData("Şunu not et, yarın bankaya uğra", "ekle", "yarın bankaya uğra")]
    [InlineData("Notlarımı oku", "oku", "")]
    [InlineData("Son notum neydi?", "oku", "")]
    public void RecognizesNotes(string sentence, string action, string text)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("not", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(text, command.Arguments.GetValueOrDefault("metin", ""));
    }

    [Theory]
    [InlineData("Hatırlatmalarım neler?", "listele")]
    [InlineData("Doktor hatırlatmasını iptal et", "sil")]
    public void ManagesReminders(string sentence, string action) =>
        Assert.Equal(action, QuickIntents.Match(sentence)?.Arguments["islem"]);

    [Theory]
    [InlineData("Sesi yüzde 30 yap", "ses_ayarla", "30")]
    [InlineData("Sesi 40'a ayarla", "ses_ayarla", "40")]
    [InlineData("Ses seviyesi kaç?", "ses_seviyesi", "")]
    public void RecognizesExactVolume(string sentence, string action, string value)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("medya", command?.Tool);
        Assert.Equal(action, command!.Arguments["komut"]);
        Assert.Equal(value, command.Arguments.GetValueOrDefault("deger", ""));
    }

    [Theory]
    [InlineData("345 çarpı 12 kaç?", "hesap")]
    [InlineData("Yılbaşına kaç gün kaldı?", "gun_sayaci")]
    [InlineData("Bu hafta ne kadar çalıştım?", "ritim")]
    public void RoutesEverydayQuestions(string sentence, string tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool);
}

public class SpokenMathTests
{
    [Theory]
    [InlineData("345 çarpı 12 kaç?", "345 çarpı 12, 4.140 eder.")]
    [InlineData("12 artı 7 eksi 3 kaç eder", "12 artı 7 eksi 3, 16 eder.")]
    [InlineData("2 artı 3 çarpı 4", "2 artı 3 çarpı 4, 14 eder.")]
    [InlineData("1500 bölü 4", "1500 bölü 4, 375 eder.")]
    [InlineData("7 bölü 0", "Sıfıra bölünemez.")]
    [InlineData("200'ün yüzde 15'i kaç", "200 sayısının yüzde 15'i 30 eder.")]
    [InlineData("5 mil kaç kilometre", "5 mil, 8,0467 kilometre eder.")]
    [InlineData("70 kilo kaç pound", "70 kilogram, 154,32 pound eder.")]
    [InlineData("30 derece kaç fahrenheit", "30 santigrat derece, 86 fahrenheit eder.")]
    public void CalculatesExactly(string question, string answer) => Assert.Equal(answer, SpokenMath.Answer(question));

    [Theory]
    [InlineData("Saat kaç?")]
    [InlineData("Dolar kaç lira?")]
    [InlineData("5 elma kaç armut")]
    public void IgnoresNonCalculations(string question) => Assert.Null(SpokenMath.Answer(question));
}

public class DayCountdownTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    [Theory]
    [InlineData("Yılbaşına kaç gün kaldı?", "Yılbaşı için 45 gün kaldı; 1 Ocak Cuma.")]
    [InlineData("29 Ekim'e kaç gün var", "29 ekim için 346 gün kaldı; 29 Ekim Cuma.")]
    [InlineData("Öğretmenler gününe kaç gün kaldı", "Öğretmenler günü için 7 gün kaldı; 24 Kasım Salı.")]
    public void CountsDownToKnownDays(string question, string answer) =>
        Assert.Equal(answer, DayCountdown.Answer(DayCountdown.Target(question)!, [], Today));

    [Fact]
    public void FindsTheUsersOwnDatesInMemory()
    {
        MemoryItem[] memories =
        [
            new("a", "Kullanıcının kızının doğum günü 3 Mart.", DateTimeOffset.Now),
            new("b", "Kullanıcının doğum günü 2 Aralık.", DateTimeOffset.Now)
        ];
        Assert.StartsWith("Doğum günüm için 15 gün kaldı", DayCountdown.Answer(DayCountdown.Target("Doğum günüme kaç gün kaldı?")!, memories, Today));
        Assert.StartsWith("Kızımın doğum günü için 106 gün kaldı", DayCountdown.Answer(DayCountdown.Target("Kızımın doğum gününe kaç gün kaldı?")!, memories, Today));
    }
}

public class ReminderAndNoteStoreTests
{
    [Fact]
    public void RemindersSurviveRestartAndFireOnce()
    {
        using var folder = new TempDirectory();
        var file = Path.Combine(folder.Path, "reminders.json");
        var now = new DateTime(2026, 11, 17, 14, 0, 0);
        var store = new ReminderStore(file);
        store.Add(now.AddMinutes(10), "çayı", now);
        store.Add(now.AddDays(1), "doktoru", now);

        var reopened = new ReminderStore(file);
        Assert.Equal(2, reopened.Upcoming.Count);
        Assert.Empty(reopened.TakeDue(now.AddMinutes(5)));
        var due = Assert.Single(reopened.TakeDue(now.AddMinutes(11)));
        Assert.Equal("Çayı unutma, zamanı geldi.", ReminderStore.Announcement(due));
        Assert.Empty(reopened.TakeDue(now.AddMinutes(12)));
        Assert.Equal(1, reopened.Cancel("doktor"));
        Assert.Empty(new ReminderStore(file).Upcoming);
    }

    [Fact]
    public void NotesAreAPlainMarkdownFile()
    {
        using var folder = new TempDirectory();
        var store = new NoteStore(Path.Combine(folder.Path, "notes.md"));
        store.Add("süt al", new DateTime(2026, 11, 17, 9, 5, 0));
        store.Add("bankaya uğra", new DateTime(2026, 11, 17, 10, 0, 0));
        Assert.Equal(["süt al", "bankaya uğra"], store.All.Select(note => note.Text));
        Assert.Contains("- [2026-11-17 09:05] süt al", File.ReadAllText(store.File), StringComparison.Ordinal);
    }
}
