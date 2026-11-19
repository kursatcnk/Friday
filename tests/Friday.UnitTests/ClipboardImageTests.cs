using Friday.Core;

namespace Friday.UnitTests;

public class ClipboardImageIntentTests
{
    [Theory]
    [InlineData("panodaki resmi oku")]
    [InlineData("kopyaladığım görseldeki yazıyı çıkar")]
    [InlineData("bu ekran görüntüsündeki metni kopyala")]
    [InlineData("panodaki ekran alıntısını yazıya çevir")]
    public void ReadsTextFromTheClipboardImage(string sentence)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("pano_gorseli", command?.Tool);
        Assert.Equal("oku", command!.Arguments["islem"]);
    }

    [Theory]
    [InlineData("panodaki resimde ne var")]
    [InlineData("kopyaladığım görseli açıkla")]
    public void AsksAboutTheClipboardImage(string sentence)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("pano_gorseli", command?.Tool);
        Assert.Equal("acikla", command!.Arguments["islem"]);
    }

    [Theory]
    [InlineData("kopyaladıklarım", "pano_gecmisi")]
    [InlineData("2. kopyaladığımı yapıştır", "pano_gecmisi")]
    public void KeepsClipboardHistory(string sentence, string tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool);
}
