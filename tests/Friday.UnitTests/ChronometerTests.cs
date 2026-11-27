using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Kronometre: başlar, duraklar, sürer, tur alır, sıfırlanır ve yeniden açılışta kaldığı yerden devam eder.</summary>
public class ChronometerTests
{
    private static readonly DateTime Start = new(2026, 11, 27, 10, 0, 0);

    [Fact]
    public void PausesResumesAndTakesLaps()
    {
        var watch = new Chronometer();
        Assert.False(watch.HasTime(Start));
        Assert.True(watch.Start(Start));
        Assert.False(watch.Start(Start.AddSeconds(5)));

        var first = watch.Lap(Start.AddSeconds(65));
        Assert.Equal(new ChronometerLap(1, TimeSpan.FromSeconds(65), TimeSpan.FromSeconds(65)), first);

        Assert.True(watch.Pause(Start.AddSeconds(90)));
        // Duraklatılmışken süre ilerlemez.
        Assert.Equal(TimeSpan.FromSeconds(90), watch.Elapsed(Start.AddMinutes(10)));
        watch.Start(Start.AddMinutes(10));
        Assert.Equal(TimeSpan.FromSeconds(120), watch.Elapsed(Start.AddMinutes(10).AddSeconds(30)));

        var second = watch.Lap(Start.AddMinutes(10).AddSeconds(30));
        Assert.Equal(new ChronometerLap(2, TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(120)), second);
        Assert.Equal(2, watch.Laps.Count);

        watch.Reset();
        Assert.False(watch.Running);
        Assert.Empty(watch.Laps);
        Assert.Null(watch.Lap(Start.AddMinutes(20)));
    }

    [Fact]
    public void KeepsCountingAcrossARestart()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "friday-chrono-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(folder, "chronometer.json");
            var watch = new Chronometer(file);
            watch.Start(Start);
            watch.Lap(Start.AddSeconds(30));

            var reopened = new Chronometer(file);
            Assert.True(reopened.Running);
            Assert.Equal(TimeSpan.FromMinutes(2), reopened.Elapsed(Start.AddMinutes(2)));
            Assert.Single(reopened.Laps);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Theory]
    [InlineData("kronometreyi başlat", "baslat")]
    [InlineData("süre tut", "baslat")]
    [InlineData("kronometreyi duraklat", "durdur")]
    [InlineData("kronometreye devam et", "devam")]
    [InlineData("tur", "tur")]
    [InlineData("tur at", "tur")]
    [InlineData("kronometreyi sıfırla", "sifirla")]
    [InlineData("kronometre kaç oldu", "durum")]
    public void UnderstandsStopwatchCommands(string sentence, string action)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("kronometre", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
    }

    [Fact]
    public void ShowsOnlyShortLivedRemindersAsCountdowns()
    {
        var now = Start;
        Reminder[] reminders =
        [
            new("a", now.AddMinutes(10), "çay", now.AddMinutes(-5)),
            new("b", now.AddDays(1), "dişçi", now.AddDays(-2)),
            new("c", now.AddHours(2), "toplantı", now.AddDays(-3)),
            new("d", now.AddMinutes(3), "", now.AddMinutes(-1)),
            new("e", now.AddMinutes(30), "su iç", now.AddMinutes(-1)) { Repeat = "gunluk" },
        ];
        Assert.Equal(["d", "a"], Countdowns.Active(reminders, now).Select(item => item.Id));
    }
}
