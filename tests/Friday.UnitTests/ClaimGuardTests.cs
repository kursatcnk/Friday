using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Araç çağrılmadan söylenen "yaptım" cümlelerinin tanınması (bkz. AgentPlan.ClaimsAction).</summary>
public class ClaimGuardTests
{
    [Theory]
    [InlineData("Tamam, not ettim.")]
    [InlineData("Selin'in doğum gününü hafızama kaydettim Patron.")]
    [InlineData("Yumurtayı market listene ekledim.")]
    [InlineData("Yarın öğlen sana hatırlatırım.")]
    [InlineData("Merak etme, akşam yedide haber vereceğim.")]
    [InlineData("Bunu aklımda tutacağım.")]
    [InlineData("Sabah yedide alarm kurdum.")]
    [InlineData("Hemen ekliyorum.")]
    [InlineData("Notunu aldım Patron.")]
    [InlineData("Ela'nın yeni öğretmeninin adı Selin Hanım olarak kaydedildi.")]
    [InlineData("Bir kenara yazdım.")]
    [InlineData("E-posta taslağını hazırladım, kontrol etmen için taslak penceresi açık.")]
    public void HearsAClaimedAction(string sentence) => Assert.True(AgentPlan.ClaimsAction(sentence), sentence);

    [Theory]
    [InlineData("Ankara'nın nüfusu yaklaşık 5,8 milyon.")]
    [InlineData("Bunu kaydedemedim, bir daha söyler misin?")]
    [InlineData("İstersen hatırlatıcı kurabilirim.")]
    [InlineData("Hatırlatıcı kurmamı ister misin?")]
    [InlineData("Dune'u öneririm.")]
    public void LeavesOtherSentencesAlone(string sentence) => Assert.False(AgentPlan.ClaimsAction(sentence), sentence);

    [Theory]
    [InlineData("Ayşe'nin numarasını hafızama kaydettim.", "hatirla")]
    [InlineData("Bu fikri not aldım.", "not")]
    [InlineData("Notunu aldım Patron.", "not")]
    [InlineData("Anneni araman için akşam 19:00'a bir hatırlatıcı kurdum.", "hatirlatici")]
    [InlineData("Faturayı yapılacaklar listene ekledim.", "yapilacaklar")]
    [InlineData("Yumurtayı market listene ekledim.", "liste")]
    [InlineData("Yarın yedide seni uyandırırım.", "hatirlatici")]
    [InlineData("E-posta taslağını hazırladım.", "eposta_taslagi")]
    [InlineData("Odak modunu bir dakika için beklettim.", "ritim")]
    [InlineData("Odak durumunu bir dakika için askıya aldım.", "ritim")]
    [InlineData("Tabii, sayacı durduruyorum. Kapıdaki durum ne?", "ritim")]
    [InlineData("Tabii, odak oturumunu beklemeye alıyorum.", "ritim")]
    [InlineData("Molanı 5 dakika daha uzattım, tadını çıkar.", "ritim")]
    [InlineData("Anladım, Mert'i aramanı içeren 23:30 hatırlatmasını siliyorum.", "hatirlatici")]
    [InlineData("Sunum işini önemli olarak işaretledim.", "yapilacaklar")]
    [InlineData("Sistemler hazır, hemen kaldığın yerden devam ediyoruz.", "ritim")]
    [InlineData("Ahmet Bey'e iletmek istediğin detayları eposta_taslagi aracına aktarıyorum.", "eposta_taslagi")]
    public void KnowsWhichToolAClaimNeeds(string sentence, string tool)
    {
        var tools = AgentPlan.ClaimedTools(sentence);
        Assert.Contains(tool, tools);
        Assert.Contains(tools[0] + " aracını", AgentPlan.ClaimNudge(tools), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Listeye ne ekledin?", true)]
    [InlineData("Dün hangi hatırlatıcıları kurdun?", true)]
    [InlineData("Marketten yumurta da almam lazım, listeye at", false)]
    [InlineData("Cuma günü Selin'in doğum günü, bunu aklında tut", false)]
    public void KnowsWhenTheUserAsksAboutEarlierWork(string question, bool asked) =>
        Assert.Equal(asked, AgentPlan.AsksWhatWasDone(question));

    [Theory]
    [InlineData("Yarın sabah yürüyüş yapmanı hatırlatmak için bir hatırlatıcı kurabilirim.", "yarın hava güzel olursa yürüyüş yaparım, hatırlatır mısın sabah", true)]
    [InlineData("Listene eklememi ister misin?", "markete ekmek ekle", true)]
    [InlineData("İstersen sana bir hatırlatıcı kurabilirim.", "bugün çok yoruldum", false)]
    [InlineData("Ne zaman hatırlatayım?", "Mert'i aramayı hatırlat", false)]
    public void NoticesAnOfferInsteadOfTheRequestedWork(string sentence, string question, bool offers) =>
        Assert.Equal(offers, AgentPlan.OffersInstead(sentence, question));
}
