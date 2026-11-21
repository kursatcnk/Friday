using Friday.Core;

namespace Friday.UnitTests;

public class PronounceIntentTests
{
    [Theory]
    [InlineData("Claude'u Klod diye oku", "claude", "klod")]
    [InlineData("Friday, Netflix kelimesini netfliks diye söyle.", "netflix", "netfliks")]
    [InlineData("Visual Studio adını vijual stüdyo diye oku", "visual studio", "vijual stüdyo")]
    [InlineData("Spotify'ı spotifay şeklinde telaffuz et", "spotify", "spotifay")]
    public void LearnsHowToSayAWord(string text, string word, string spoken)
    {
        var command = QuickIntents.Match(text);
        Assert.NotNull(command);
        Assert.Equal("okunus", command.Tool);
        Assert.Equal("ekle", command.Arguments["islem"]);
        Assert.Equal(word, command.Arguments["kelime"]);
        Assert.Equal(spoken, command.Arguments["okunus"]);
    }

    [Fact]
    public void ForgetsALearnedSound()
    {
        var command = QuickIntents.Match("Claude'un okunuşunu unut");
        Assert.NotNull(command);
        Assert.Equal("unut", command.Arguments["islem"]);
        Assert.Equal("claude", command.Arguments["kelime"]);
    }

    [Theory]
    [InlineData("bu kitabı yüksek sesle oku")]
    [InlineData("haberleri oku")]
    [InlineData("ekrandakini oku")]
    public void LeavesReadingRequestsAlone(string text) => Assert.NotEqual("okunus", QuickIntents.Match(text)?.Tool);

    [Fact]
    public void AppliesTaughtSoundsWithSuffixes()
    {
        var spoken = Pronunciation.Apply("Claude'a sordum.", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["claude"] = "klod" });
        Assert.Contains("klod'a", spoken, StringComparison.OrdinalIgnoreCase);
    }
}
