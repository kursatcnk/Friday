using Friday.Core;

namespace Friday.UnitTests;

public sealed class LearnedShortcutsTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "friday-shortcuts-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() => File.Delete(_file);

    private static Dictionary<string, string> Args(params (string Key, string Value)[] pairs) => pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Fact]
    public void LearnsAPhraseAfterTheSameCallTwice()
    {
        var shortcuts = new LearnedShortcuts(_file);
        Assert.False(shortcuts.Observe("sesi bir tık kıs", "medya", Args(("komut", "ses"), ("deger", "-10"))));
        Assert.Null(shortcuts.Find("sesi bir tık kıs"));
        Assert.True(shortcuts.Observe("Sesi bir tık kıs.", "medya", Args(("komut", "ses"), ("deger", "-10"))));

        var command = new LearnedShortcuts(_file).Find("Friday, sesi bir tık kıs");
        Assert.NotNull(command);
        Assert.Equal("medya", command.Tool);
        Assert.Equal("-10", command.Arguments["deger"]);
        Assert.Single(shortcuts.Learned);
    }

    [Fact]
    public void StartsOverWhenTheModelDidSomethingElse()
    {
        var shortcuts = new LearnedShortcuts(_file);
        shortcuts.Observe("sesi bir tık kıs", "medya", Args(("komut", "ses"), ("deger", "-10")));
        shortcuts.Observe("sesi bir tık kıs", "medya", Args(("komut", "ses"), ("deger", "-20")));
        Assert.Null(shortcuts.Find("sesi bir tık kıs"));
        Assert.True(shortcuts.Observe("sesi bir tık kıs", "medya", Args(("komut", "ses"), ("deger", "-20"))));
    }

    [Theory]
    // Tek sözcük bağlama bağlıdır ("kapat" neyi?), hafızaya yazmak ya da silmek öğrenilmez, saatin tarih odağı hızlı yolda yoktur.
    [InlineData("kapat", "medya", "komut", "duraklat")]
    [InlineData("bunu unutma lütfen", "hatirla", "bilgi", "x")]
    [InlineData("bugün ayın kaçı", "saat_ve_tarih", "", "")]
    [InlineData("yarın hava nasıl olacak", "hava_durumu", "gun", "2")]
    [InlineData("müziği durdur artık", "medya", "beklenmeyen", "x")]
    public void NeverLearnsWhatCouldGoWrong(string phrase, string tool, string key, string value)
    {
        var shortcuts = new LearnedShortcuts(_file);
        var arguments = key.Length == 0 ? Args() : Args((key, value));
        shortcuts.Observe(phrase, tool, arguments);
        Assert.False(shortcuts.Observe(phrase, tool, arguments));
        Assert.Null(shortcuts.Find(phrase));
    }

    [Fact]
    public void UsesTheQuickPathName()
    {
        var shortcuts = new LearnedShortcuts(_file);
        shortcuts.Observe("spor haberleri neler", "haberler", Args(("kategori", "spor")));
        shortcuts.Observe("spor haberleri neler", "haberler", Args(("kategori", "spor")));
        Assert.Equal("haber", shortcuts.Find("spor haberleri neler")?.Tool);
    }

    [Fact]
    public void ForgetsOnRequest()
    {
        var shortcuts = new LearnedShortcuts(_file);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            shortcuts.Observe("odak modunu başlat", "ritim", Args(("islem", "odak"), ("dakika", "25")));
            shortcuts.Observe("günümü planla bakalım", "gun_plani", Args());
        }

        Assert.Equal(2, shortcuts.Learned.Count);
        Assert.True(shortcuts.Forget("odak modunu başlat"));
        Assert.Null(shortcuts.Find("odak modunu başlat"));
        Assert.Equal(1, shortcuts.Clear());
        Assert.Empty(new LearnedShortcuts(_file).Learned);
    }

    [Theory]
    [InlineData("öğrendiğin kısayollar neler", "listele")]
    [InlineData("kısayolları unut", "sifirla")]
    public void UnderstandsShortcutCommands(string text, string action)
    {
        var command = QuickIntents.Match(text);
        Assert.NotNull(command);
        Assert.Equal("kisayol_ogrenimi", command.Tool);
        Assert.Equal(action, command.Arguments["islem"]);
    }
}
