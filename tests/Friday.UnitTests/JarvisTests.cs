using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class CustomCommandTests
{
    [Theory]
    [InlineData("Baha dediğimde 'Baha abinin umrunda değilsin' de", "baha", "soyle:Baha abinin umrunda değilsin")]
    [InlineData("Hey Friday, Baha ismini duyduğum anda, Baha abi geldi diye söyle.", "baha", "soyle:Baha abi geldi")]
    [InlineData("ders çalışıyorum deyince Spotify'ı aç", "ders çalışıyorum", "uygulama:Spotify")]
    [InlineData("haber dersem bbc.com'u aç", "haber", "site:bbc.com")]
    [InlineData("Günaydın dediğimde \"Günaydın patron, kahve hazır mı?\" diye cevap ver", "günaydın", "soyle:Günaydın patron, kahve hazır mı?")]
    public void ParsesCustomCommands(string text, string phrase, string step)
    {
        var command = QuickIntents.CustomCommand(text);
        Assert.NotNull(command);
        Assert.Equal(phrase, command.Value.Phrase);
        Assert.Equal(step, command.Value.Step);
    }

    [Theory]
    [InlineData("Merhaba deyince ne diyorsun")]
    [InlineData("Yarın hava nasıl?")]
    [InlineData("annem gelince haber ver")]
    public void IgnoresOrdinarySentences(string text) => Assert.Null(QuickIntents.CustomCommand(text));

    [Fact]
    public void CustomCommandWinsOverItsOwnTrigger()
    {
        var command = QuickIntents.Match("Baha dediğimde selam de", triggers: ["baha"]);
        Assert.Equal("ozel_komut", command?.Tool);
    }

    [Theory]
    [InlineData("Baha geldi", true)]
    [InlineData("Baha'yı gördün mü", true)]
    [InlineData("bahayı aradın mı", true)]
    [InlineData("bahar geldi", false)]
    [InlineData("Kebabahane", false)]
    public void TriggersMatchWholeWordsWithSuffixes(string sentence, bool expected)
    {
        var command = QuickIntents.Match(sentence, triggers: ["baha"]);
        Assert.Equal(expected, command?.Tool == "tetik");
    }
}

