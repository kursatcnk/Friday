using System.Globalization;
using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// İşlerde söylenen son tarihler. Ölçüm (cumartesi 21 Kasım 2026): 14 söyleyişten 6'sı doğruydu; "ayın 30'una kadar",
/// "3 Aralık'ta", "2 gün içinde" tanınmıyor, "haftaya", "akşama kadar" iş metninde kalıyordu.
/// </summary>
public class TodoDeadlineTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 14, 0, 0);

    [Theory]
    [InlineData("cumaya kadar raporu bitir", "raporu bitir", "2026-11-27")]
    [InlineData("faturayı 25 Kasım'a kadar öde", "faturayı öde", "2026-11-25")]
    [InlineData("raporu ayın 30'una kadar bitir", "raporu bitir", "2026-11-30")]
    [InlineData("haftaya salıya kadar sunumu hazırla", "sunumu hazırla", "2026-11-24")]
    [InlineData("kirayı ayın 5'inde öde", "kirayı öde", "2026-12-05")]
    [InlineData("vergiyi ay sonuna kadar ver", "vergiyi ver", "2026-11-30")]
    [InlineData("yarın akşama kadar mail at", "mail at", "2026-11-22")]
    [InlineData("2 gün içinde dönüş yap", "dönüş yap", "2026-11-23")]
    [InlineData("bu hafta içinde bankaya git", "bankaya git", "2026-11-22")]
    [InlineData("pazartesiye kadar formu doldur", "formu doldur", "2026-11-23")]
    [InlineData("3 Aralık'ta doktora git", "doktora git", "2026-12-03")]
    [InlineData("hafta sonuna kadar odayı topla", "odayı topla", "2026-11-21")]
    [InlineData("ayın 15'ine kadar aidatı öde", "aidatı öde", "2026-12-15")]
    [InlineData("yarın bankaya git", "bankaya git", "2026-11-22")]
    public void FindsTheDeadlineAndCleansTheTask(string said, string text, string due)
    {
        var (clean, got, _) = TodoDue.Extract(said, Now);
        Assert.Equal(text, clean, ignoreCase: true);
        Assert.Equal(due, got?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("süt al")]
    [InlineData("annemi ara")]
    public void LeavesTasksWithoutADateAlone(string said) => Assert.Null(TodoDue.Extract(said, Now).Due);
}
