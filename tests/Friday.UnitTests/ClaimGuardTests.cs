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
    [InlineData("Bir kenara yazdım.")]
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
}
