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
    public void CatchesCommonPhrasingsWithoutTheModel(string text, string tool) =>
        Assert.Equal(tool, QuickIntents.Match(text)?.Tool);
}
