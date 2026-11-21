using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// Sık kullanılan komutların doğal söyleyişleri hızlı yolda (dil modeli beklenmeden) yakalanmalı; kaçan her söz
/// 1–3 sn daha yavaş cevaplanır.
/// </summary>
public class QuickCoverageTests
{
    [Theory]
    [InlineData("saat kaç oldu", "saat_ve_tarih")]
    [InlineData("bugün günlerden ne", "saat_ve_tarih")]
    [InlineData("dışarısı soğuk mu", "hava_durumu")]
    [InlineData("yarın yağmur yağacak mı", "hava_durumu")]
    [InlineData("euro ne kadar", "doviz")]
    [InlineData("100 dolar kaç lira", "doviz")]
    [InlineData("müziği durdur", "medya")]
    [InlineData("sesi biraz aç", "medya")]
    [InlineData("on dakika sonra haber ver", "zamanlayici")]
    [InlineData("yarın sabah dokuzda faturayı ödemeyi hatırlat", "hatirlatici")]
    [InlineData("not al toplantı perşembeye kaldı", "not")]
    [InlineData("yapılacaklara raporu bitir ekle", "yapilacak")]
    [InlineData("yapılacaklarımda ne var", "yapilacak")]
    [InlineData("market listeme süt ekle", "liste")]
    [InlineData("hesap makinesini aç", "uygulama_ac")]
    [InlineData("mola vermem lazım", "ritim")]
    [InlineData("odaklanmak istiyorum", "ritim")]
    [InlineData("yarım saat rahatsız etme", "rahatsiz_etme")]
    [InlineData("bugün ne kadar ekranda kaldım", "ekran_suresi")]
    [InlineData("50 lira kahve harcadım", "harcama")]
    [InlineData("koşuyu yaptım", "aliskanlik")]
    [InlineData("günümü planla", "gun_plani")]
    [InlineData("şimdi ne yapsam", "ne_yapayim")]
    [InlineData("dünkü haberler neler", "haber")]
    [InlineData("bilgisayarı kilitle", "sistem")]
    [InlineData("akşam sekizde annemi aramamı hatırlat", "hatirlatici")]
    [InlineData("hatırlatıcılarım neler", "hatirlatici")]
    [InlineData("yarın için hatırlatman var mı", "hatirlatici")]
    [InlineData("bunu not et yarın bankaya git", "not")]
    [InlineData("notlarımı oku", "not")]
    [InlineData("raporu bitirdim", "yapilacak")]
    [InlineData("faturayı ödeme işini tamamladım", "yapilacak")]
    [InlineData("bugün kitap okudum", "aliskanlik")]
    [InlineData("alışkanlıklarım nasıl gidiyor", "aliskanlik")]
    [InlineData("taksiye 120 lira verdim", "harcama")]
    [InlineData("bu ay ne kadar harcadım", "harcama")]
    [InlineData("beş dakikalık zamanlayıcı kur", "zamanlayici")]
    [InlineData("çayı demledim on beş dakika sonra söyle", "zamanlayici")]
    [InlineData("chrome'u aç", "uygulama_ac")]
    [InlineData("spotify'ı kapat", "uygulama_kapat")]
    [InlineData("sıradaki şarkı", "medya")]
    [InlineData("sesi kapat", "medya")]
    [InlineData("bugün nasıl geçti", "gun_sonu")]
    [InlineData("haftam nasıl geçti", "haftalik_ozet")]
    [InlineData("pomodoro başlat", "ritim")]
    [InlineData("su içtim", "ritim")]
    [InlineData("istanbul'da hava nasıl", "hava_durumu")]
    [InlineData("internet bağlantım yavaş mı", "baglanti")]
    public void CatchesCommonPhrasingsWithoutTheModel(string text, string tool) =>
        Assert.Equal(tool, QuickIntents.Match(text)?.Tool);

    [Theory]
    [InlineData("derin çalışma modunu başlat")]
    [InlineData("derin çalışmayı başlat")]
    public void StartsAUserRoutineByName(string text) =>
        Assert.Equal("rutin", QuickIntents.Match(text, ["derin çalışma", "akşam kapanışı"])?.Tool);

    [Theory]
    // Bilgi ya da sohbet soruları hızlı yolda bir komut sanılmamalı; dil modeline gitmeli.
    [InlineData("bu şarkıyı kim besteledi")]
    [InlineData("hava durumu uygulaması nasıl yapılır")]
    [InlineData("dolar neden yükseliyor")]
    [InlineData("zamanlayıcı nasıl çalışır")]
    [InlineData("not almanın en iyi yolu ne")]
    [InlineData("mola vermek verimliliği artırır mı")]
    [InlineData("yapılacaklar listesi tutmak işe yarar mı")]
    [InlineData("hatırlatıcı uygulaması önerir misin")]
    [InlineData("müzik dinlemek odaklanmaya yardımcı olur mu")]
    [InlineData("sesi kısmak için hangi tuşa basılır")]
    [InlineData("bilgisayarı kilitlemek neden önemli")]
    [InlineData("haberler neden hep kötü")]
    [InlineData("rutin oluşturmak için ipuçları ver")]
    [InlineData("pomodoro tekniğini kim buldu")]
    [InlineData("su içmek neden önemli")]
    [InlineData("günlük tutmanın faydaları neler")]
    [InlineData("harcamalarımı nasıl azaltabilirim")]
    [InlineData("İstanbul'un nüfusu ne kadar")]
    [InlineData("yarın ne giysem")]
    [InlineData("bana bir fıkra anlat")]
    [InlineData("chrome mu edge mi daha iyi")]
    [InlineData("spotify premium kaç lira")]
    [InlineData("ekran süresi çocuklar için kaç saat olmalı")]
    [InlineData("internet hızımı nasıl artırırım")]
    [InlineData("alışkanlık kazanmak kaç gün sürer")]
    public void LeavesQuestionsToTheModel(string text) =>
        Assert.True(QuickIntents.Match(text) is null or { Tentative: true }, $"Hızlı yol yakaladı: {QuickIntents.Match(text)?.Tool}");
}
