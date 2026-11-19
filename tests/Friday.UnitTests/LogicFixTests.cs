using Friday.Core;

namespace Friday.UnitTests;

public class LogicFixTests
{
    [Theory]
    [InlineData("Süt al", true)]
    [InlineData("Annemi ara", true)]
    [InlineData("Faturayı öde", true)]
    [InlineData("Kargoyu iade", true)]
    [InlineData("Raporu bitir", false)]
    [InlineData("Sunumu hazırla", false)]
    [InlineData("Makaleyi oku", false)]
    public void TellsErrandsFromDeepWork(string todo, bool errand) => Assert.Equal(errand, DayPlanner.IsErrand(todo));

    [Fact]
    public void GivesErrandsShortBlocksWithoutBreaks()
    {
        var morning = new DateTime(2026, 11, 19, 9, 0, 0);
        var plan = DayPlanner.Plan(morning, [], ["Süt al", "Raporu bitir", "Annemi ara"], []);
        var milk = plan.Single(block => block.Title == "Süt al");
        Assert.Equal(PlanKind.Errand, milk.Kind);
        Assert.Equal(TimeSpan.FromMinutes(15), milk.End - milk.Start);

        // Kısa işten sonra mola yok; rapor hemen ardından başlar ve 45 dakika sürer.
        var report = plan.Single(block => block.Title == "Raporu bitir");
        Assert.Equal(milk.End, report.Start);
        Assert.Equal(TimeSpan.FromMinutes(45), report.End - report.Start);
        Assert.Contains(plan, block => block.Kind == PlanKind.Break && block.Start == report.End);
        Assert.Equal("09:00'da süt al ve 09:15'te raporu bitir için odak", DayPlanner.Describe(plan, 2));
    }
}
