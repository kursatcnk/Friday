using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// Konuşma hafızası: geçmişe dönen sorularda eski sohbetlerden doğru soru-cevabın bulunması. Geçmiş, birbirine benzeyen
/// konular içerir (iki kitap, iki film sohbeti; "yanına ne yapsam" gibi konusu bir önceki soruda kalan devam soruları).
/// </summary>
public class ConversationRecallTests
{
    // 21 Kasım 2026 cumartesi.
    private static readonly DateTime Now = new(2026, 11, 21, 18, 0, 0);

    private static readonly (string Key, PastTurn Turn)[] History =
    [
        ("film-uyku", Turn(2026, 11, 3, 21, 0, "Sinema", "Film izlerken uyuyakalıyorum, neden?", "Akşam yorgunluğu ve karanlık oda melatonini artırır; filmi daha erken saatte izlemeyi dene.")),
        ("kira", Turn(2026, 11, 5, 16, 0, "Kira artışı", "Kira artışı yasal olarak ne kadar olabilir?", "Konut kiralarında artış, bir önceki yılın on iki aylık TÜFE ortalamasını geçemez; sözleşmende daha düşük bir oran varsa o geçerli.")),
        ("kahve", Turn(2026, 11, 8, 12, 0, "Kahve makinesi", "Kahve makinesi önerir misin?", "Filtre kahve seviyorsan Fakir Aroma iyi bir başlangıç; espresso istersen De'Longhi Dedica'ya bak.")),
        ("corba", Turn(2026, 11, 10, 19, 0, "Mercimek çorbası", "Mercimek çorbası tarifi verir misin?", "Bir su bardağı kırmızı mercimek, bir soğan, bir havuç, bir yemek kaşığı salça ve altı su bardağı suyla 25 dakika pişir, sonra blenderdan geçir.")),
        ("yanina", Turn(2026, 11, 10, 19, 6, "Mercimek çorbası", "Yanına ne yapsam?", "Yanına kıtır ekmek ve limonlu bir roka salatası çok iyi gider.")),
        ("okuma", Turn(2026, 11, 11, 10, 0, "Okuma alışkanlığı", "Kitap okuma alışkanlığı nasıl kazanılır?", "Her gün aynı saatte yirmi sayfa okumakla başla; telefonu başka odaya bırak.")),
        ("dizi", Turn(2026, 11, 12, 20, 0, "İngilizce pratiği", "İngilizce pratiği için hangi diziyi izleyeyim?", "Friends ile başla; günlük dil ve net telaffuz var. Sonra The Office'e geçebilirsin.")),
        ("fikra", Turn(2026, 11, 13, 18, 0, "Fıkra", "Bir fıkra anlat", "Temel bir gün Dursun'a sormuş: ...")),
        ("tatil", Turn(2026, 11, 14, 10, 0, "Antalya tatili", "Antalya tatili için ne kadar bütçe ayırmalıyım?", "Dört gecelik bir Antalya tatili için konaklama, uçak ve yemekle kişi başı yaklaşık 25 bin lira ayırmanı öneririm.")),
        ("ulasim", Turn(2026, 11, 14, 10, 5, "Antalya tatili", "Uçak bileti mi otobüs mü daha mantıklı?", "İstanbul–Antalya için erken alınmış uçak bileti hem süre hem fiyat olarak daha mantıklı; otobüs 11 saat sürüyor.")),
        ("lastik", Turn(2026, 11, 15, 13, 0, "Araba lastikleri", "Arabanın lastikleri ne zaman değişmeli?", "Diş derinliği 3 milimetrenin altına inince ya da lastikler 5 yaşını geçince değiştirmelisin; kış lastiğine aralıkta geçmen iyi olur.")),
        ("python", Turn(2026, 11, 16, 10, 0, "Python mu C# mı", "Python mu öğreneyim C# mı?", "Masaüstü uygulaması yapmak istiyorsan C#, veri analizi ve otomasyon istiyorsan Python ile başla.")),
        ("laptop", Turn(2026, 11, 17, 11, 0, "Yeni laptop", "Yeni laptop almak istiyorum, 30 bin liraya ne önerirsin?", "Bu bütçede Lenovo IdeaPad Slim 5 ya da Asus Vivobook 15 OLED mantıklı; 16 GB bellek ve 512 GB SSD'den aşağısını alma.")),
        ("piyano", Turn(2026, 11, 17, 11, 10, "Yeni laptop", "Ekinsu'nun piyano dersi için hangi saat uygun?", "Okul çıkışı yorgun olacağı için cumartesi sabahı 10 gibi bir saat daha verimli olur.")),
        ("saat", Turn(2026, 11, 18, 8, 0, "Saat", "Saat kaç?", "Saat sekiz.")),
        ("kitap", Turn(2026, 11, 18, 21, 10, "Kitap önerisi", "Bana bilim kurgu bir kitap önerir misin?", "Frank Herbert'in Dune'unu öneririm; çöl gezegeni Arrakis'te geçen, siyaset ve ekoloji dolu bir destan. Daha hafif bir şey istersen Andy Weir'ın Marslı'sı da çok keyifli.")),
        ("film", Turn(2026, 11, 18, 21, 20, "Kitap önerisi", "Hafta sonu için güzel bir film var mı?", "Interstellar'ı izlemediysen kesinlikle öneririm; Christopher Nolan'ın uzay ve zaman üzerine filmi.")),
        ("kredi", Turn(2026, 11, 19, 15, 0, "Kredi kartı", "Kredi kartı borcunu taksitlendirmek mantıklı mı?", "Faizi yüksek olduğu için ancak zorunluysa; mümkünse borcu ihtiyaç kredisiyle kapatmak daha ucuza gelebilir.")),
        ("uyku", Turn(2026, 11, 19, 22, 0, "Uyku", "Uykuya dalmakta zorlanıyorum, ne yapabilirim?", "Yatmadan bir saat önce ekranı bırak, odayı serin tut, 4-7-8 nefes tekniğini dene; kafeini öğleden sonra kes.")),
        ("dolar", Turn(2026, 11, 20, 8, 0, "Dolar", "Dolar ne kadar?", "Dolar 42,10 lira.")),
        ("bel", Turn(2026, 11, 20, 9, 30, "Bel ağrısı", "Bel ağrım için hangi egzersizleri yapabilirim?", "Kedi-deve esnemesi, köprü ve diz-göğüs esnemesi bel ağrısına iyi gelir; ağrı sürerse bir fizyoterapiste görünmelisin.")),
        ("mail", Turn(2026, 11, 20, 14, 0, "Sunum maili", "Ilgaz Bey'e sunum için nasıl bir mail yazayım?", "Kısa tut: sunumun ekte olduğunu, öne çıkan üç bulguyu ve cuma gününe kadar geri dönüş beklediğini yaz.")),
        ("hava", Turn(2026, 11, 21, 9, 0, "Hava", "Bugün hava nasıl?", "İstanbul'da bugün 14 derece, parçalı bulutlu.")),
        ("gezi", Turn(2026, 11, 21, 9, 5, "Hava", "Doruk'un okul gezisi için ne hazırlamalıyım?", "Yanına su şişesi, atıştırmalık, yağmurluk ve okul kartını koy; isim etiketini unutma.")),
        ("hediye", Turn(2026, 10, 25, 18, 0, "Annemin doğum günü", "Annemin doğum günü için hediye fikri?", "Ayşe Hanım bahçeyle uğraşıyorsa güzel bir saksı seti ya da bitki yetiştirme kiti; kitap seviyorsa Elif Şafak'ın son romanı.")),
    ];