public class JarvisIntentTests
{
    [Theory]
    [InlineData("Durum raporu", "durum_raporu", null, null)]
    [InlineData("her şey yolunda mı", "durum_raporu", null, null)]
    [InlineData("Bilgisayarı kilitle", "sistem", "islem", "kilitle")]
    [InlineData("uyku moduna al", "sistem", "islem", "uyku")]
    [InlineData("ekranı kapat", "sistem", "islem", "ekran_kapat")]
    [InlineData("masaüstünü göster", "sistem", "islem", "masaustu")]
    [InlineData("karanlık moda geç", "sistem", "islem", "tema_koyu")]
    [InlineData("açık temaya geç", "sistem", "islem", "tema_acik")]
    [InlineData("ekran görüntüsü al", "sistem", "islem", "ekran_goruntusu")]
    [InlineData("kapatmayı iptal et", "sistem", "islem", "kapat_iptal")]
    [InlineData("indirme bitince haber ver", "nobet", "islem", "indirme")]
    [InlineData("render bitince haber ver", "nobet", "islem", "bosta")]
    [InlineData("internet gelince söyle", "nobet", "islem", "internet")]
    [InlineData("neyi bekliyorsun", "nobet", "islem", "liste")]
    [InlineData("nöbeti iptal et", "nobet", "islem", "iptal")]
    [InlineData("yazı tura at", "sans", "islem", "yazitura")]
    [InlineData("iki zar at", "sans", "adet", "2")]
    [InlineData("1 ile 100 arasında bir sayı tut", "sans", "en_cok", "100")]
    [InlineData("kronometreyi başlat", "kronometre", "islem", "baslat")]
    [InlineData("kronometre kaç oldu", "kronometre", "islem", "durum")]
    [InlineData("kronometreyi durdur", "kronometre", "islem", "durdur")]
    [InlineData("teşekkürler", "kapanis", null, null)]
    [InlineData("tamam bu kadar", "kapanis", null, null)]
    [InlineData("Sağ ol Friday", "kapanis", null, null)]
    public void RecognisesJarvisCommands(string sentence, string tool, string? key, string? value)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal(tool, command?.Tool);
        if (key is not null)
        {
            Assert.Equal(value, command!.Arguments[key]);
        }
    }

    [Theory]
    [InlineData("bilgisayarı kapat", "1")]
    [InlineData("30 dakika sonra bilgisayarı kapat", "30")]
    [InlineData("bir saat sonra bilgisayarı kapat", "60")]
    public void ShutdownIsAlwaysDelayed(string sentence, string minutes)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("kapat", command?.Arguments["islem"]);
        Assert.Equal(minutes, command!.Arguments["dakika"]);
    }

    [Fact]
    public void GpuWatchKeepsTheLimit()
    {
        var command = QuickIntents.Match("ekran kartı 85 dereceyi geçerse uyar");
        Assert.Equal("gpu", command?.Arguments["islem"]);
        Assert.Equal("85", command!.Arguments["derece"]);
    }

    [Theory]
    [InlineData("beni yarın 7'de uyandır", "yarın 7'de")]
    [InlineData("sabah 6:30'da uyandır", "sabah 6:30'da")]
    public void WakeUpBecomesAnAlarm(string sentence, string when)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("hatirlatici", command?.Tool);
        Assert.Equal(when, command!.Arguments["zaman"]);
        Assert.Equal(QuickIntents.AlarmText, command.Arguments["metin"]);
    }

    [Fact]
    public void AlarmIsAnnouncedAsAWakeUp()
    {
        var reminder = new Reminder("a", new DateTime(2026, 11, 19, 7, 0, 0), QuickIntents.AlarmText, DateTime.Now);
        Assert.Equal("Günaydın! Saat 07:00, uyanma vakti.", ReminderStore.Announcement(reminder));
    }

    [Fact]
    public void TenMinutesLaterWakeUpIsATimer() => Assert.Equal("zamanlayici", QuickIntents.Match("10 dakika sonra beni uyandır")?.Tool);

    [Fact]
    public void ProtocolsRunRoutines()
    {
        var command = QuickIntents.Match("gece protokolünü başlat", ["gece"]);
        Assert.Equal("rutin", command?.Tool);
        Assert.Equal("gece", command!.Arguments["ad"]);
    }

    [Fact]
    public void GoodNightRoutineIsNotAClosing() =>
        Assert.Equal("rutin", QuickIntents.Match("iyi geceler", new FridaySettings().Routines.Keys)?.Tool);
}

public class WatchLogicTests
{
    private static readonly DateTime Start = new(2026, 11, 18, 12, 0, 0);

    [Fact]
    public void IdleFiresOnlyAfterRealWork()
    {
        var detector = new IdleDetector();
        Assert.False(detector.Update(5, Start));
        Assert.False(detector.Update(5, Start.AddSeconds(40)));
        Assert.False(detector.SawWork);

        Assert.False(detector.Update(90, Start.AddMinutes(1)));
        Assert.False(detector.Update(8, Start.AddMinutes(2)));
        Assert.False(detector.Update(9, Start.AddMinutes(2).AddSeconds(20)));
        Assert.True(detector.Update(7, Start.AddMinutes(2).AddSeconds(31)));
    }

    [Fact]
    public void ABriefSpikeResetsTheQuietWindow()
    {
        var detector = new IdleDetector();
        detector.Update(80, Start);
        detector.Update(5, Start.AddSeconds(5));
        detector.Update(60, Start.AddSeconds(20));
        Assert.False(detector.Update(5, Start.AddSeconds(40)));
        Assert.True(detector.Update(5, Start.AddSeconds(71)));
    }

