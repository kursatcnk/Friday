using Friday.Core;

namespace Friday.UnitTests;

public sealed class CorrectionTests : IDisposable
{
    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "friday-corrections-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("yanlış anladın", "")]
    [InlineData("Hayır, yanlış anladın.", "")]
    [InlineData("beni yanlış anladın", "")]
    [InlineData("öyle demedim", "")]
    [InlineData("hayır onu demedim", "")]
    [InlineData("Yanlış anladın, yarın sabah dokuzda annemi aramayı hatırlat.", "yarın sabah dokuzda annemi aramayı hatırlat")]
    [InlineData("yanlış anladın hatırlatıcı kur demek istedim", "hatırlatıcı kur")]
    [InlineData("Hayır, market listesine ekle demek istedim.", "market listesine ekle")]
    [InlineData("hayır, yağmur yağacak mı dedim", "yağmur yağacak mı")]
    public void RecognizesCorrections(string text, string meant) =>
        Assert.Equal(meant, CorrectionBook.Meant(text));

    [Theory]
    [InlineData("hayır")]
    [InlineData("yanlış bir şey yaptım galiba")]
    [InlineData("bu cevap yanlış mı")]
    [InlineData("ne demek istedin")]
    public void IgnoresOrdinarySentences(string text) =>
        Assert.Null(CorrectionBook.Meant(text));

    [Fact]
    public void RemembersMisheardPhrasesInTheirQuickForm()
    {
        var file = Path.Combine(_folder, "corrections.json");
        var book = new CorrectionBook(file);
        Assert.False(book.Contains("annemi ara"));
        Assert.True(book.Add("Friday, annemi ara."));
        Assert.False(book.Add("annemi ara"));
        Assert.True(book.Contains("annemi ara lütfen"));
        Assert.False(book.Contains("babamı ara"));

        var reloaded = new CorrectionBook(file);
        Assert.True(reloaded.Contains("Annemi ara!"));
        Assert.Equal(1, reloaded.Clear());
        Assert.False(reloaded.Contains("annemi ara"));
        Assert.Empty(new CorrectionBook(file).Phrases);
    }

    [Fact]
    public void NeverLearnsMetaCommands()
    {
        Assert.Contains("geri_al", CorrectionBook.Unlearnable);
        Assert.Contains("tekrar", CorrectionBook.Unlearnable);
        Assert.DoesNotContain("yapilacak", CorrectionBook.Unlearnable);
    }

    [Theory]
    [InlineData("öğrendiğin düzeltmeleri unut", "sifirla")]
    [InlineData("düzeltmeleri sıfırla", "sifirla")]
    [InlineData("öğrendiğin düzeltmeler neler", "listele")]
    public void UnderstandsCorrectionCommands(string text, string action)
    {
        var command = QuickIntents.Match(text);
        Assert.NotNull(command);
        Assert.Equal("duzeltme", command.Tool);
        Assert.Equal(action, command.Arguments["islem"]);
    }

    [Fact]
    public void CorrectionPhrasesAreNotQuickCommands()
    {
        Assert.Null(QuickIntents.Match("yanlış anladın"));
        Assert.Null(QuickIntents.Match("öyle demedim"));
    }
}
