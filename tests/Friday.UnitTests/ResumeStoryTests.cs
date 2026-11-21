using Friday.Core;

namespace Friday.UnitTests;

public class ResumeStoryTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 14, 0, 0);

    [Fact]
    public void PutsTheRecentTracesTogether()
    {
        var facts = new ResumeFacts(Now)
        {
            BeforeBreak = "Visual Studio Code, Rapor.cs",
            Focus = ("Raporu bitir", Now.AddHours(-1)),
            RecentApps = [("Visual Studio Code", 1800), ("Chrome", 600), ("Spotify", 60)],
            LastChat = ("Bütçe tablosu", Now.AddHours(-2)),
            Due = ["Faturayı öde"]
        };
        Assert.Equal(
            "Son molandan önce Visual Studio Code, Rapor.cs açıktı. En son \"raporu bitir\" işine odaklanıyordun. "
            + "Son bir saatte en çok Visual Studio Code ve Chrome açıktı. Benimle en son \"Bütçe tablosu\" üzerine konuştun.",
            ResumeStory.Tell(facts));
    }

    [Fact]
    public void FallsBackToYesterdayAfterANightBreak()
    {
        var facts = new ResumeFacts(Now)
        {
            Focus = ("Eski iş", Now.AddDays(-1)),
            YesterdayApps = [("Visual Studio Code", 9000), ("Chrome", 3000)],
            LastChat = ("Çok eski sohbet", Now.AddDays(-3)),
            NewestTodo = ("Sunumu hazırla", Now.AddHours(-3)),
            LastNote = ("Müşteri mavi istiyor", Now.AddHours(-5))
        };
        Assert.Equal(
            "Dün en çok Visual Studio Code ve Chrome kullanmıştın. En son eklediğin iş: sunumu hazırla. Son notun: Müşteri mavi istiyor.",
            ResumeStory.Tell(facts));
    }

    [Fact]
    public void UsesTodaysScreenTimeAfterARestart()
    {
        var facts = new ResumeFacts(Now) { TodayApps = [("Excel", 2400), ("Chrome", 900)], YesterdayApps = [("Oyun", 9000)] };
        Assert.Equal("Bugün en çok Excel ve Chrome kullandın.", ResumeStory.Tell(facts));
    }

    [Fact]
    public void AdmitsWhenThereIsNothingRecent() =>
        Assert.StartsWith("Kaldığın yeri gösterecek yakın bir kaydım yok", ResumeStory.Tell(new ResumeFacts(Now)), StringComparison.Ordinal);

    [Theory]
    [InlineData("nerede kalmıştım")]
    [InlineData("nerede kalmıştık")]
    [InlineData("Nerede kalmıştım?")]
    [InlineData("en son neyle uğraşıyordum")]
    [InlineData("kaldığım yerden devam edelim")]
    public void UnderstandsWhereWasI(string text) => Assert.Equal("kaldigim_yer", QuickIntents.Match(text)?.Tool);
}