    private static IReadOnlyList<PastTurn> Turns => [.. History.Select(item => item.Turn).OrderBy(turn => turn.Time)];

    public static TheoryData<string, string> Questions => new()
    {
        { "Geçen hafta bana bir kitap önermiştin, neydi?", "kitap" },
        { "Hangi filmi önermiştin hatırlıyor musun?", "film" },
        { "Tatil için ne kadar bütçe demiştin?", "tatil" },
        { "Antalya'ya uçakla mı otobüsle mi gitmeyi önermiştin?", "ulasim" },
        { "Bel ağrısı için hangi hareketleri söylemiştin?", "bel" },
        { "Ilgaz Bey'e yazacağım mail hakkında ne demiştin?", "mail" },
        { "Mercimek çorbasının tarifini vermiştin, tekrar söyler misin?", "corba" },
        { "Çorbanın yanına ne önermiştin?", "yanina" },
        { "Laptop için hangi modelleri önermiştin?", "laptop" },
        { "Ekinsu'nun piyano dersi saatini konuşmuştuk, hangi saat demiştin?", "piyano" },
        { "Uyku sorunum için ne demiştin?", "uyku" },
        { "Kira artışı hakkında ne söylemiştin?", "kira" },
        { "Doruk'un gezisi için ne hazırlamamı söylemiştin?", "gezi" },
        { "İngilizce için hangi diziyi önermiştin?", "dizi" },
        { "Lastikleri ne zaman değiştirmem gerektiğini konuşmuştuk, neydi?", "lastik" },
        { "Geçen ay kahve makinesi önermiştin, hangisiydi?", "kahve" },
        { "Annemin doğum günü için hediye fikri vermiştin, neydi?", "hediye" },
        { "Python mu C# mı demiştin?", "python" },
        { "Kredi kartı borcu için ne tavsiye etmiştin?", "kredi" },
        { "Dün bel ağrısı hakkında konuştuğumuz şeyi hatırlıyor musun?", "bel" },
    };

