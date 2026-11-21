using Friday.Core;

namespace Friday.UnitTests;

public class InsightIntentTests
{
    [Theory]
    [InlineData("bunu neden söyledin", "neden")]
    [InlineData("Niye hatırlattın?", "neden")]
    [InlineData("neden bunu sordun", "neden")]
    [InlineData("neler öğrendin", "ogrendiklerin")]
    [InlineData("bu hafta ne öğrendin", "ogrendiklerin")]
    [InlineData("benim hakkımda neler öğrendin", "ogrendiklerin")]
    public void UnderstandsQuestionsAboutFriday(string text, string tool) =>
        Assert.Equal(tool, QuickIntents.Match(text)?.Tool);

    [Theory]
    [InlineData("neden gökyüzü mavi")]
    [InlineData("bugün ne öğrendim")]
    [InlineData("neden geç kaldın diye sordu")]
    public void LeavesOtherWhyQuestionsToTheModel(string text) =>
        Assert.NotEqual("neden", QuickIntents.Match(text)?.Tool);
}
