using System.Globalization;
using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Kurulu bir hatırlatmanın sesle yeni zamana taşınması: söylenmeyen gün ya da saat eskisinden alınır.</summary>
public class ReminderMoveTests
{
    // Cumartesi 21 Kasım 2026, 14:00; hatırlatma pazartesi 23 Kasım 09:00'da.
    private static readonly DateTime Now = new(2026, 11, 21, 14, 0, 0);
    private static readonly DateTime Old = new(2026, 11, 23, 9, 0, 0);

    [Theory]
    [InlineData("cumaya", "2026-11-27 09:00")]
    [InlineData("yarına", "2026-11-22 09:00")]
    [InlineData("11'e", "2026-11-23 11:00")]
    [InlineData("saat 3'e", "2026-11-23 15:00")]
    [InlineData("akşama", "2026-11-23 20:00")]
    [InlineData("akşam 7'ye", "2026-11-23 19:00")]
    [InlineData("1 saat ileri", "2026-11-23 10:00")]
    [InlineData("yarım saat geri", "2026-11-23 08:30")]
    [InlineData("2 gün", "2026-11-25 09:00")]
    [InlineData("yarın 3'e", "2026-11-22 15:00")]
    [InlineData("30 Kasım'a", "2026-11-30 09:00")]
    [InlineData("on bir buçuğa", "2026-11-23 11:30")]
    public void KeepsWhatIsNotSaid(string said, string expected) =>
        Assert.Equal(expected, TurkishTime.Shift(Old, said, Now)?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

    [Fact]
    public void AnAfternoonReminderMovedToFourStaysInTheAfternoon() =>
        Assert.Equal(new DateTime(2026, 11, 23, 16, 0, 0), TurkishTime.Shift(new DateTime(2026, 11, 23, 15, 0, 0), "4'e", Now));

    [Theory]
    [InlineData("dişçi hatırlatmasını yarın 3'e al", "dişçi", "yarın 3'e")]
    [InlineData("toplantı hatırlatmasını cumaya ertele", "toplantı", "cumaya")]
    [InlineData("annemi arama hatırlatmasını akşam 8'e kaydır", "annemi arama", "akşam 8'e")]
    [InlineData("hatırlatmayı bir saat ileri al", "", "bir saat ileri")]
    public void TheQuickPathMovesAReminder(string said, string which, string when)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("hatirlatici", command?.Tool);
        Assert.Equal("tasi", command!.Arguments["islem"]);
        Assert.Equal(which, command.Arguments.GetValueOrDefault("metin") ?? "");
        Assert.Equal(when, command.Arguments["zaman"]);
    }

    [Fact]
    public void SnoozingTheReminderThatJustRangIsNotAMove() =>
        Assert.NotEqual("tasi", QuickIntents.Match("hatırlatmayı 10 dakika ertele")?.Arguments.GetValueOrDefault("islem"));

    [Fact]
    public void TheStoreMovesTheMatchingReminder()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "friday-move-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ReminderStore(Path.Combine(folder, "reminders.json"));
            store.Add(Old, "dişçiyi aramamı", Now);
            store.Add(Old.AddHours(2), "toplantıyı", Now);
            var moved = store.Move("dişçi", item => TurkishTime.Shift(item.Due, "yarın 3'e", Now));
            Assert.NotNull(moved);
            Assert.Equal("dişçiyi aramamı", moved.Value.New.Text);
            Assert.Equal(new DateTime(2026, 11, 22, 15, 0, 0), moved.Value.New.Due);
            Assert.Contains(store.Upcoming, item => item.Text == "toplantıyı" && item.Due == Old.AddHours(2));
            Assert.Null(store.Move("kira", item => TurkishTime.Shift(item.Due, "cumaya", Now)));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
