using Friday.Core;

namespace Friday.UnitTests;

public class RhythmTrackerTests
{
    private static readonly DateTime Morning = new(2026, 11, 17, 9, 0, 0);

    /// <summary>Her 20 sn'de bir yoklama; kullanıcı yazıyorsa boşta kalma süresi birkaç saniyedir.</summary>
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
    public void SuggestsABreakAfterAContinuousStretchOnlyInANaturalPause()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        // Sürekli yazıyor (boşta 0 sn): 50. dakikada öneri gelmez, araya girilmez.
        Assert.DoesNotContain(Run(tracker, Morning, TimeSpan.FromMinutes(55), TimeSpan.Zero), item => item.Kind == RhythmEventKind.SuggestBreak);
        // İlk duraksamada öneri gelir.
        var events = tracker.Update(Morning.AddMinutes(55).AddSeconds(20), TimeSpan.FromSeconds(4));
        Assert.Contains(events, item => item.Kind == RhythmEventKind.SuggestBreak);
    }

    [Fact]
    public void InsistsIfTheUserNeverPauses()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        var events = Run(tracker, Morning, TimeSpan.FromMinutes(66), TimeSpan.Zero);
        Assert.Single(events, item => item.Kind == RhythmEventKind.SuggestBreak);
    }

    [Fact]
    public void WelcomesBackAfterASuggestedBreakAndCountsIt()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        Run(tracker, Morning, TimeSpan.FromMinutes(51), TimeSpan.FromSeconds(5));
        // Kullanıcı 7 dakika kalkıyor.
        var away = Morning.AddMinutes(51);
        for (var minute = 0; minute <= 7; minute++)
        {
            tracker.Update(away.AddMinutes(minute), TimeSpan.FromMinutes(minute));
        }

        var back = tracker.Update(away.AddMinutes(7).AddSeconds(20), TimeSpan.FromSeconds(1));
        var welcome = Assert.Single(back, item => item.Kind == RhythmEventKind.BackFromBreak);
        Assert.True(welcome.Length >= TimeSpan.FromMinutes(6));
        Assert.Equal(1, tracker.Today.Breaks);
        Assert.True(tracker.CurrentStretch(away.AddMinutes(8)) < TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void StaysQuietDuringFocusAndAnnouncesItsEnd()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5));
        tracker.StartFocus(Morning, TimeSpan.FromMinutes(25));
        var events = Run(tracker, Morning, TimeSpan.FromMinutes(26), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(events, item => item.Kind == RhythmEventKind.SuggestBreak);
        var ended = Assert.Single(events, item => item.Kind == RhythmEventKind.FocusEnded);
        Assert.Equal(TimeSpan.FromMinutes(25), ended.Length);
        Assert.InRange(tracker.Today.FocusMinutes, 24, 26);
        Assert.Contains(tracker.Today.Segments, segment => segment.Kind == RhythmSegmentKind.Focus);
    }

    [Fact]
    public void ABreakThatNeverHappensEndsOnTime()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        tracker.Update(Morning, TimeSpan.Zero);
        tracker.StartBreak(Morning);
        var events = Run(tracker, Morning, TimeSpan.FromMinutes(6), TimeSpan.FromSeconds(3));
        Assert.Single(events, item => item.Kind == RhythmEventKind.BreakOver);
    }

    [Fact]
    public void SnoozeDelaysTheNextSuggestion()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        Run(tracker, Morning, TimeSpan.FromMinutes(40), TimeSpan.FromSeconds(5));
        tracker.Snooze(Morning.AddMinutes(40), TimeSpan.FromMinutes(30));
        var events = Run(tracker, Morning.AddMinutes(40), TimeSpan.FromMinutes(25), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(events, item => item.Kind == RhythmEventKind.SuggestBreak);
    }

    [Fact]
    public void GreetsTheFirstSittingOfTheMorning()
    {
        var tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        var evening = new DateTime(2026, 11, 16, 23, 0, 0);
        tracker.Update(evening, TimeSpan.Zero);
        tracker.Update(evening.AddMinutes(5), TimeSpan.FromMinutes(5));
        var morning = new DateTime(2026, 11, 17, 8, 30, 0);
        var events = tracker.Update(morning, TimeSpan.FromSeconds(1));
        Assert.Contains(events, item => item.Kind == RhythmEventKind.DayStarted);
        Assert.Equal(new DateOnly(2026, 11, 17), tracker.Today.Date);
    }

    [Fact]
    public void CoachNeverRepeatsTheSameSuggestionTwiceInARow()
    {
        var lines = new RhythmCoachLines(new Random(1));
        var day = new RhythmDay { Date = DateOnly.FromDateTime(Morning) };
        var previous = "";
        for (var index = 0; index < 20; index++)
        {
            var first = lines.Suggestion(TimeSpan.FromMinutes(55), Morning.AddHours(3), day)[0].Text;
            Assert.NotEqual(previous, first);
            previous = first;
        }
    }

    [Fact]
    public void LongStretchesSuggestAWalkAndLateNightsAWindDown()
    {
        var lines = new RhythmCoachLines(new Random(1));
        var day = new RhythmDay { Date = DateOnly.FromDateTime(Morning), ActiveMinutes = 420 };
        Assert.Contains("yürü", lines.Suggestion(TimeSpan.FromMinutes(95), Morning.AddHours(4), day)[0].Text, StringComparison.Ordinal);
        Assert.Contains("23:10", lines.Suggestion(TimeSpan.FromMinutes(60), Morning.Date.AddHours(23).AddMinutes(10), day)[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BreathingIsAGuidedScriptWithPauses()
    {
        var script = RhythmCoachLines.Breathing();
        Assert.True(script.Count >= 10);
        Assert.Contains(script, step => step.PauseSeconds >= 4);
    }

    [Fact]
    public void DescribesTheWindowToReturnTo()
    {
        Assert.Equal("Visual Studio Code, FridayAssistant.cs", RhythmCoachLines.DescribeWindow("FridayAssistant.cs - Friday - Visual Studio Code", "Code"));
        Assert.Null(RhythmCoachLines.DescribeWindow("", "explorer"));
    }

    [Fact]
    public void SummarizesTheDay()
    {
        var day = new RhythmDay { ActiveMinutes = 250, FocusMinutes = 75, Breaks = 3, LongestStretchMinutes = 80, Water = 4 };
        var status = RhythmCoachLines.Status(day, TimeSpan.FromMinutes(25));
        Assert.Contains("4 saat 10 dakika", status, StringComparison.Ordinal);
        Assert.Contains("3 mola", status, StringComparison.Ordinal);
        Assert.Contains("4 bardak", status, StringComparison.Ordinal);
    }
}

public class MemoryImportTests
{
    [Fact]
    public void ReadsAChatGptMemoryList()
    {
        const string text = """
            Here is what I remember about you:

            - Is a software developer living in Istanbul.
            - Prefers concise answers.
            - Has a daughter whose birthday is on March 3.
            - Is building a voice assistant called Friday
              in C# and WPF.
            """;
        var items = MemoryImport.Parse(text);
        Assert.Equal(4, items.Count);
        Assert.Equal("Is building a voice assistant called Friday in C# and WPF.", items[3]);
        Assert.Equal("ChatGPT", MemoryImport.GuessSource(text, "chatgpt-memories.txt"));
    }

    [Fact]
    public void ReadsNumberedTurkishMarkdown()
    {
        const string text = """
            # Claude'un hafızası

            1. **Kahveyi** şekersiz içer.
            2. Kızının doğum günü 3 Mart.
            3) Hafta sonları bisiklete biner.
            """;
        Assert.Equal(["Kahveyi şekersiz içer.", "Kızının doğum günü 3 Mart.", "Hafta sonları bisiklete biner."], MemoryImport.Parse(text));
    }

    [Fact]
    public void ReadsJsonExports()
    {
        const string json = """
            {"memories": [{"id": 1, "content": "Works at a fintech startup."}, {"id": 2, "content": "Likes jazz."}, {"content": "Likes jazz."}]}
            """;
        Assert.Equal(["Works at a fintech startup.", "Likes jazz."], MemoryImport.Parse(json));
        Assert.Equal(["Birinci bilgi", "İkinci bilgi"], MemoryImport.Parse("""["birinci bilgi", "ikinci bilgi"]"""));
    }

    [Fact]
    public void MarksWhatFridayAlreadyKnows()
    {
        var known = new[] { new MemoryItem("a", "Kahveyi şekersiz içer.", DateTimeOffset.Now) };
        var preview = MemoryImport.Preview("- kahveyi şekersiz içer\n- Kedisinin adı Pamuk.", known);
        Assert.True(preview[0].AlreadyKnown);
        Assert.False(preview[1].AlreadyKnown);
    }

    [Fact]
    public void ImportSkipsDuplicatesAndKeepsTheSource()
    {
        using var folder = new TempDirectory();
        var store = new MemoryStore(Path.Combine(folder.Path, "memory.json"));
        store.Remember("Kahveyi şekersiz içer.");
        var added = store.Import(["kahveyi şekersiz içer", "Kedisinin adı Pamuk.", "Kedisinin adı Pamuk"], "ChatGPT");
        Assert.Equal(1, added);
        Assert.Equal("ChatGPT", store.Items[^1].Source);
        Assert.Contains("- Kedisinin adı Pamuk.", store.ToMarkdown("Friday"), StringComparison.Ordinal);
        Assert.StartsWith("Aşağıdakiler benim hakkımda", store.ToPortablePrompt(), StringComparison.Ordinal);
        // Kaynak alanı olmayan eski dosyalar da okunur.
        Assert.Null(new MemoryStore(Path.Combine(folder.Path, "memory.json")).Items[0].Source);
    }
}

public class MemoryDateTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    [Theory]
    [InlineData("Kullanıcının arabasının muayenesi 20 Kasım'da.", "3 gün kaldı")]
    [InlineData("Toplantı 17 kasım", "Bugün")]
    [InlineData("Annesinin doğum günü 18 Kasım", "Yarın")]
    [InlineData("Kızının doğum günü 3 Mart.", null)]
    [InlineData("Kahveyi şekersiz içer.", null)]
    public void CountsDownToUpcomingDates(string text, string? expected) =>
        Assert.Equal(expected, MemoryDates.Countdown(text, Today));

    [Fact]
    public void RollsYearlyDatesOver()
    {
        Assert.Equal(new DateOnly(2027, 3, 3), MemoryDates.NextOccurrence("Kızının doğum günü 3 Mart", Today));
        Assert.Equal(new DateOnly(2027, 1, 5), MemoryDates.NextOccurrence("Sınav 5 Ocak 2027", Today));
        Assert.Null(MemoryDates.NextOccurrence("Geçen yıl 5 Ocak 2025", Today));
    }
}
