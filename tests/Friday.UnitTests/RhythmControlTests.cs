using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// Ritmi istendiği an yönetmek: odak duraklatılır, sürdürülür, uzatılır; pomodoro serisi kendi saatiyle ilerler ve her an
/// bitirilebilir. Eskiden seri ayrı bir zamanlayıcıyla dönüyor, "Odağı bitir" deyince arkadan mola ve yeni turlar gelmeye
/// devam ediyordu; odak da duraklatılamıyordu.
/// </summary>
public class RhythmControlTests
{
    private static readonly DateTime Morning = new(2026, 11, 17, 9, 0, 0);

    private static List<RhythmEvent> Run(RhythmTracker tracker, DateTime from, TimeSpan length, TimeSpan idle)
    {
        var events = new List<RhythmEvent>();
        for (var now = from; now <= from + length; now += TimeSpan.FromSeconds(20))
        {
            events.AddRange(tracker.Update(now, idle));
        }

        return events;
    }

    [Fact]
    public void APausedFocusKeepsItsRemainingTime()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        tracker.StartFocus(Morning, TimeSpan.FromMinutes(25));
        Run(tracker, Morning, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5));

        Assert.True(tracker.PauseFocus(Morning.AddMinutes(10)));
        Assert.False(tracker.PauseFocus(Morning.AddMinutes(11)));
        // 20 dakikalık telefon görüşmesi: odak bitmez, odak süresi sayılmaz.
        var during = Run(tracker, Morning.AddMinutes(10).AddSeconds(20), TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(during, item => item.Kind == RhythmEventKind.FocusEnded);
        Assert.InRange(tracker.Today.FocusMinutes, 9, 11);

        Assert.True(tracker.ResumeFocus(Morning.AddMinutes(30)));
        Assert.Equal(Morning.AddMinutes(45), tracker.FocusEnd);
        var after = Run(tracker, Morning.AddMinutes(30), TimeSpan.FromMinutes(16), TimeSpan.FromSeconds(5));
        var ended = Assert.Single(after, item => item.Kind == RhythmEventKind.FocusEnded);
        Assert.Equal(TimeSpan.FromMinutes(25), ended.Length);
    }

    [Fact]
    public void EndingAPausedFocusCountsOnlyTheFocusedPart()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        tracker.StartFocus(Morning, TimeSpan.FromMinutes(25));
        tracker.PauseFocus(Morning.AddMinutes(8));
        Assert.Equal(TimeSpan.FromMinutes(8), tracker.EndFocus(Morning.AddMinutes(40)));
        Assert.False(tracker.InFocus);
        Assert.Null(tracker.FocusPausedAt);
    }

    [Fact]
    public void ExtendingMovesTheEnd()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        Assert.False(tracker.ExtendFocus(TimeSpan.FromMinutes(10)));
        tracker.StartFocus(Morning, TimeSpan.FromMinutes(25));
        Assert.True(tracker.ExtendFocus(TimeSpan.FromMinutes(10)));
        Assert.Equal(Morning.AddMinutes(35), tracker.FocusEnd);
    }

    [Fact]
    public void ABreakEndsTheFocusAndAFocusEndsTheBreak()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        tracker.StartFocus(Morning, TimeSpan.FromMinutes(25));
        tracker.StartBreak(Morning.AddMinutes(5));
        Assert.False(tracker.InFocus);
        Assert.NotNull(tracker.BreakStart);

        tracker.StartFocus(Morning.AddMinutes(7), TimeSpan.FromMinutes(25));
        Assert.Null(tracker.BreakStart);
        Assert.True(tracker.InFocus);
    }

    [Fact]
    public void ASeriesRunsOnItsOwnClock()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(10));
        tracker.StartSeries(Morning, 2);
        Assert.Equal(1, tracker.SeriesRound);

        var events = Run(tracker, Morning, TimeSpan.FromMinutes(26), TimeSpan.FromSeconds(5));
        var rest = Assert.Single(events, item => item.Kind == RhythmEventKind.SeriesBreak);
        Assert.Equal((1, 2), (rest.Round, rest.Rounds));
        Assert.DoesNotContain(events, item => item.Kind == RhythmEventKind.FocusEnded);
        // Seri molası ayardaki 10 dakika değil, 5 dakika.
        Assert.Equal(TimeSpan.FromMinutes(5), tracker.CurrentBreakLength);

        events = Run(tracker, Morning.AddMinutes(26).AddSeconds(20), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5));
        var next = Assert.Single(events, item => item.Kind == RhythmEventKind.SeriesNext);
        Assert.Equal(2, next.Round);
        Assert.True(tracker.InFocus);

        events = Run(tracker, Morning.AddMinutes(31).AddSeconds(40), TimeSpan.FromMinutes(26), TimeSpan.FromSeconds(5));
        var done = Assert.Single(events, item => item.Kind == RhythmEventKind.SeriesDone);
        Assert.Equal(2, done.Rounds);
        Assert.Equal(0, tracker.SeriesRound);
        Assert.Null(tracker.BreakStart);
    }

    [Fact]
    public void TheNextRoundWaitsUntilTheUserIsBack()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        tracker.StartSeries(Morning, 3);
        Run(tracker, Morning, TimeSpan.FromMinutes(25).Add(TimeSpan.FromSeconds(20)), TimeSpan.FromSeconds(5));
        Assert.NotNull(tracker.BreakStart);

        // Molada 12 dakika masadan kalkıyor: o sırada yeni tur başlamaz, "hoş geldin" de denmez.
        var away = Morning.AddMinutes(26);
        var gone = new List<RhythmEvent>();
        for (var minute = 0; minute <= 12; minute++)
        {
            gone.AddRange(tracker.Update(away.AddMinutes(minute), TimeSpan.FromMinutes(minute)));
        }

        Assert.DoesNotContain(gone, item => item.Kind == RhythmEventKind.SeriesNext);
        var back = tracker.Update(away.AddMinutes(12).AddSeconds(20), TimeSpan.FromSeconds(1));
        Assert.Contains(back, item => item.Kind == RhythmEventKind.SeriesNext);
        Assert.DoesNotContain(back, item => item.Kind == RhythmEventKind.BackFromBreak);
        Assert.Equal(2, tracker.SeriesRound);
    }

    [Fact]
    public void SkippingTheSeriesBreakStartsTheNextRound()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        tracker.StartSeries(Morning, 4);
        Run(tracker, Morning, TimeSpan.FromMinutes(25).Add(TimeSpan.FromSeconds(20)), TimeSpan.FromSeconds(5));
        tracker.NextRound(Morning.AddMinutes(27));
        Assert.Equal(2, tracker.SeriesRound);
        Assert.Null(tracker.BreakStart);
        Assert.Equal(Morning.AddMinutes(52), tracker.FocusEnd);
    }

    [Fact]
    public void PausedFocusAndSeriesAreShown()
    {
        var now = Morning.AddMinutes(20);
        var paused = new RhythmSnapshot(true, Morning, Morning, Morning.AddMinutes(25), null, TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5), false, null,
            Morning.AddMinutes(10), 2, 4);
        var display = RhythmText.Describe(paused, now);
        Assert.Equal("Odak duraklatıldı", display.Label);
        Assert.Equal("15:00", display.Clock);
        Assert.StartsWith("Pomodoro 2/4.", display.Sentence, StringComparison.Ordinal);

        var rest = new RhythmSnapshot(true, null, null, null, now.AddMinutes(-1), TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5), false, null, null, 1, 4);
        Assert.Contains("sıradaki tur kendiliğinden", RhythmText.Describe(rest, now).Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void SilencedSuggestionsSayUntilWhen()
    {
        var now = Morning.AddMinutes(30);
        var snoozed = new RhythmSnapshot(true, Morning, null, null, null, TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5), false, now.AddHours(1));
        Assert.Contains("10:30'a kadar kapalı", RhythmText.Describe(snoozed, now).Sentence, StringComparison.Ordinal);
        var tomorrow = snoozed with { SnoozedUntil = now.Date.AddDays(1).AddHours(5) };
        Assert.StartsWith("Mola önerileri bugünlük kapalı", RhythmText.Describe(tomorrow, now).Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("odağı duraklat", "odak_duraklat", "")]
    [InlineData("odağa ara ver", "odak_duraklat", "")]
    [InlineData("pomodoroyu duraklat", "odak_duraklat", "")]
    [InlineData("odağa devam et", "odak_devam", "")]
    [InlineData("odağı sürdür", "odak_devam", "")]
    [InlineData("odağı 15 dakika uzat", "odak_uzat", "15")]
    [InlineData("odağı uzat", "odak_uzat", "10")]
    [InlineData("on dakika daha odaklanayım", "odak_uzat", "10")]
    [InlineData("odağı bitir", "odak_bitir", "")]
    [InlineData("telefon çaldı, odağı bir dakika beklet", "odak_duraklat", "")]
    [InlineData("tamam döndüm, kaldığım yerden odaklanmaya devam edelim", "odak_devam", "")]
    [InlineData("geldim, sayaç kaldığı yerden sürsün", "odak_devam", "")]
    public void RecognizesFocusControls(string sentence, string action, string minutes)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("ritim", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(minutes, command.Arguments.GetValueOrDefault("dakika", ""));
    }

    [Fact]
    public void FocusSessionsAreRecordedWithTheirTask()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        tracker.StartFocus(Morning, TimeSpan.FromMinutes(25), "Raporu bitir");
        Run(tracker, Morning, TimeSpan.FromMinutes(26), TimeSpan.FromSeconds(5));
        tracker.StartFocus(Morning.AddMinutes(30), TimeSpan.FromMinutes(50));
        tracker.PauseFocus(Morning.AddMinutes(40));
        tracker.EndFocus(Morning.AddMinutes(55));
        // Bir dakikadan kısa odak sayılmaz.
        tracker.StartFocus(Morning.AddMinutes(60), TimeSpan.FromMinutes(25), "Sunum");
        tracker.StartBreak(Morning.AddMinutes(60).AddSeconds(30));

        Assert.Equal(2, tracker.Today.Sessions.Count);
        Assert.Equal(("Raporu bitir", 25.0), (tracker.Today.Sessions[0].Task, tracker.Today.Sessions[0].Minutes));
        Assert.Equal(("", 10.0), (tracker.Today.Sessions[1].Task, tracker.Today.Sessions[1].Minutes));
        Assert.Equal("", tracker.FocusTask);
    }

    [Fact]
    public void SeriesRoundsKeepTheTask()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        tracker.StartSeries(Morning, 2, "Sunum");
        Run(tracker, Morning, TimeSpan.FromMinutes(56), TimeSpan.FromSeconds(5));
        Assert.Equal(2, tracker.Today.Sessions.Count);
        Assert.All(tracker.Today.Sessions, session => Assert.Equal("Sunum", session.Task));
    }

    [Fact]
    public void DescribesWhereTheFocusWent()
    {
        FocusSession[] sessions =
        [
            new(Morning, 25, "Raporu bitir"),
            new(Morning.AddHours(1), 50, "raporu bitir"),
            new(Morning.AddHours(3), 25, "Sunum hazırla"),
            new(Morning.AddHours(4), 15, ""),
        ];
        Assert.Equal("Bu hafta \"raporu bitir\" için 1 sa 15 dk odaklandın, 2 oturumda.", FocusLog.Describe(sessions, "rapora", "Bu hafta"));
        Assert.Equal("Bugün 4 oturumda toplam 1 sa 55 dk odaklandın: raporu bitir 1 sa 15 dk, sunum hazırla 25 dk, iş belirtmeden 15 dk.",
            FocusLog.Describe(sessions, "", "Bugün"));
        Assert.StartsWith("Bugün \"fatura\" için bir odak oturumu yok.", FocusLog.Describe(sessions, "fatura", "Bugün"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bu hafta rapora ne kadar çalıştım", "rapor", "hafta")]
    [InlineData("sunuma kaç saat odaklandım", "sunum", "hafta")]
    [InlineData("bugün faturalar için ne kadar çalıştım", "faturalar", "bugun")]
    [InlineData("bugün neye odaklandım", "", "bugun")]
    [InlineData("bu ay en çok neye çalıştım", "", "ay")]
    [InlineData("odak oturumlarım", "", "bugun")]
    [InlineData("bu hafta en çok hangi işe vakit ayırmışım", "", "hafta")]
    public void AsksWhereTheFocusWent(string sentence, string task, string period)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("ritim", command?.Tool);
        Assert.Equal("odaklar", command!.Arguments["islem"]);
        Assert.Equal(task, command.Arguments.GetValueOrDefault("is", ""));
        Assert.Equal(period, command.Arguments["donem"]);
    }

    [Theory]
    [InlineData("bugün ne kadar çalıştım", "durum")]
    [InlineData("bu hafta ne kadar çalıştım", "hafta")]
    public void LeavesTheDaySummaryAlone(string sentence, string action) => Assert.Equal(action, QuickIntents.Match(sentence)?.Arguments["islem"]);

    [Fact]
    public void ARestartKeepsWhatTheUserStarted()
    {
        var before = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        before.StartSeries(Morning, 4, "Sunum");
        before.PauseFocus(Morning.AddMinutes(10));
        before.Snooze(Morning, TimeSpan.FromHours(20));
        var live = before.Live();

        // Friday bir saat sonra yeniden açılıyor: duraklatılmış tur, seri ve susturma yerinde.
        var after = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        after.Restore(live, Morning.AddHours(1));
        Assert.Equal(Morning.AddMinutes(10), after.FocusPausedAt);
        Assert.Equal((1, 4, "Sunum"), (after.SeriesRound, after.SeriesRounds, after.FocusTask));
        Assert.Equal(Morning.AddHours(20), after.SnoozedUntil);
        Assert.True(after.ResumeFocus(Morning.AddHours(1)));
        Assert.Equal(Morning.AddHours(1).AddMinutes(15), after.FocusEnd);
    }

    [Fact]
    public void WhatEndedWhileClosedIsNotRestored()
    {
        var before = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        before.StartFocus(Morning, TimeSpan.FromMinutes(25), "Rapor");
        before.Snooze(Morning, TimeSpan.FromMinutes(30));
        var after = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        after.Restore(before.Live(), Morning.AddHours(2));
        Assert.False(after.InFocus);
        Assert.Null(after.SnoozedUntil);

        var resting = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        resting.StartBreak(Morning);
        var back = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        back.Restore(resting.Live(), Morning.AddMinutes(3));
        Assert.Equal(Morning, back.BreakStart);
        back = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        back.Restore(resting.Live(), Morning.AddMinutes(10));
        Assert.Null(back.BreakStart);
    }

    [Fact]
    public void ABreakCanBeExtended()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        Assert.False(tracker.ExtendBreak(TimeSpan.FromMinutes(5)));
        tracker.Update(Morning, TimeSpan.Zero);
        tracker.StartBreak(Morning);
        Assert.True(tracker.ExtendBreak(TimeSpan.FromMinutes(5)));
        var events = Run(tracker, Morning, TimeSpan.FromMinutes(11), TimeSpan.FromSeconds(3));
        var over = Assert.Single(events, item => item.Kind == RhythmEventKind.BreakOver);
        Assert.True(over.Length >= TimeSpan.FromMinutes(10));
    }

    [Theory]
    [InlineData("molayı 5 dakika uzat", "5")]
    [InlineData("molayı uzat", "5")]
    [InlineData("on dakika daha mola", "10")]
    public void RecognizesBreakExtension(string sentence, string minutes)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("mola_uzat", command?.Arguments["islem"]);
        Assert.Equal(minutes, command!.Arguments["dakika"]);
    }
}
