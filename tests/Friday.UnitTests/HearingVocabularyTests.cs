using Friday.Core;

namespace Friday.UnitTests;

public class HearingVocabularyTests
{
    [Theory]
    [InlineData("Kızım Ekinsu 9 yaşında, piyano dersi alıyor.", "Ekinsu")]
    [InlineData("Ekinsu'nun piyano dersi yarın.", "Ekinsu")]
    [InlineData("Patronum Ilgaz Bey.", "Ilgaz Bey")]
    [InlineData("Ilgaz Bey'e raporu gönder.", "Ilgaz Bey")]
    [InlineData("Tülin Hanım Fikirtepe'deki daireyi kiraladı.", "Tülin Hanım|Fikirtepe")]
    [InlineData("Annem Ayşe Ataşehir'de oturuyor.", "Ayşe|Ataşehir")]
    [InlineData("Yarın sabah Nehir'i Acıbadem'e götür.", "Nehir|Acıbadem")]
    public void FindsProperNames(string text, string expected) =>
        Assert.Equal(expected.Split('|'), HearingVocabulary.ProperNames(text));

    [Theory]
    [InlineData("Yarın market alışverişi yap.")]
    [InlineData("Toplantı Perşembe günü, Mart ayında.")]
    [InlineData("Bey ve Hanım geldi.")]
    [InlineData("süt al")]
    public void SkipsSentenceStartsAndCommonWords(string text) =>
        Assert.Empty(HearingVocabulary.ProperNames(text));

    [Fact]
    public void PutsTaughtWordsFirstThenTheMostMentionedNames()
    {
        var terms = HearingVocabulary.Collect(
            ["Göktuğ", "", "İstanbul"],
            ["Doruk'un servisi geldi.", "Kızım Ekinsu ve oğlum Doruk.", "Doruk'u okuldan al.", "Patronum Ilgaz Bey."]);
        // Eşit sayıda geçenlerde daha yeni anılan önce gelir.
        Assert.Equal(["Göktuğ", "İstanbul", "Doruk", "Ilgaz Bey", "Ekinsu"], terms);
    }

    [Fact]
    public void KeepsThePromptShort()
    {
        var many = Enumerable.Range(0, 100).Select(index => $"Ad{index}x");
        var prompt = HearingVocabulary.Prompt("Friday", many);
        Assert.StartsWith("Merhaba Friday. Ad0x, Ad1x", prompt, StringComparison.Ordinal);
        Assert.Equal(HearingVocabulary.MaxTerms, prompt.Split(", ").Length);
        Assert.Equal("Merhaba Friday.", HearingVocabulary.Prompt("Friday", ["friday", " "]));
    }

    [Theory]
    [InlineData("Ekinsu, Doruk, Ilgaz Bey.", true)]
    [InlineData("Doruk, Ilgaz Bey, Validebağ", true)]
    [InlineData("Doruk'u okuldan al.", false)]
    [InlineData("Ekinsu", false)]
    [InlineData("Ekinsu, Doruk, Ilgaz Bey, Selin.", false)]
    [InlineData("", false)]
    public void TreatsAnEchoOfThePromptAsSilence(string heard, bool echo) =>
        Assert.Equal(echo, HearingVocabulary.IsEcho(heard, "Merhaba Friday. Ekinsu, Doruk, Ilgaz Bey, Validebağ."));

    [Theory]
    [InlineData("Ekinsu adını öğren", "ogren", "ekinsu")]
    [InlineData("Friday, Ilgaz Bey ismini öğren.", "ogren", "ılgaz bey")]
    [InlineData("şu adı öğren: Validebağ", "ogren", "validebağ")]
    [InlineData("Ekinsu adını unut", "unut", "ekinsu")]
    [InlineData("hangi adları tanıyorsun", "listele", null)]
    public void UnderstandsTeachingNames(string text, string action, string? name)
    {
        var command = QuickIntents.Match(text);
        Assert.NotNull(command);
        Assert.Equal("kulak", command.Tool);
        Assert.Equal(action, command.Arguments["islem"]);
        Assert.Equal(name, command.Arguments.GetValueOrDefault("ad"));
    }

    [Theory]
    [InlineData("Kinsu'nun piyano dersi yarın saat beşte.", "Ekinsu'nun piyano dersi yarın saat beşte", "Ekinsu")]
    [InlineData("Göktuğa Valideba'daki dükkanı sor.", "Göktuğ'a Validebağ'daki dükkanı sor", "Göktuğ|Validebağ")]
    [InlineData("Boruk'un okul servisini ara.", "Doruk'un okul servisini ara.", "Doruk")]
    public void LearnsNamesFromATypedCorrection(string heard, string typed, string expected) =>
        Assert.Equal(expected.Split('|'), HearingVocabulary.Corrections(heard, typed));

    [Theory]
    // Aynı cümle, bambaşka bir cümle ya da adı olmayan bir düzeltme öğretmez.
    [InlineData("Doruk'un servisini ara.", "Doruk'un servisini ara.")]
    [InlineData("Hava nasıl?", "Selin'e mesaj yaz ve akşam yemeğini sor")]
    [InlineData("toplantıyı yarına al", "toplantıyı cumaya al")]
    public void LearnsNothingFromUnrelatedText(string heard, string typed) =>
        Assert.Empty(HearingVocabulary.Corrections(heard, typed));
}
