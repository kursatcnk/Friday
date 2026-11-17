using Friday.Core;

namespace Friday.UnitTests;

public class QuickIntentTests
{
    private static readonly string[] Routines = ["çalışma modu", "günaydın"];

    [Theory]
    [InlineData("YouTube'da lofi hip hop aç.", "lofi hip hop")]
    [InlineData("Friday, YouTube'dan Tarkan Kuzu Kuzu oynat", "tarkan kuzu kuzu")]
    [InlineData("Tarkan'ın Kuzu Kuzu klibini aç.", "tarkan'ın kuzu kuzu")]
    [InlineData("Barış Manço Gülpembe şarkısının klibini açar mısın?", "barış manço gülpembe")]
    [InlineData("komik kedi videosu aç", "komik kedi")]
    [InlineData("YouTube'da lütfen sakin piyano müziği açsana", "sakin piyano müziği")]
    // Kullanıcının gerçekten söylediği cümleler:
    [InlineData("İyiyim teşekkürler. Youtube'dan Power to Resist şarkısını dinlemek istiyorum.", "power to resist")]
    [InlineData("Bana YouTube'dan Naruto Kokuten şarkısını aç.", "naruto kokuten")]
    [InlineData("Naruto Kokuten şarkısını çal", "naruto kokuten")]
    [InlineData("Power to Resist'i YouTube'dan aç", "power to resist'i")]
    [InlineData("YouTube'da Dragon Ball müziği izlemek istiyorum", "dragon ball müziği")]
    [InlineData("Hey Jarvis, YouTube'dan Tarkan aç", "tarkan")]
    public void RecognizesClearVideoCommands(string sentence, string query)
    {
        var command = QuickIntents.Match(sentence);
        Assert.NotNull(command);
        Assert.Equal("video_ac", command.Tool);
        Assert.Equal(query, command.Arguments["arama"]);
    }