    [Theory]
    [MemberData(nameof(Questions))]
    public void FindsTheConversationAmongTheFirstFew(string question, string key)
    {
        Assert.True(ConversationRecall.RefersToPast(question), question);
        var found = ConversationRecall.Find(question, Turns, Now);
        Assert.Contains(History.Single(item => item.Key == key).Turn, found);
    }

    [Fact]
    public void UsuallyPutsTheRightConversationFirst()
    {
        // Ölçüm: 20 sorudan kaçında doğru konuşma ilk sırada. Eski arama (sözcüğün aynen geçmesi) için bkz. sürüm notları.
        var first = Questions.Count(row => ConversationRecall.Find((string)row[0], Turns, Now) is [var best, ..]
                                           && best == History.Single(item => item.Key == (string)row[1]).Turn);
        Assert.True(first >= 19, $"{first}/20");
    }

    [Theory]
    [InlineData("Bana bir kitap önerir misin?")]
    [InlineData("Hava nasıl?")]
    [InlineData("Dune kimin kitabı?")]
    [InlineData("Bugün ne yapmalıyım?")]
    [InlineData("Uçak bileti nasıl alınır?")]
    [InlineData("Kira artışı ne kadar olabilir?")]
    [InlineData("Bir fıkra anlat")]
    public void LeavesNewQuestionsAlone(string question)
    {
        Assert.False(ConversationRecall.RefersToPast(question), question);
        Assert.Equal("", ConversationRecall.Context(question, Turns, Now));
    }

    [Fact]
    public void ATimeWordNarrowsTheSearch()
    {
        // İki kitap sohbeti var; "çarşamba günü" 18 Kasım'dakini, "geçen ay" ise yalnızca o aralıktakileri seçer.
        Assert.Equal(Key("kitap"), ConversationRecall.Find("Çarşamba günü kitap hakkında ne konuşmuştuk?", Turns, Now)[0]);
        Assert.Equal(Key("okuma"), ConversationRecall.Find("11 Kasım'da kitap okuma hakkında ne demiştin?", Turns, Now)[0]);
        Assert.Equal((Now.Date.AddDays(-1), Now.Date), ConversationRecall.Window("dün akşam ne konuşmuştuk", Now));
        Assert.Equal((Now.Date.AddDays(-3), Now.Date.AddDays(-2)), ConversationRecall.Window("çarşamba ne demiştin", Now));
        Assert.Equal((Now.Date.AddDays(-7), Now.Date.AddDays(-6)), ConversationRecall.Window("cumartesi ne konuşmuştuk", Now));
        Assert.Null(ConversationRecall.Window("kitap önermiştin", Now));
    }

