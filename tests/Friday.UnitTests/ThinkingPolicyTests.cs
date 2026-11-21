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

    [Fact]
    public void IgnoresHistoryWithoutNumbers() =>
        Assert.False(AgentPlan.NeedsThinking("Yarın için kaç hatırlatıcım var?", ["Tabii, ne istersin?"]));
}