    [Theory]
    [InlineData("film.mkv.crdownload", true, "film.mkv")]
    [InlineData("kurulum.exe.part", true, "kurulum.exe")]
    [InlineData("rapor.pdf", false, "rapor.pdf")]
    public void RecognisesPartialDownloads(string file, bool partial, string final)
    {
        Assert.Equal(partial, DownloadFiles.IsPartial(file));
        Assert.Equal(final, DownloadFiles.FinalName(file));
    }

    [Fact]
    public void ParsesNvidiaSmi()
    {
        var reading = SystemProbe.Parse("NVIDIA GeForce RTX 5070 Ti, 54, 3\r\n");
        Assert.Equal(new GpuReading("NVIDIA GeForce RTX 5070 Ti", 54, 3), reading);
        Assert.Equal(new GpuReading("NVIDIA GeForce RTX 5070 Ti", 39, 0), SystemProbe.Parse("NVIDIA GeForce RTX 5070 Ti, 39.02, 0"));
        Assert.Null(SystemProbe.Parse("No devices were found"));
    }

    [Fact]
    public void CpuBusyComesFromIdleAndTotalTimes() =>
        Assert.Equal(25, SystemProbe.CpuBusy((100, 1000), (400, 1400)));

    [Fact]
    public void ChanceAndStopwatchTexts()
    {
        Assert.Matches("^(?:Yazı|Tura) geldi\\.$", Chance.CoinFlip(new Random(1)));
        Assert.Matches(@"^Zarlar \d ve \d geldi, toplam \d+\.$", Chance.Dice(new Random(2), 2));
        Assert.Equal("1 saat 3 dakika 12 saniye", SpokenDuration.Of(TimeSpan.FromSeconds(3792)));
        Assert.Equal("45 saniye", SpokenDuration.Of(TimeSpan.FromSeconds(45)));
        Assert.Equal("2 dakika", SpokenDuration.Of(TimeSpan.FromMinutes(2)));
    }
}

public class MemoryDuplicateTests
{
    [Theory]
    [InlineData("Baha ismini duyduğum anda, Baha abinin umrunda değilsin salak salak sorular sorma artık de.",
        "Baha ismini duyduğum anda, Baha abinin umrunda değilsin artık aptal aptal şeyler sorma aq de.", true)]
    [InlineData("Kullanıcının arabasının muayenesi 20 Kasım'da.", "Kullanıcının arabasının muayenesi 25 Kasım'da.", true)]
    [InlineData("Kullanıcının kızının doğum günü 3 Mart.", "Kullanıcının oğlunun doğum günü 5 Nisan.", false)]
    [InlineData("Kahveyi şekersiz içer.", "Çayı şekersiz içer.", false)]
    public void RecognisesTheSameFact(string first, string second, bool same) => Assert.Equal(same, MemoryStore.SameFact(first, second));

    [Fact]
    public void ACorrectedFactReplacesTheOldOne()
    {
        using var temp = new TempDirectory();
        var memory = new MemoryStore(Path.Combine(temp.Path, "memory.json"));
        memory.Remember("Kullanıcının arabasının muayenesi 20 Kasım'da.");
        memory.Remember("Kullanıcının arabasının muayenesi 25 Kasım'da.");
        Assert.Equal("Kullanıcının arabasının muayenesi 25 Kasım'da.", Assert.Single(memory.Items).Text);
    }
}

public class AddressTests
{
    [Fact]
    public void PromptMentionsTheChosenAddress()
    {
        var prompt = SystemPrompt.Build(new FridaySettings { AddressAs = "patron" }, [], DateTime.Now);
        Assert.Contains("\"patron\" diye hitap et", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("patron", SystemPrompt.Build(new FridaySettings(), [], DateTime.Now), StringComparison.Ordinal);
    }
}
