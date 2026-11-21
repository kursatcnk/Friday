using Friday.Core;

namespace Friday.UnitTests;

public class DictationTextTests
{
    [Theory]
    [InlineData("merhaba virgül nasılsın soru işareti", "Merhaba, nasılsın?")]
    [InlineData("ııı toplantıyı eee yarına alalım nokta", "Toplantıyı yarına alalım.")]
    [InlineData("Bu nokta çok önemli.", "Bu nokta çok önemli.")]
    [InlineData("harika ünlem bunu bekliyordum", "Harika! Bunu bekliyordum")]
    [InlineData("liste iki nokta üst üste süt, ekmek", "Liste: süt, ekmek")]
    [InlineData("Hmm, sanırım haklısın.", "Sanırım haklısın.")]
    [InlineData("Teşekkürler virgül.", "Teşekkürler,")]
    [InlineData("selam", "Selam")]
    [InlineData("dünya ekonomisi", "Dünya ekonomisi")]
    public void CleansDictatedText(string heard, string expected) => Assert.Equal(expected, DictationText.Clean(heard));
}
