using Friday.Core;

namespace Friday.UnitTests;

public class ProductivityTests
{
    // 2026-11-21 bir cumartesi.
    private static readonly DateOnly Saturday = new(2026, 11, 21);

    [Fact]
    public void WaitsForEnoughDays()
    {
        var days = Enumerable.Range(1, 4).Select(back => new ProductiveDay(Saturday.AddDays(-back), 60, 300, 20)).ToList();
        Assert.Equal("Verimliliğini çıkarmak için en az 7 günlük kayıt gerekiyor; şu an 4 gün var. Birkaç gün sonra tekrar sor.",
            Productivity.Analyze(days, Saturday));
    }

    [Fact]
    public void FindsTheBestAndWorstWeekdaysAndTheWeeklyTrend()
    {
        var days = Enumerable.Range(1, 14).Select(back =>
        {
            var day = Saturday.AddDays(-back);
            var focus = day.DayOfWeek switch { DayOfWeek.Tuesday => 130, DayOfWeek.Monday => 20, _ => 60 };
            var distracted = back <= 7 ? 30 : 60;
            return new ProductiveDay(day, focus, 360, distracted);
        }).ToList();
        var report = Productivity.Analyze(days, Saturday);
        Assert.StartsWith("Son 14 günde en çok salı günleri odaklanıyorsun (ortalama 2 saat 10 dakika), en az pazartesi (20 dakika).", report, StringComparison.Ordinal);
        Assert.Contains("Dikkat dağıtan sitelerde geçen süren bu hafta geçen haftaya göre yüzde 50 azaldı; günde ortalama 30 dakika.", report, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysSoWhenThereAreNoFocusSessions()
    {
        var days = Enumerable.Range(1, 8).Select(back => new ProductiveDay(Saturday.AddDays(-back), 0, 240, 0)).ToList();
        Assert.StartsWith("Son 8 günde günde ortalama 4 saat bilgisayar başındasın", Productivity.Analyze(days, Saturday), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ne zaman daha verimliyim")]
    [InlineData("hangi günler daha verimliyim")]
    [InlineData("verimlilik analizim")]
    [InlineData("verimliliğim nasıl")]
    public void UnderstandsProductivityQuestions(string text) => Assert.Equal("verimlilik", QuickIntents.Match(text)?.Tool);
}
