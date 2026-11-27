using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Dünya saati: Windows'un saat dilimleriyle, internetsiz.</summary>
public class WorldClockTests
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");

    [Theory]
    [InlineData("Tokyo'da saat kaç", "Tokyo")]
    [InlineData("londrada şu an saat kaç", "londra")]
    [InlineData("New York'ta saat kaç", "New York")]
    [InlineData("Friday, Paris'te saat kaç oldu", "Paris")]
    public void RecognizesWorldTimeQuestions(string sentence, string place)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("dunya", command?.Arguments["odak"]);
        Assert.Equal(place, command!.Arguments["yer"], ignoreCase: true);
    }

    [Theory]
    [InlineData("saat kaç")]
    [InlineData("evde saat kaç")]
    public void LeavesOtherClockQuestionsAlone(string sentence) => Assert.NotEqual("dunya", QuickIntents.Match(sentence)?.Arguments.GetValueOrDefault("odak"));

    [Fact]
    public void TellsTheTimeElsewhere()
    {
        // 27 Kasım 2026 21:30 İstanbul (UTC+3) = 18:30 UTC.
        var utc = new DateTime(2026, 11, 27, 18, 30, 0, DateTimeKind.Utc);
        Assert.Equal("Tokyo'da saat 03:30, yarın cumartesi; buradan 6 saat ileride.", WorldClock.Describe("tokyo", utc, Istanbul));
        Assert.Equal("Londra'da saat 18:30; buradan 3 saat geride.", WorldClock.Describe("Londra", utc, Istanbul));
        Assert.Equal("New York'ta saat 13:30; buradan 8 saat geride.", WorldClock.Describe("new york", utc, Istanbul));
        Assert.Equal("Paris'te saat 19:30; buradan 2 saat geride.", WorldClock.Describe("paris", utc, Istanbul));
        Assert.Null(WorldClock.Describe("ev", utc, Istanbul));
    }
}