    [Fact]
    public void FallsBackToAllHistoryWhenTheDayIsMisremembered()
    {
        // Laptop 17 Kasım'da konuşuldu; kullanıcı "dün" diyor. Yine bulunur, cevaba gerçek tarihi yazılır.
        var found = ConversationRecall.Find("Dün laptop için ne önermiştin?", Turns, Now);
        Assert.Equal(Key("laptop"), found[0]);
        var context = ConversationRecall.Context("Dün laptop için ne önermiştin?", Turns, Now);
        Assert.Contains("4 gün önce", context, StringComparison.Ordinal);
        Assert.Contains("kısaca düzelt", context, StringComparison.Ordinal);
        Assert.DoesNotContain("düzelt", ConversationRecall.Context("Geçen hafta bana bir kitap önermiştin, neydi?", Turns, Now), StringComparison.Ordinal);
    }

    [Fact]
    public void ListsTheTopicsOfADayWhenNoSubjectIsGiven()
    {
        var context = ConversationRecall.Context("Dün ne konuşmuştuk?", Turns, Now);
        Assert.Contains("Bel ağrısı", context, StringComparison.Ordinal);
        Assert.Contains("Sunum maili", context, StringComparison.Ordinal);
        Assert.DoesNotContain("Kitap önerisi", context, StringComparison.Ordinal);
    }

    [Fact]
    public void TheContextCarriesWhatWasActuallySaid()
    {
        var context = ConversationRecall.Context("Geçen hafta bana bir kitap önermiştin, neydi?", Turns, Now);
        Assert.StartsWith("\n[Önceki sohbetlerinden", context, StringComparison.Ordinal);
        Assert.Contains("Dune", context, StringComparison.Ordinal);
        Assert.Contains("3 gün önce", context, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongAnswerKeepsTheSentencesAboutTheSubject()
    {
        var answer = "Güzel soru. " + string.Concat(Enumerable.Repeat("Bu arada başka şeyler de anlatayım, uzun bir giriş yapıyorum. ", 6))
                     + "Kitap olarak Dune'u öneririm. Sonra yine başka konular.";
        var excerpt = ConversationRecall.Excerpt(answer, ConversationRecall.Topic("kitap"));
        Assert.StartsWith("Güzel soru.", excerpt, StringComparison.Ordinal);
        Assert.Contains("Dune", excerpt, StringComparison.Ordinal);
        Assert.True(excerpt.Length <= 265, excerpt);
    }

    [Theory]
    [InlineData("geçen hafta ne konuşmuştuk")]
    [InlineData("bana önerdiğin kitap neydi")]
    [InlineData("geçen sefer ne demiştin")]
    [InlineData("tatil için yaptığımız planı hatırlıyor musun")]
    [InlineData("geçen gün hesaplamıştık ya")]
    [InlineData("bana söylediğin yerin adı neydi")]
    public void HearsAReferenceToThePast(string question) => Assert.True(ConversationRecall.RefersToPast(question), question);

    [Fact]
    public void SearchFindsInflectedWordsInAnyOrder()
    {
        // Eski arama "kitap önerisi"ni ancak bu iki sözcük yan yana geçerse buluyordu.
        Assert.Equal(Key("kitap"), ConversationRecall.Search("kitap önerisi", Turns)[0]);
        Assert.Equal(Key("lastik"), ConversationRecall.Search("lastik değişimi", Turns)[0]);
        Assert.Empty(ConversationRecall.Search("deprem", Turns));
    }

    private static PastTurn Key(string key) => History.Single(item => item.Key == key).Turn;

    private static PastTurn Turn(int year, int month, int day, int hour, int minute, string chat, string user, string assistant) =>
        new(new DateTime(year, month, day, hour, minute, 0), user, assistant, chat);
}
