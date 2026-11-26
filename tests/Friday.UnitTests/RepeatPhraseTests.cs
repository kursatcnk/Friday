using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// Dil modeli tekrarı zaman alanına yazar ("her sabah 08:00"); hatırlatma tek seferlik değil, tekrarlı kurulmalı.
/// Ölçümde "her sabah sekizde su içmemi hatırlatır mısın" yarın 08:00'e tek seferlik kuruluyordu.
/// </summary>
public class RepeatPhraseTests
{
    [Theory]
    [InlineData("her sabah 08:00", "gunluk", "sabah 08:00")]
    [InlineData("her gün 9'da", "gunluk", "9'da")]
    [InlineData("her pazartesi 10'da", "haftalik", "pazartesi 10'da")]
    [InlineData("hafta içi her gün 8:30", "haftaici", "8:30")]
    [InlineData("Her akşam 21:00", "gunluk", "akşam 21:00")]
    public void ReadsTheRepeatFromTheTime(string phrase, string rule, string when)
    {
        var found = RepeatRule.FromPhrase(phrase);
        Assert.Equal((rule, when), found);
    }

    [Theory]
    [InlineData("yarın 9'da")]
    [InlineData("akşam 8'de")]
    [InlineData("pazartesi 10:30'da")]
    public void LeavesOneOffTimesAlone(string phrase) => Assert.Null(RepeatRule.FromPhrase(phrase));
}
