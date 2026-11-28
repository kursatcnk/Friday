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

    [Theory]
    [InlineData("bugünden 45 gün sonra hangi gün", "ileri")]
    [InlineData("2 hafta sonra ne tarih", "ileri")]
    [InlineData("100 gün önce hangi gündü", "ileri")]
    [InlineData("3 Mart ile 15 Nisan arası kaç gün", "aralik")]
    [InlineData("3 Mart'tan 15 Nisan'a kaç gün var", "aralik")]
    public void RecognizesDateArithmetic(string sentence, string focus) => Assert.Equal(focus, QuickIntents.Match(sentence)?.Arguments["odak"]);

    [Fact]
    public void CountsDays()
    {
        Assert.Equal("45 gün sonra 10 Ocak 2027, pazar.", TurkishTime.Offset(45, Today));
        Assert.Equal("14 gün sonra 10 Aralık, perşembe.", TurkishTime.Offset(14, Today));
        Assert.Equal("100 gün önce 18 Ağustos, salıydı.", TurkishTime.Offset(-100, Today));
        var now = Today.ToDateTime(new TimeOnly(10, 0));
        Assert.Equal("3 Mart 2027 ile 15 Nisan 2027 arasında 43 gün var (6 hafta 1 gün).", TurkishTime.Between("3 Mart", "15 Nisan", now));
        Assert.Equal("20 Aralık ile 5 Ocak 2027 arasında 16 gün var (2 hafta 2 gün).", TurkishTime.Between("20 Aralık", "5 Ocak", now));
    }

    [Theory]
    [InlineData("1990 doğumlu biri kaç yaşında")]
    [InlineData("15 Mart 1990'da doğan kaç yaşında")]
    public void RecognizesAgeQuestions(string sentence) => Assert.Equal("yas", QuickIntents.Match(sentence)?.Arguments["odak"]);

    [Fact]
    public void WorksOutAges()
    {
        Assert.Equal("1990 doğumlu biri bu yıl 36 yaşına girer; doğum günü henüz gelmediyse 35 yaşında.", TurkishTime.Age("1990", Today));
        Assert.Equal("36 yaşında; 37 yaşına 109 gün var.", TurkishTime.Age("15 Mart 1990", Today));
        Assert.Equal("36 yaşında; doğum günü bugün.", TurkishTime.Age("26 Kasım 1990", Today));
        Assert.Null(TurkishTime.Age("15 Mart 2030", Today));
    }
}
