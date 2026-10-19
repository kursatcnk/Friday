using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class SentenceChunkerTests
{
    [Fact]
    public void EmitsSentencesAsSoonAsTheyEnd()
    {
        var chunker = new SentenceChunker();
        Assert.Empty(chunker.Push("Bugün İstanbul'da hava"));
        Assert.Equal(["Bugün İstanbul'da hava güneşli."], chunker.Push(" güneşli. Yarın"));
        Assert.Empty(chunker.Push(" yağmur bekleniyor"));
        Assert.Equal("Yarın yağmur bekleniyor", chunker.Flush());
    }

    [Fact]
    public void DoesNotSplitDecimalsOrAddresses()
    {
        var chunker = new SentenceChunker();
        Assert.Empty(chunker.Push("Sıcaklık 3.5 derece, site www.ornek.com adresinde"));
        Assert.Equal("Sıcaklık 3.5 derece, site www.ornek.com adresinde", chunker.Flush());
    }

    [Fact]
    public void JoinsVeryShortSentencesWithTheNextOne()
    {
        var chunker = new SentenceChunker();
        Assert.Equal(["Tamam. Not defterini açtım."], chunker.Push("Tamam. Not defterini açtım. "));
    }
}

public class SpokenTextTests
{
    [Theory]
    [InlineData("**Merhaba!** 😊 Nasılsın?", "Merhaba! Nasılsın?")]
    [InlineData("- Birinci\n- İkinci", "Birinci İkinci")]
    [InlineData("Detaylar için [buraya](https://ornek.com) bak.", "Detaylar için buraya bak.")]
    [InlineData("Adres: https://ornek.com/a?b=1", "Adres: bağlantı")]
    [InlineData("Çğıöşü ÇĞİÖŞÜ", "Çğıöşü ÇĞİÖŞÜ")]
    public void CleansTextForSpeech(string input, string expected) =>
        Assert.Equal(expected, SpokenText.Clean(input));
}

public class TurkishNumberTests
{
    [Theory]
    [InlineData(0, "sıfır")]
    [InlineData(7, "yedi")]
    [InlineData(16, "on altı")]
    [InlineData(100, "yüz")]
    [InlineData(250, "iki yüz elli")]
    [InlineData(1000, "bin")]
    [InlineData(1905, "bin dokuz yüz beş")]
    [InlineData(2026, "iki bin yirmi altı")]
    [InlineData(1_000_000, "bir milyon")]
    [InlineData(85_300_000, "seksen beş milyon üç yüz bin")]
    public void IntegersAreSpelledInTurkish(long number, string expected) =>
        Assert.Equal(expected, TurkishNumbers.ToWords(number));

    [Theory]
    [InlineData("Saat 11:05.", "Saat on bir sıfır beş.")]
    [InlineData("Saat 16:00 oldu", "Saat on altı oldu")]
    [InlineData("Bugün 19 Ekim 2026.", "Bugün on dokuz Ekim iki bin yirmi altı.")]
    [InlineData("Yağış ihtimali %20, sıcaklık -3 derece.", "Yağış ihtimali yüzde yirmi, sıcaklık eksi üç derece.")]
    [InlineData("Dolar 41,25 lira.", "Dolar kırk bir virgül yirmi beş lira.")]
    [InlineData("Nüfus 1.250.000 kişi.", "Nüfus bir milyon iki yüz elli bin kişi.")]
    [InlineData("İstanbul'da 17 derece", "İstanbul'da on yedi derece")]
    public void NumbersInSentencesAreReadAloud(string input, string expected) =>
        Assert.Equal(expected, SpokenText.NumbersToWords(input));
}

public class TranscriptFilterTests
{
    [Theory]
    [InlineData("Altyazı M.K.")]
    [InlineData(" İzlediğiniz için teşekkür ederim. ")]
    [InlineData("   ")]
    public void DropsWhisperHallucinations(string transcript) =>
        Assert.Equal("", TranscriptFilter.Normalize(transcript));

    [Fact]
    public void KeepsRealSpeech() =>
        Assert.Equal("Hava nasıl?", TranscriptFilter.Normalize(" Hava nasıl? "));
}

public class HotkeyGestureTests
{
    [Fact]
    public void ParsesModifiersAndKey()
    {
        var gesture = HotkeyGesture.Parse("ctrl + alt + Space");
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, gesture.Modifiers);
        Assert.Equal("Space", gesture.Key);
        Assert.Equal("Ctrl+Alt+Space", gesture.ToString());
    }

    [Theory]
    [InlineData("Space")]
    [InlineData("Ctrl+Hyper+F")]
    [InlineData("")]
    public void RejectsInvalidGestures(string text) =>
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse(text));
}

public class AppNameTests
{
    [Fact]
    public void SimplifiesTurkishSpelling() =>
        Assert.Equal(AssistantTools.Simplify("Görev Yöneticisi"), AssistantTools.Simplify("gorev yoneticisi"));
}
