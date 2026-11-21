using Friday.Core;

namespace Friday.UnitTests;

public class DayStoryTests
{
    // 2026-11-21 bir cumartesi.
    private static readonly DateOnly Saturday = new(2026, 11, 21);

    [Theory]
    [InlineData("dün", "2026-11-20")]
    [InlineData("bugün", "2026-11-21")]
    [InlineData("önceki gün", "2026-11-19")]
    [InlineData("3 gün önce", "2026-11-18")]
    [InlineData("salı", "2026-11-17")]
    [InlineData("geçen salı", "2026-11-17")]
    [InlineData("cumartesi", "2026-11-21")]
    [InlineData("geçen cumartesi", "2026-11-14")]
    [InlineData("geçen hafta salı", "2026-11-10")]
    [InlineData("18 kasım", "2026-11-18")]
    [InlineData("25 aralık", "2025-12-25")]
    [InlineData("pazartesi günü", "2026-11-16")]
    public void UnderstandsPastDays(string text, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), DayStory.Parse(text, Saturday));

    [Theory]
    [InlineData("yarın")]
    [InlineData("31 şubat")]
    [InlineData("geçen ay")]
    public void RejectsWhatItCannotPlace(string text) => Assert.Null(DayStory.Parse(text, Saturday));

    [Fact]
    public void TellsTheDayInAFewSentences()
    {
        var facts = new DayFacts(new DateOnly(2026, 11, 17))
        {
            Span = (TimeSpan.FromHours(9.5), TimeSpan.FromHours(25.5)),
            ActiveMinutes = 380,
            FocusMinutes = 75,
            Apps = [("Visual Studio Code", 11400), ("Chrome", 4800), ("Spotify", 120)],
            Finished = ["Raporu bitir", "Faturayı öde"],
            Habits = ["Koşu"],
            Spent = 340,
            TopCategory = "Market",
            Journal = ["Sunum çok iyi geçti."]
        };
        var story = DayStory.Tell(facts, Saturday);
        Assert.StartsWith("Salı günü (17 Kasım) 09:30 ile 01:30 arasında bilgisayar başındaydın, toplam 6 saat 20 dakika.", story, StringComparison.Ordinal);
        Assert.Contains("En çok kullandıkların: Visual Studio Code 3 saat 10 dakika, Chrome 1 saat 20 dakika.", story, StringComparison.Ordinal);
        Assert.DoesNotContain("Spotify", story, StringComparison.Ordinal);
        Assert.Contains("2 iş bitirdin (raporu bitir, faturayı öde) ve 1 saat 15 dakika odaklandın.", story, StringComparison.Ordinal);
        Assert.Contains("Koşu alışkanlığını yaptın", story, StringComparison.Ordinal);
        Assert.Contains("en çok market", story, StringComparison.Ordinal);
        Assert.EndsWith("Günlüğüne şunu yazmışsın: Sunum çok iyi geçti.", story, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysSoWhenThereIsNothing()
    {
        var story = DayStory.Tell(new DayFacts(new DateOnly(2026, 11, 20)), Saturday);
        Assert.Equal("Dün için hiçbir kaydım yok; Friday kapalıydı ya da bilgisayar kullanılmamış.", story);
    }

    [Fact]
    public void StartsWithTheDayEvenWithoutScreenRecords()
    {
        var story = DayStory.Tell(new DayFacts(new DateOnly(2026, 11, 20)) { Finished = ["Raporu bitir"] }, Saturday);
        Assert.Equal("Dün: \"raporu bitir\" işini bitirdin.", story);
    }

    [Theory]
    [InlineData("dün ne yaptım", "dün")]
    [InlineData("Geçen salı ne yaptım?", "geçen salı")]
    [InlineData("3 gün önce neler yaptım", "3 gün önce")]
    [InlineData("18 kasım nasıl geçti", "18 kasım")]
    [InlineData("pazartesi günü ne yapıyordum", "pazartesi")]
    public void UnderstandsDayQuestions(string text, string day)
    {
        var command = QuickIntents.Match(text);
        Assert.NotNull(command);
        Assert.Equal("o_gun", command.Tool);
        Assert.Equal(day, command.Arguments["gun"]);
    }
}
