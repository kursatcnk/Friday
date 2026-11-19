using Friday.Core;

namespace Friday.UnitTests;

public class ActiveHoursTests
{
    // 2026-11-19 bir perşembe.
    private static readonly DateTime Thursday = new(2026, 11, 19, 12, 0, 0);

    [Fact]
    public void CountsAfterMidnightWorkToThePreviousDay()
    {
        Assert.Equal(new DateOnly(2026, 11, 18), ActiveHours.DayOf(new DateTime(2026, 11, 19, 1, 30, 0)));
        Assert.Equal(new DateOnly(2026, 11, 19), ActiveHours.DayOf(new DateTime(2026, 11, 19, 5, 0, 0)));
        Assert.Equal(0, ActiveHours.SlotOf(new DateTime(2026, 11, 19, 5, 10, 0)));
        Assert.Equal(40, ActiveHours.SlotOf(new DateTime(2026, 11, 20, 1, 0, 0)));
        Assert.Equal(47, ActiveHours.SlotOf(new DateTime(2026, 11, 20, 4, 59, 0)));
    }

    [Fact]
    public void NeedsAFewDaysBeforeItGuesses()
    {
        var hours = Worked(days: 4, from: 9, to: 19);
        Assert.Null(hours.TypicalEnd(Thursday));
        Assert.Equal(ActiveHours.DefaultEnd, hours.DayEnd(Thursday));
    }

    [Fact]
    public void LearnsWhenTheDayUsuallyEnds()
    {
        var hours = Worked(days: 10, from: 9, to: 19);
        Assert.Equal(TimeSpan.FromHours(9), hours.TypicalStart(Thursday));
        Assert.Equal(TimeSpan.FromHours(19), hours.TypicalEnd(Thursday));
        Assert.Equal(TimeSpan.FromHours(19), hours.DayEnd(Thursday));
    }

    [Fact]
    public void FollowsNightOwlsPastMidnightButNotForever()
    {
        var owl = Worked(days: 10, from: 14, to: 25.5);
        Assert.Equal(TimeSpan.FromHours(25.5), owl.TypicalEnd(Thursday));
        Assert.Equal(TimeSpan.FromHours(25.5), owl.DayEnd(Thursday));

        var allNight = Worked(days: 10, from: 20, to: 28.5);
        Assert.Equal(ActiveHours.LatestEnd, allNight.DayEnd(Thursday));

        var early = Worked(days: 10, from: 7, to: 15);
        Assert.Equal(ActiveHours.EarliestEnd, early.DayEnd(Thursday));
    }

    [Fact]
    public void TellsWeekdaysAndWeekendsApart()
    {
        var hours = new ActiveHours();
        for (var back = 1; back <= 21; back++)
        {
            var day = Thursday.Date.AddDays(-back);
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            Work(hours, day, weekend ? 11 : 9, weekend ? 24 : 18);
        }

        Assert.Equal(TimeSpan.FromHours(18), hours.TypicalEnd(Thursday));
        var saturday = new DateTime(2026, 11, 21, 15, 0, 0);
        Assert.Equal(TimeSpan.FromHours(24), hours.TypicalEnd(saturday));
        Assert.Equal(TimeSpan.FromHours(11), hours.TypicalStart(saturday));
    }

    [Fact]
    public void IgnoresShortVisitsAndToday()
    {
        var hours = Worked(days: 6, from: 9, to: 20);
        // Kısa bir gece uğrayışı (5 dk'dan az) ve bugünün yarım günü sonucu değiştirmez.
        hours.Add(Thursday.Date.AddDays(-1).AddHours(23).AddMinutes(10), 120);
        Work(hours, Thursday.Date, 9, 11);
        Assert.Equal(TimeSpan.FromHours(20), hours.TypicalEnd(Thursday));
    }

    [Theory]
    // Varsayılan gün sonu (23.00) eski sabit pencereleri verir: gün sonu sorusu 21.30–23.00.
    [InlineData(23.0, "21:29", false)]
    [InlineData(23.0, "21:30", true)]
    [InlineData(23.0, "22:59", true)]
    [InlineData(23.0, "23:00", false)]
    // Gece 01.30'da bırakan biri: 00.00–01.30.
    [InlineData(25.5, "23:59", false)]
    [InlineData(25.5, "00:10", true)]
    [InlineData(25.5, "01:29", true)]
    [InlineData(25.5, "01:30", false)]
    // 18.00'de bırakan biri: 16.30–18.00.
    [InlineData(18.0, "16:29", false)]
    [InlineData(18.0, "16:30", true)]
    public void PlacesTheDayEndQuestionBeforeTheUsualEnd(double end, string clock, bool expected)
    {
        var time = TimeSpan.Parse(clock, System.Globalization.CultureInfo.InvariantCulture);
        var now = Thursday.Date.Add(time);
        Assert.Equal(expected, ActiveHours.InEvening(now, TimeSpan.FromHours(end), TimeSpan.FromMinutes(90), TimeSpan.Zero));
    }

    [Fact]
    public void NeverOpensAnEveningWindowBeforeFour()
    {
        // 18.00'de bırakan biri için "5 saat önce" 13.00 olurdu; akşam pencereleri 16.00'dan önce açılmaz.
        Assert.False(ActiveHours.InEvening(Thursday.Date.AddHours(14), TimeSpan.FromHours(18), TimeSpan.FromHours(5), TimeSpan.FromMinutes(90)));
        Assert.True(ActiveHours.InEvening(Thursday.Date.AddHours(16), TimeSpan.FromHours(18), TimeSpan.FromHours(5), TimeSpan.FromMinutes(90)));
    }

    [Fact]
    public void SurvivesARoundTripAndForgetsOldDays()
    {
        var hours = Worked(days: 40, from: 9, to: 21);
        var copy = ActiveHours.FromJson(hours.ToJson());
        Assert.Equal(TimeSpan.FromHours(21), copy.TypicalEnd(Thursday));
        Assert.True(hours.ToJson().Split("\"2026-").Length - 1 <= 29);
        Assert.Null(ActiveHours.FromJson("bozuk").TypicalEnd(Thursday));
    }

    [Theory]
    [InlineData("genelde ne zaman bırakıyorum")]
    [InlineData("günüm genelde ne zaman bitiyor")]
    [InlineData("hangi saatlerde bilgisayar başındayım")]
    [InlineData("günlük ritmim ne")]
    public void AnswersQuestionsAboutTheUsualDay(string text) =>
        Assert.Equal("aktif_saatler", QuickIntents.Match(text)?.Tool);

    private static ActiveHours Worked(int days, double from, double to)
    {
        var hours = new ActiveHours();
        for (var back = days; back >= 1; back--)
        {
            Work(hours, Thursday.Date.AddDays(-back), from, to);
        }

        return hours;
    }

    /// <summary>Günün <paramref name="from"/>–<paramref name="to"/> saatleri arasında (24'ü geçebilir) 15 sn'de bir etkinlik.</summary>
    private static void Work(ActiveHours hours, DateTime day, double from, double to)
    {
        for (var at = day.AddHours(from); at < day.AddHours(to); at = at.AddMinutes(1))
        {
            hours.Add(at, 60);
        }
    }
}
