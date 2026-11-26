using Friday.Core;

namespace Friday.UnitTests;

public class ProactiveSchedulerTests
{
    private static readonly DateTime Evening = new(2026, 11, 19, 20, 0, 0);

    private static ProactiveCandidate Say(string kind, int priority, string? text, List<string>? asked = null) =>
        new(kind, priority, _ =>
        {
            asked?.Add(kind);
            return Task.FromResult(text);
        });

    [Fact]
    public async Task PicksTheMostImportantAndAsksNoOneElse()
    {
        var asked = new List<string>();
        var scheduler = new ProactiveScheduler();
        var next = await scheduler.NextAsync([Say("hava", 40, "Yarın yağmur var.", asked), Say("son_tarih", 65, null, asked) with { Spaced = true }, Say("seri", 55, "Seri tehlikede.", asked)],
            new ProactiveContext(Evening, Muted: false, Away: false, FullScreen: false));
        Assert.Equal("seri", next?.Candidate.Kind);
        // Daha önemli olan denendi (söyleyecek bir şeyi yoktu); daha önemsizine hiç sorulmadı.
        Assert.Equal(["son_tarih", "seri"], asked);
    }

    [Fact]
    public async Task KeepsEveningNudgesApart()
    {
        var scheduler = new ProactiveScheduler();
        var context = new ProactiveContext(Evening, false, false, false);
        Assert.NotNull(await scheduler.NextAsync([Say("seri", 55, "Seri.") with { Spaced = true }], context));
        Assert.Null(await scheduler.NextAsync([Say("son_tarih", 65, "Yarın son gün.") with { Spaced = true }], context with { Now = Evening.AddMinutes(30) }));
        Assert.NotNull(await scheduler.NextAsync([Say("kur", 100, "Dolar 45'i geçti.") with { UserSet = true }], context with { Now = Evening.AddMinutes(30) }));
        Assert.NotNull(await scheduler.NextAsync([Say("son_tarih", 65, "Yarın son gün.") with { Spaced = true }], context with { Now = Evening.AddMinutes(46) }));
    }

    [Fact]
    public async Task RespectsQuietAwayFullScreenAndLearning()
    {
        var scheduler = new ProactiveScheduler { Allowed = kind => kind != "goz" };
        var quiet = new ProactiveContext(Evening, Muted: true, Away: false, FullScreen: false);
        Assert.Null(await scheduler.NextAsync([Say("seri", 55, "Seri.")], quiet));
        Assert.Equal("kur", (await scheduler.NextAsync([Say("seri", 55, "Seri."), Say("kur", 10, "Kur.") with { UserSet = true }], quiet))?.Candidate.Kind);

        var away = new ProactiveContext(Evening, false, Away: true, FullScreen: true);
        Assert.Null(await scheduler.NextAsync([Say("gun_sonu", 50, "Günün nasıl geçti?") with { NeedsPresence = true }], away));
        Assert.Null(await scheduler.NextAsync([Say("seri", 50, "Seri.") with { AvoidFullScreen = true }], away));
        Assert.Null(await scheduler.NextAsync([Say("goz", 60, "Göz molası.")], new ProactiveContext(Evening, false, false, false)));
    }

    [Fact]
    public async Task WaitsUntilAFocusIsOverButLetsUserSetAlertsThrough()
    {
        var scheduler = new ProactiveScheduler();
        var focused = new ProactiveContext(Evening, Muted: false, Away: false, FullScreen: false, Focused: true);
        Assert.Null(await scheduler.NextAsync([Say("seri", 55, "Seri tehlikede.")], focused));
        Assert.Equal("kur", (await scheduler.NextAsync([Say("seri", 55, "Seri."), Say("kur", 10, "Dolar 45'i geçti.") with { UserSet = true }], focused))?.Candidate.Kind);
        Assert.Equal("seri", (await scheduler.NextAsync([Say("seri", 55, "Seri tehlikede.")], focused with { Focused = false }))?.Candidate.Kind);
    }
}
