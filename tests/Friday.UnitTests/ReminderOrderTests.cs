using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// Hatırlatmada sözcük sırası. Türkçede iş çoğu kez zamandan önce söylenir ("dişçiyi yarın 9'da hatırlat"); hızlı yol
/// yalnızca zamanı başta tanıyordu ve 14 söyleyişten 8'i dil modeline gidiyordu.
/// </summary>
public class ReminderOrderTests
{
    [Theory]
    [InlineData("yarın 9'da dişçiyi hatırlat", "yarın 9'da", "dişçiyi")]
    [InlineData("dişçiyi yarın 9'da hatırlat", "yarın 9'da", "dişçiyi")]
    [InlineData("bana yarın 9'da dişçiyi hatırlat", "yarın 9'da", "dişçiyi")]
    [InlineData("dişçiyi aramamı yarın sabah 9'da hatırlat", "yarın sabah 9'da", "dişçiyi aramamı")]
    [InlineData("annemi aramayı akşam 8'de hatırlat", "akşam 8'de", "annemi aramayı")]
    [InlineData("ilacımı saat 10'da hatırlat", "saat 10'da", "ilacımı")]
    [InlineData("toplantıyı pazartesi 10:30'da hatırlat", "pazartesi 10:30'da", "toplantıyı")]
    [InlineData("faturayı ödemeyi ayın 15'inde hatırlat", "ayın 15'inde", "faturayı ödemeyi")]
    [InlineData("dişçiyi yarın hatırlat", "yarın", "dişçiyi")]
    [InlineData("cuma günü raporu göndermeyi hatırlat", "cuma günü", "raporu göndermeyi")]
    [InlineData("raporu göndermeyi cuma günü hatırlat", "cuma günü", "raporu göndermeyi")]
    [InlineData("yarın sabah 9'da bana dişçiyi aramamı hatırlatır mısın", "yarın sabah 9'da", "dişçiyi aramamı")]
    public void TakesEitherOrder(string said, string when, string note)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("hatirlatici", command?.Tool);
        Assert.Equal(when, command!.Arguments["zaman"]);
        Assert.Equal(note, command.Arguments["metin"]);
    }

    [Theory]
    [InlineData("bunu yarın hatırlat")]
    [InlineData("onu akşam 8'de hatırlat")]
    public void LeavesAPronounToTheModel(string said) => Assert.NotEqual("hatirlatici", QuickIntents.Match(said)?.Tool);
}
