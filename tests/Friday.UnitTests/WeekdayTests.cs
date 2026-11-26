using Friday.Core;

namespace Friday.UnitTests;

/// <summary>"15 Mart 2027 hangi güne denk geliyor": gün kodla hesaplanır (model bu soruda 14 sn düşünüyordu).</summary>
public class WeekdayTests
{
    private static readonly DateOnly Today = new(2026, 11, 26);

    [Theory]
    [InlineData("15 Mart 2027 hangi güne denk geliyor")]
    [InlineData("23 Nisan ne günü")]
    [InlineData("1 Ocak 2027 haftanın hangi günü")]
    [InlineData("Friday, 29 Ekim 2027 hangi gün")]
    public void RecognizesWeekdayQuestions(string sentence)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("saat_ve_tarih", command?.Tool);
        Assert.Equal("haftagunu", command!.Arguments["odak"]);
    }

    [Theory]
    [InlineData("bugün hangi gün")]
    [InlineData("annemin doğum günü hangi gün")]
    public void LeavesOtherDayQuestionsAlone(string sentence) => Assert.NotEqual("haftagunu", QuickIntents.Match(sentence)?.Arguments.GetValueOrDefault("odak"));

    [Fact]
    public void NamesTheWeekday()
    {
        var now = Today.ToDateTime(new TimeOnly(10, 0));
        Assert.Equal(new DateOnly(2027, 3, 15), TurkishTime.DayOf("15 Mart 2027", now));
        Assert.Equal("15 Mart 2027 pazartesiye denk geliyor, 109 gün sonra.", TurkishTime.Weekday(new DateOnly(2027, 3, 15), Today));
        Assert.Equal("1 Aralık salıya denk geliyor, 5 gün sonra.", TurkishTime.Weekday(new DateOnly(2026, 12, 1), Today));
        Assert.Equal("Yarın, 27 Kasım, cuma.", TurkishTime.Weekday(new DateOnly(2026, 11, 27), Today));
        Assert.Equal("29 Ekim 2023 pazara denk gelmişti.", TurkishTime.Weekday(new DateOnly(2023, 10, 29), Today));
    }
}