    [Theory]
    [InlineData("Saat kaç?", "saat")]
    [InlineData("Friday, şu an saat kaç", "saat")]
    [InlineData("Saati söyler misin?", "saat")]
    [InlineData("Bugün ayın kaçı?", "tarih")]
    [InlineData("Bugün günlerden ne?", "tarih")]
    public void AnswersClockQuestionsWithoutTheModel(string sentence, string focus)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("saat_ve_tarih", command?.Tool);
        Assert.Equal(focus, command!.Arguments["odak"]);
    }

    [Theory]
    [InlineData("Hava nasıl?", "", "bugun", "")]
    [InlineData("Bugün hava nasıl olacak?", "", "bugun", "")]
    [InlineData("Yarın hava nasıl?", "", "yarin", "")]
    [InlineData("Ankara'da hava nasıl?", "ankara", "bugun", "")]
    [InlineData("Yarın İzmir'de hava nasıl olacak?", "izmir", "yarin", "")]
    [InlineData("Bu hafta Kars'ta hava durumu", "kars", "hafta", "")]
    [InlineData("Dışarıda hava nasıl?", "", "bugun", "")]
    [InlineData("Yarın yağmur yağacak mı?", "", "yarin", "yagmur")]
    [InlineData("Hafta sonu hava nasıl", "", "haftasonu", "")]
    public void RecognizesWeatherQuestions(string sentence, string city, string focus, string question)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("hava_durumu", command?.Tool);
        Assert.Equal(city, command!.Arguments.GetValueOrDefault("sehir", ""));
        Assert.Equal(focus, command.Arguments["odak"]);
        Assert.Equal(question, command.Arguments.GetValueOrDefault("soru", ""));
    }

    [Theory]
    [InlineData("Dolar kaç?", "dolar")]
    [InlineData("Dolar kaç lira?", "dolar")]
    [InlineData("Euro ne kadar?", "euro")]
    [InlineData("Bugün dolar ve euro kaç?", "dolar ve euro")]
    [InlineData("Sterlin kuru", "sterlin")]
    public void RecognizesCurrencyQuestions(string sentence, string currency)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("doviz", command?.Tool);
        Assert.Equal(currency, command!.Arguments["birim"]);
    }

    [Theory]
    [InlineData("Müziği durdur", "oynat_duraklat")]
    [InlineData("Durdur.", "oynat_duraklat")]
    [InlineData("Müziği devam ettir", "oynat_duraklat")]
    [InlineData("Sonraki şarkı", "sonraki")]
    [InlineData("Şarkıyı geç", "sonraki")]
    [InlineData("Önceki şarkıya dön", "onceki")]
    [InlineData("Sesi açar mısın?", "ses_ac")]
    [InlineData("Sesi biraz kıs", "ses_kis")]
    [InlineData("Sessize al", "sessiz")]
    [InlineData("Bu şarkı ne?", "ne_caliyor")]
    [InlineData("Şu an ne çalıyor?", "ne_caliyor")]
    public void RecognizesMediaCommands(string sentence, string action)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("medya", command?.Tool);
        Assert.Equal(action, command!.Arguments["komut"]);
    }

    [Theory]
    [InlineData("10 dakika sonra beni uyar", "10", "")]
    [InlineData("Beş dakika sonra çamaşırı hatırlat", "5", "çamaşırı")]
    [InlineData("Yarım saat sonra toplantıyı hatırlat bana", "30", "toplantıyı")]
    [InlineData("On beş dakikalık zamanlayıcı kur", "15", "")]
    [InlineData("30 saniye sonra haber ver", "0.5", "")]
    [InlineData("Bir buçuk saat sonra fırını hatırlat", "90", "fırını")]
    public void RecognizesTimers(string sentence, string minutes, string note)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("zamanlayici", command?.Tool);
        Assert.Equal(minutes, command!.Arguments["dakika"]);
        Assert.Equal(note, command.Arguments.GetValueOrDefault("not", ""));
    }

    [Theory]
    [InlineData("Çalışma modunu aç", "çalışma modu")]
    [InlineData("Çalışma modunu başlat", "çalışma modu")]
    [InlineData("Günaydın", "günaydın")]
    public void RecognizesRoutines(string sentence, string routine)
    {
        var command = QuickIntents.Match(sentence, Routines);
        Assert.Equal("rutin", command?.Tool);
        Assert.Equal(routine, command!.Arguments["ad"]);
    }

    [Theory]
    [InlineData("Mola ver", "mola", "")]
    [InlineData("Mola vermek istiyorum", "mola", "")]
    [InlineData("Molaya çıkıyorum", "mola", "")]
    [InlineData("25 dakika odaklanacağım", "odak", "25")]
    [InlineData("Elli dakikalık odak modunu aç", "odak", "50")]
    [InlineData("Odak modu", "odak", "25")]
    [InlineData("Pomodoro başlat", "odak", "25")]
    [InlineData("Odağı bitir", "odak_bitir", "")]
    [InlineData("Molayı ertele", "ertele", "15")]
    [InlineData("On dakika ertele", "ertele", "10")]
    [InlineData("Nefes egzersizi yapalım", "nefes", "")]
    [InlineData("Esneme yapalım", "esneme", "")]
    [InlineData("Su içtim", "su", "")]
    [InlineData("Bir bardak su içtim", "su", "")]
    [InlineData("Bugün ne kadar çalıştım?", "durum", "")]
    [InlineData("Nerede kalmıştım?", "kaldigim_yer", "")]
    public void RecognizesRhythmCommands(string sentence, string action, string minutes)
    {
        var command = QuickIntents.Match(sentence, ["mola"]);
        Assert.Equal("ritim", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(minutes, command.Arguments.GetValueOrDefault("dakika", ""));
    }

    [Theory]
    [InlineData("Not defterini aç", "not defteri")]
    [InlineData("Spotify'ı aç", "spotify")]
    [InlineData("Hesap makinesini açar mısın", "hesap makinesi")]
    [InlineData("Chrome'u başlat", "chrome")]
    public void TreatsOpenRequestsAsTentativeApps(string sentence, string app)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("uygulama_ac", command?.Tool);
        Assert.True(command!.Tentative);
        Assert.Equal(app, command.Arguments["ad"]);
    }

    [Theory]
    [InlineData("Bu videoda ne anlatılıyor?")]
    [InlineData("Video izlemek istiyorum ama hangisini bilmiyorum")]
    [InlineData("Bana YouTube'dan")]
    [InlineData("Saat kaçta toplantım var?")]
    [InlineData("Dolar neden yükseliyor?")]
    [InlineData("Hava kirliliği hakkında bilgi ver")]
    [InlineData("Merhaba Friday, nasılsın?")]
    [InlineData("Bana bir fıkra anlat")]
    [InlineData("Ekranımda ne var?")]
    [InlineData("Unutma, arabamın muayenesi 20 Kasım'da")]
    public void LeavesEverythingElseToTheModel(string sentence) =>
        Assert.Null(QuickIntents.Match(sentence, Routines));

    [Theory]
    [InlineData("beş", 5)]
    [InlineData("on beş", 15)]
    [InlineData("yirmi", 20)]
    [InlineData("yarım", 0.5)]
    [InlineData("bir buçuk", 1.5)]
    [InlineData("2 buçuk", 2.5)]
    [InlineData("45", 45)]
    public void ParsesSpokenNumbers(string text, double expected) =>
        Assert.Equal(expected, QuickIntents.ParseNumber(text));

    [Theory]
    [InlineData("merhaba", true)]
    [InlineData("Selam Friday, nasılsın?", true)]
    [InlineData("Teşekkürler.", true)]
    [InlineData("Bana bir espri yap", true)]
    [InlineData("Bana kısa bir espri yap.", true)]
    [InlineData("Benim hakkımda ne biliyorsun?", true)]
    [InlineData("Bir fıkra anlat ve not defterini aç", false)]
    [InlineData("Merhaba, bugün hava nasıl?", false)]
    [InlineData("Selam, not defterini aç", false)]
    [InlineData("Teşekkürler, şimdi YouTube'dan Tarkan aç", false)]
    public void TellsSmallTalkFromRequests(string text, bool smallTalk) =>
        Assert.Equal(smallTalk, ConversationStyle.IsSmallTalk(text));
}

