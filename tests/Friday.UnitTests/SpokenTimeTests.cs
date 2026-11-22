using System.Globalization;
using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// Hatırlatmalarda söylenen zamanlar. Ölçüm (cumartesi 21 Kasım 2026, 14:00): 30 söyleyişten 18'i doğruydu; "23 Kasım'da"
/// bugün 23:00'e, "2 Aralık akşam 7'de" gece 02:00'ye, "yarın on bir buçukta" 09:00'a sessizce kuruluyordu.
/// </summary>
public class SpokenTimeTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 14, 0, 0);

    [Theory]
    [InlineData("yarın 9'da", "2026-11-22 09:00")]
    [InlineData("akşam 8'de", "2026-11-21 20:00")]
    [InlineData("2 saat sonra", "2026-11-21 16:00")]
    [InlineData("yarım saat sonra", "2026-11-21 14:30")]
    [InlineData("bir buçuk saat sonra", "2026-11-21 15:30")]
    [InlineData("on beş dakika sonra", "2026-11-21 14:15")]
    [InlineData("üç buçukta", "2026-11-21 15:30")]
    [InlineData("yarın sabah dokuzda", "2026-11-22 09:00")]
    [InlineData("yarın sabah yedide", "2026-11-22 07:00")]
    [InlineData("akşam sekizde", "2026-11-21 20:00")]
    [InlineData("saat üçte", "2026-11-21 15:00")]
    [InlineData("beşe çeyrek kala", "2026-11-21 16:45")]
    [InlineData("dokuzu on geçe", "2026-11-21 21:10")]
    [InlineData("dörde beş var", "2026-11-21 15:55")]
    [InlineData("dördü çeyrek geçe", "2026-11-21 16:15")]
    [InlineData("yarın on bir buçukta", "2026-11-22 11:30")]
    [InlineData("bu akşam", "2026-11-21 20:00")]
    [InlineData("bu gece", "2026-11-21 22:00")]
    [InlineData("akşamüstü", "2026-11-21 17:00")]
    [InlineData("yarın öğleden sonra", "2026-11-22 15:00")]
    [InlineData("haftaya salı", "2026-11-24 09:00")]
    [InlineData("pazartesi 10:30'da", "2026-11-23 10:30")]
    [InlineData("ayın 15'inde", "2026-12-15 09:00")]
    [InlineData("ayın on beşinde", "2026-12-15 09:00")]
    [InlineData("23 Kasım'da", "2026-11-23 09:00")]
    [InlineData("28 Kasım saat 14:00'te", "2026-11-28 14:00")]
    [InlineData("2 Aralık akşam 7'de", "2026-12-02 19:00")]
    [InlineData("3 Ocak", "2027-01-03 09:00")]
    [InlineData("2026-11-23 09:00", "2026-11-23 09:00")]
    [InlineData("23.11.2026 18:30", "2026-11-23 18:30")]
    [InlineData("hafta sonu", "2026-11-22 10:00")]
    [InlineData("iki gün sonra", "2026-11-23 14:00")]
    [InlineData("öbür gün sabah", "2026-11-23 09:00")]
    [InlineData("cuma öğleden sonra 4'te", "2026-11-27 16:00")]
    [InlineData("saat 15:30'da", "2026-11-21 15:30")]
    [InlineData("saat 15.30'da", "2026-11-21 15:30")]
    [InlineData("bir saat sonra", "2026-11-21 15:00")]
    [InlineData("yarın 3'te", "2026-11-22 15:00")]
    [InlineData("yarın sabah 6'da", "2026-11-22 06:00")]
    [InlineData("25 Kasım 4'te", "2026-11-25 16:00")]
    public void UnderstandsSpokenTimes(string said, string expected) =>
        Assert.Equal(expected, TurkishTime.Parse(said, Now)?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("ahmet'e onu sor", "ahmet'e onu sor")]
    [InlineData("ona kitabı ver", "ona kitabı ver")]
    [InlineData("saat onda", "saat 10'da")]
    [InlineData("sabah dokuzda", "sabah 9'da")]
    public void TurnsOnlyClockNumbersIntoDigits(string text, string expected) => Assert.Equal(expected, TurkishTime.Spoken(text));

    [Theory]
    [InlineData("bir ara")]
    [InlineData("sonra")]
    [InlineData("31 Şubat'ta")]
    public void HasNoTimeWhenNoneIsSaid(string said) => Assert.Null(TurkishTime.Parse(said, Now));

    [Theory]
    [InlineData("23 kasım'da annemi aramayı hatırlat", "23 kasım'da")]
    [InlineData("ayın 15'inde kirayı hatırlat", "ayın 15'inde")]
    [InlineData("2 aralık akşam 7'de toplantıyı hatırlat", "2 aralık akşam 7'de")]
    [InlineData("yarın on bir buçukta dişçiye gitmem gerektiğini hatırlat", "yarın 11:30")]
    [InlineData("akşam sekizde ilacımı hatırlat", "akşam 8'de")]
    public void TheQuickPathTakesDates(string said, string when)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("hatirlatici", command?.Tool);
        Assert.Equal(when, command!.Arguments["zaman"]);
    }
}
