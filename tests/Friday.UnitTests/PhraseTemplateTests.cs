using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Dil modelinin çözdüğü isteklerden kalıp öğrenilmesi ve yeni değerlere genellenmesi.</summary>
public class PhraseTemplateTests
{
    private static Dictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static PhraseTemplates Fresh(out string folder)
    {
        folder = Path.Combine(Path.GetTempPath(), "friday-templates-" + Guid.NewGuid().ToString("N"));
        return new PhraseTemplates(Path.Combine(folder, "templates.json"));
    }

    [Fact]
    public void TheValuesBecomeSlots()
    {
        var skeleton = PhraseTemplates.Skeleton("Ilgaz Bey'i aramayı yarın 10'da bana bildir", "hatirlatici",
            Args(("islem", "ekle"), ("zaman", "yarın 10'da"), ("metin", "Ilgaz Bey'i aramayı")), out _, out var fixedValues);
        Assert.Equal("{metin} {zaman} bildir", skeleton);
        Assert.Equal("ekle", fixedValues["islem"]);
    }

    [Theory]
    // Değer cümlede hiç karşılığı olmayan bir şeyse kalıp çıkmaz.
    [InlineData("annemi aramayı yarın 10'da bana bildir", "babanı ara", "yarın 10'da")]
    // Sabit sözcük kalmıyorsa çok geneldir.
    [InlineData("yarın 10'da annemi ara", "annemi ara", "yarın 10'da")]
    public void NoSkeletonWithoutClearAnchors(string sentence, string text, string when) =>
        Assert.Null(PhraseTemplates.Skeleton(sentence, "hatirlatici", Args(("islem", "ekle"), ("zaman", when), ("metin", text)), out _, out _));

    [Fact]
    public void AToolOutsideTheListIsNeverLearned() =>
        Assert.Null(PhraseTemplates.Skeleton("bilgisayarı kapat lütfen hemen", "bilgisayar", Args(("islem", "kapat")), out _, out _));

    [Fact]
    public void ATemplateGeneralisesAfterTwoDifferentValues()
    {
        var templates = Fresh(out var folder);
        try
        {
            Assert.False(templates.Observe("Ilgaz Bey'i aramayı yarın 10'da bana bildir", "hatirlatici",
                Args(("islem", "ekle"), ("zaman", "yarın 10'da"), ("metin", "Ilgaz Bey'i aramayı"))));
            // Tek bir değerle görülen kalıp henüz etkin değildir.
            Assert.Null(templates.Find("faturayı ödemeyi cuma 9'da bana bildir"));
            Assert.True(templates.Observe("çiçekleri sulamayı akşam 7'de bana bildir", "hatirlatici",
                Args(("islem", "ekle"), ("zaman", "akşam 7'de"), ("metin", "çiçekleri sulamayı"))));

            Assert.Equal("Selin'i aramayı", templates.Find("Selin'i aramayı cuma 9'da bana bildir")?.Arguments["metin"]);
            var command = templates.Find("faturayı ödemeyi cuma 9'da bana bildir");
            Assert.NotNull(command);
            Assert.Equal("hatirlatici", command.Tool);
            Assert.Equal("ekle", command.Arguments["islem"]);
            Assert.Equal("faturayı ödemeyi", command.Arguments["metin"]);
            Assert.Equal("cuma 9'da", command.Arguments["zaman"]);

            // Zaman yuvası çözülemeyen bir değer kalıba uymaz.
            Assert.Null(templates.Find("faturayı ödemeyi bir ara bana bildir"));

            // "Yanlış anladın": o söze uyan kalıp unutulur.
            Assert.Equal(1, templates.Forget("faturayı ödemeyi cuma 9'da bana bildir"));
            Assert.Null(templates.Find("faturayı ödemeyi cuma 9'da bana bildir"));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void CategoricalSlotsOnlyTakeKnownValues()
    {
        var templates = Fresh(out var folder);
        try
        {
            templates.Observe("dolar şu sıralar kaçtan işlem görüyor", "doviz", Args(("birim", "dolar")));
            templates.Observe("euro şu sıralar kaçtan işlem görüyor", "doviz", Args(("birim", "euro")));
            Assert.Equal("sterlin", templates.Find("sterlin şu sıralar kaçtan işlem görüyor")?.Arguments["birim"]);
            Assert.Null(templates.Find("maaşım şu sıralar kaçtan işlem görüyor"));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void ASkeletonThatLeadsToAnotherJobIsDropped()
    {
        var templates = Fresh(out var folder);
        try
        {
            templates.Observe("süt kısmına bunu yaz", "not", Args(("islem", "ekle"), ("metin", "süt")));
            templates.Observe("ekmek kısmına bunu yaz", "liste", Args(("islem", "ekle"), ("oge", "ekmek")));
            templates.Observe("peynir kısmına bunu yaz", "not", Args(("islem", "ekle"), ("metin", "peynir")));
            Assert.Null(templates.Find("zeytin kısmına bunu yaz"));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