public class FillerTests
{
    [Theory]
    [InlineData("Başka bir şeye ihtiyacın var mı?", true)]
    [InlineData("Bugün sana nasıl yardımcı olabilirim?", true)]
    [InlineData("Size nasıl yardımcı olabilirim?", true)]
    [InlineData("Yardımcı olmak için buradayım.", true)]
    [InlineData("Ankara'da yarın yağmur yok.", false)]
    [InlineData("Ne yapmamı istersin?", false)]
    public void RecognizesFillerSentences(string sentence, bool filler) =>
        Assert.Equal(filler, ConversationStyle.IsFiller(sentence));

    [Fact]
    public void RemovesFillerButNeverEverything()
    {
        Assert.Equal("Rica ederim.", ConversationStyle.RemoveFiller("Rica ederim. Başka bir şeye ihtiyacın var mı?"));
        Assert.Equal("Size nasıl yardımcı olabilirim?", ConversationStyle.RemoveFiller("Size nasıl yardımcı olabilirim?"));
    }
}

public class TurkishGrammarTests
{
    [Theory]
    [InlineData("İstanbul", "İstanbul'da")]
    [InlineData("İzmir", "İzmir'de")]
    [InlineData("Kars", "Kars'ta")]
    [InlineData("Tokat", "Tokat'ta")]
    [InlineData("Eskişehir", "Eskişehir'de")]
    [InlineData("Ankara", "Ankara'da")]
    public void AddsTheLocativeSuffix(string city, string expected) =>
        Assert.Equal(expected, TurkishGrammar.Locative(city));

    [Theory]
    [InlineData(0.5, "30 saniye")]
    [InlineData(10, "10 dakika")]
    [InlineData(60, "1 saat")]
    [InlineData(90, "1 saat 30 dakika")]
    public void ReadsDurations(double minutes, string expected) =>
        Assert.Equal(expected, TurkishGrammar.Duration(minutes));

    [Fact]
    public void JoinsListsTheTurkishWay()
    {
        Assert.Equal("Cuma", TurkishGrammar.Join(["Cuma"]));
        Assert.Equal("Perşembe ve Cuma", TurkishGrammar.Join(["Perşembe", "Cuma"]));
        Assert.Equal("Salı, Perşembe ve Cuma", TurkishGrammar.Join(["Salı", "Perşembe", "Cuma"]));
    }
}
