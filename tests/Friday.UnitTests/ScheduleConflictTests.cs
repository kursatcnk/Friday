using Friday.Core;

namespace Friday.UnitTests;

public class ScheduleConflictTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 10, 0, 0);

    [Fact]
    public void NoticesANearbyReminder()
    {
        var added = new Reminder("yeni", Now.AddHours(5), "annemi ara", Now);
        Reminder[] upcoming =
        [
            added,
            new("a", Now.AddHours(5).AddMinutes(10), "diş hekimi", Now),
            new("b", Now.AddHours(8), "ilaç", Now),
            new("c", DateTime.MaxValue, "eve dönünce çiçekleri sula", Now) { Trigger = "donus" }
        ];
        Assert.Equal("Bu arada 15:10'da \"diş hekimi\" hatırlatman da var.", ScheduleConflicts.ForReminder(added.Due, added.Id, upcoming));
    }

    [Fact]
    public void SaysWhenItIsTheSameMinute()
    {
        var added = new Reminder("yeni", Now.AddHours(5), "annemi ara", Now);
        Reminder[] upcoming = [added, new("a", Now.AddHours(5), "", Now)];
        Assert.Equal("Aynı saatte bir zamanlayıcın da var.", ScheduleConflicts.ForReminder(added.Due, added.Id, upcoming));
    }

    [Fact]
    public void StaysQuietWhenNothingIsClose()
    {
        var added = new Reminder("yeni", Now.AddHours(5), "annemi ara", Now);
        Reminder[] upcoming = [added, new("a", Now.AddHours(6), "diş hekimi", Now)];
        Assert.Null(ScheduleConflicts.ForReminder(added.Due, added.Id, upcoming));
    }

    [Fact]
    public void WarnsAboutABusyDeadlineDay()
    {
        var friday = new DateOnly(2026, 11, 27);
        var added = new TodoItem("n", "Sunumu hazırla", Now, Due: friday);
        TodoItem[] open =
        [
            added,
            new("a", "Raporu bitir", Now, Due: friday),
            new("b", "Faturayı öde", Now, Due: friday),
            new("c", "Kirayı yatır", Now, Due: friday.AddDays(1)),
            new("d", "Eski iş", Now, Now, friday)
        ];
        Assert.Equal("O gün 2 işin daha teslim (raporu bitir, faturayı öde); istersen birini öne alalım.", ScheduleConflicts.ForDeadline(added, open));
        Assert.Null(ScheduleConflicts.ForDeadline(added, open.Take(2)));
        Assert.Null(ScheduleConflicts.ForDeadline(new TodoItem("x", "Süt al", Now), open));
    }
}
