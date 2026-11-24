using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Önündeki belge: başlıktan dosya adı, istek kalıpları, uzun metnin parçalanması.</summary>
public class FrontDocumentTests
{
    [Theory]
    [InlineData("rental-agreement.pdf - Adobe Acrobat Reader (64-bit)", "rental-agreement.pdf")]
    [InlineData("Annual Report 2025.pdf - Profil 1 - Microsoft Edge", "Annual Report 2025.pdf")]
    [InlineData("teklif.docx - Word", "teklif.docx")]
    [InlineData("notes.txt - Not Defteri", "notes.txt")]
    [InlineData("C:\\Users\\x\\Downloads\\paper.pdf - SumatraPDF", "paper.pdf")]
    public void FindsTheDocumentInTheTitle(string title, string file) => Assert.Equal(file, FrontDocument.FileName(title));

    [Theory]
    [InlineData("YouTube - Google Chrome")]
    [InlineData("Gelen Kutusu - Outlook")]
    public void NoDocumentInOtherTitles(string title) => Assert.Null(FrontDocument.FileName(title));

    [Theory]
    [InlineData("bu ne", "acikla")]
    [InlineData("bunu açıkla", "acikla")]
    [InlineData("burada ne yazıyor", "acikla")]
    [InlineData("bu pdf ne anlatıyor", "acikla")]
    [InlineData("bunu türkçeye çevir", "cevir")]
    [InlineData("bu sayfayı çevir", "cevir")]
    [InlineData("türkçeye çevir", "cevir")]
    [InlineData("bu belgeyi özetle", "ozetle")]
    [InlineData("devamını çevir", "devam")]
    public void UnderstandsRequestsAboutTheFrontDocument(string said, string action)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("onumdeki", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
    }

    [Theory]
    [InlineData("The security deposit is 2,500 USD and will be returned within 30 days after the tenant moves out.", false)]
    [InlineData("Depozito 2.500 dolar ve kiracı taşındıktan sonra 30 gün içinde geri ödenir.", true)]
    [InlineData("Bu sözleşme ile kira bedeli belirlenmiştir ve taraflar için bağlayıcıdır.", true)]
    public void TellsTurkishFromForeignText(string text, bool turkish) => Assert.Equal(turkish, FrontDocument.LooksTurkish(text));

    [Fact]
    public void ALongTextIsTranslatedInSentenceSizedChunks()
    {
        var text = string.Concat(Enumerable.Repeat("This is a sentence about the contract terms. ", 120));
        var (first, next) = FrontDocument.Chunk(text, 0);
        Assert.True(first.Length <= FrontDocument.ChunkLength);
        Assert.EndsWith(".", first, StringComparison.Ordinal);
        var (second, _) = FrontDocument.Chunk(text, next);
        Assert.StartsWith("This", second, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuestionGetsTheRelevantPartOfALongDocument()
    {
        var filler = string.Concat(Enumerable.Repeat("General terms and conditions apply to all parties involved. ", 200));
        var text = filler + "The security deposit is 2,500 USD and is refundable within 30 days. " + filler;
        var part = FrontDocument.Relevant(text, "security deposit refundable", 3000);
        Assert.Contains("2,500 USD", part, StringComparison.Ordinal);
        Assert.True(part.Length < text.Length);
    }
}
