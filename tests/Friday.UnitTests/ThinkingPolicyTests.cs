using Friday.Core;

namespace Friday.UnitTests;

public class ThinkingPolicyTests
{
    [Fact]
    public void SkipsThinkingForAFirstFactualQuestion() =>
        Assert.False(AgentPlan.NeedsThinking("Dolar şu an kaç lira?", []));

    [Fact]
    public void ThinksWhenAnEarlierAnswerHasNumbersToRepeat() =>
        Assert.True(AgentPlan.NeedsThinking("Peki şimdi dolar kaç?", ["Merhaba!", "Dolar şu an 34,52 lira."]));

    [Fact]
    public void NeverThinksForChatEvenWithNumbersAround() =>
        Assert.False(AgentPlan.NeedsThinking("Bana bir fıkra anlat", ["Dolar 34 lira."]));

    [Theory]
    [InlineData("Yarın akşam Tülin Hanım'a dönüş yapmam gerek, hatırlatırsın değil mi")]
    [InlineData("Faturayı yarın ödememi hatırlat")]
    [InlineData("Raporu cuma gününe kadar bitirmem lazım, not al")]
    public void DoesNotThinkForARequestEvenWithNumbersAround(string request) =>
        Assert.False(AgentPlan.NeedsThinking(request, ["Ayşe'nin telefon numarası 0532 111 22 33 olarak kaydedildi."]));

    [Theory]
    [InlineData("Şu an itibarıyla bu ayki harcamam ne kadar oldu?")]
    [InlineData("Şimdi kaç saat oldu odak süren?")]
    [InlineData("Az önce bir tane daha ekledim, şimdi yarın için kaç hatırlatıcım var?")]
    [InlineData("Takvimimde yarın hangi toplantılar var?")]
    public void StillThinksForValueFollowUps(string question) =>
        Assert.True(AgentPlan.NeedsThinking(question, ["Bu ay toplam 4.250 lira harcadın."]));

    [Fact]
    public void IgnoresHistoryWithoutNumbers() =>
        Assert.False(AgentPlan.NeedsThinking("Yarın için kaç hatırlatıcım var?", ["Tabii, ne istersin?"]));
}
