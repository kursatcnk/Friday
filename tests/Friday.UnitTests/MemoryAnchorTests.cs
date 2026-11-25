using Friday.Core;

namespace Friday.UnitTests;

public class MemoryAnchorTests
{
    // 2026-11-21 bir cumartesi.
    private static readonly DateTime Saturday = new(2026, 11, 21, 15, 0, 0);

    [Theory]
    [InlineData("yarın Ahmet'le toplantım var", "22 Kasım'da Ahmet'le toplantım var")]
    [InlineData("Yarınki sunum çok önemli", "22 Kasım'daki sunum çok önemli")]
    [InlineData("raporu yarına kadar bitirmeliyim", "raporu 22 Kasım'a kadar bitirmeliyim")]
    [InlineData("bugün koşuya başladım", "21 Kasım'da koşuya başladım")]
    [InlineData("dünkü maçı kaybettik", "20 Kasım'daki maçı kaybettik")]
    [InlineData("öbür gün annem gelecek", "23 Kasım'da annem gelecek")]
    [InlineData("gelecek cuma doğum günü partisi var", "27 Kasım Cuma doğum günü partisi var")]
    [InlineData("geçen salı günü sinemaya gittik", "17 Kasım Salı günü sinemaya gittik")]
    [InlineData("bu cumartesi kamp var", "21 Kasım Cumartesi kamp var")]
    public void TurnsRelativeDaysIntoDates(string text, string expected) =>
        Assert.Equal(expected, MemoryAnchor.Anchor(text, Saturday));

    [Theory]
    [InlineData("Dünya kupasını izlemeyi severim")]
    [InlineData("Kızımın doğum günü 3 Mart")]
    [InlineData("yarınlar güzel olacak")]
    [InlineData("Her cuma yüzmeye gidiyorum")]
    public void LeavesOtherWordsAlone(string text) => Assert.Equal(text, MemoryAnchor.Anchor(text, Saturday));

    [Fact]
    public void AnchoredMemoriesShowUpAsUpcoming()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "friday-anchor-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var memory = new MemoryStore(file);
            var item = memory.Remember("yarın diş hekimi randevum var");
            var tomorrow = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
            Assert.DoesNotContain("yarın", item.Text, StringComparison.Ordinal);
            Assert.Equal("Yarın", MemoryDates.Countdown(item.Text, DateOnly.FromDateTime(DateTime.Now)));
            Assert.StartsWith($"{tomorrow.Day} ", item.Text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
