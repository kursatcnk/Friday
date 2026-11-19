using Friday.Core;

namespace Friday.UnitTests;

public class MemoryRecallTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    private static MemoryItem Item(string text, int day) => new(Guid.NewGuid().ToString("N")[..8], text, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero).AddDays(day));

    /// <summary>Başka bir yapay zekâdan aktarılmış gibi 300 bilgi; aranan bilgi en eskilerden biri.</summary>
    private static List<MemoryItem> Imported()
    {
        var memories = new List<MemoryItem>
        {
            Item("Kullanıcının kızı Elif, Moda İlkokulu'nda 3. sınıfa gidiyor.", 0),
            Item("Kullanıcı laktoz intoleransı nedeniyle süt ürünlerinden kaçınıyor.", 1),
            Item("Kullanıcının arabası 2019 model beyaz bir Renault Clio.", 2)
        };
        for (var index = 0; index < 300; index++)
        {
            memories.Add(Item($"Kullanıcı {index}. projede yazılım mimarisi üzerine çalıştı.", 10 + index));
        }

        return memories;
    }

    [Fact]
    public void KeepsOnlyRecentAndDatedMemoriesInThePrompt()
    {
        var memories = Imported();
        memories.Insert(1, Item("Kullanıcının annesinin doğum günü 25 Kasım.", 1));
        var pinned = MemoryRecall.PinnedItems(memories, Today);
        Assert.Equal(MemoryRecall.Pinned + 1, pinned.Count);
        Assert.Contains(pinned, memory => memory.Text.Contains("doğum günü", StringComparison.Ordinal));
        Assert.DoesNotContain(pinned, memory => memory.Text.Contains("Elif", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("kızımın okulu nerede?", "Elif")]
    [InlineData("bana sütlü bir tatlı tarifi önerir misin", "laktoz")]
    [InlineData("arabamın modeli neydi", "Clio")]
    public void FindsOldMemoriesThatMatchTheQuestion(string question, string expected)
    {
        var memories = Imported();
        var recalled = MemoryRecall.Relevant(memories, question, MemoryRecall.PinnedItems(memories, Today));
        Assert.NotEmpty(recalled);
        Assert.Contains(expected, recalled[0].Text, StringComparison.Ordinal);
        Assert.True(recalled.Count <= MemoryRecall.Recalled);
    }

    [Fact]
    public void AddsNothingForUnrelatedQuestions()
    {
        var memories = Imported();
        Assert.Empty(MemoryRecall.Relevant(memories, "Güneş neden sarı görünür?", MemoryRecall.PinnedItems(memories, Today)));
        Assert.Equal("", MemoryRecall.Context([]));
        Assert.StartsWith("\n[Hafızadan, bu soruyla ilgili olabilir: Kullanıcının", MemoryRecall.Context([memories[0]]), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildsAShortStablePrompt()
    {
        var settings = new FridaySettings();
        var memories = Imported();
        var prompt = SystemPrompt.Build(settings, memories, new DateTime(2026, 11, 19, 10, 0, 0));
        Assert.DoesNotContain("Elif", prompt, StringComparison.Ordinal);
        Assert.Equal(prompt, SystemPrompt.Build(settings, memories, new DateTime(2026, 11, 19, 18, 30, 0)));
    }
}
