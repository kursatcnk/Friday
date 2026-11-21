using Friday.Core;

namespace Friday.UnitTests;

public sealed class ReplyStyleTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "friday-style-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void LearnsToBeBriefWhenLongAnswersAreOftenCut()
    {
        var style = new ReplyStyle(_file);
        Assert.Equal(Verbosity.Normal, style.Current);
        Assert.Null(style.Hint);

        // Az örnekle karar verilmez.
        style.RecordLongAnswer(interrupted: true);
        style.RecordLongAnswer(interrupted: true);
        Assert.Equal(Verbosity.Normal, style.Current);

        style.RecordLongAnswer(interrupted: false);
        style.RecordLongAnswer(interrupted: false);
        Assert.False(style.TakeNotice());
        Assert.True(style.RecordLongAnswer(interrupted: false));
        Assert.Equal(Verbosity.Brief, style.Current);
        Assert.Contains("kısa", style.Hint, StringComparison.Ordinal);

        // Friday kısaldığını bir kez söyler.
        Assert.True(style.TakeNotice());
        Assert.False(style.TakeNotice());

        // Diskten yeniden okununca da aynı.
        Assert.Equal(Verbosity.Brief, new ReplyStyle(_file).Current);
    }

    [Fact]
    public void GoesBackWhenTheUserStopsCutting()
    {
        var style = new ReplyStyle(_file);
        foreach (var cut in new[] { true, true, true, false, false })
        {
            style.RecordLongAnswer(cut);
        }

        Assert.Equal(Verbosity.Brief, style.Current);
        for (var index = 0; index < ReplyStyle.Window; index++)
        {
            style.RecordLongAnswer(interrupted: false);
        }

        Assert.Equal(Verbosity.Normal, style.Current);
    }

    [Fact]
    public void ExplicitPreferenceWinsAndNormalResets()
    {
        var style = new ReplyStyle(_file);
        style.Prefer(Verbosity.Detailed);
        foreach (var _ in Enumerable.Range(0, 8))
        {
            style.RecordLongAnswer(interrupted: true);
        }

        Assert.Equal(Verbosity.Detailed, style.Current);
        Assert.True(style.IsExplicit);
        Assert.False(style.TakeNotice());

        style.Prefer(Verbosity.Normal);
        Assert.Equal(Verbosity.Normal, style.Current);
        Assert.Equal((0, 0), style.Recent);
    }

    [Theory]
    [InlineData("daha kısa konuş", "kisa")]
    [InlineData("Friday, bundan sonra kısa cevap ver.", "kisa")]
    [InlineData("çok uzun konuşuyorsun", "kisa")]
    [InlineData("cevaplarını daha ayrıntılı ver", "ayrintili")]
    [InlineData("normal konuş", "normal")]
    [InlineData("neden bu kadar kısa konuşuyorsun", "durum")]
    public void UnderstandsStyleRequests(string text, string action)
    {
        var command = QuickIntents.Match(text);
        Assert.NotNull(command);
        Assert.Equal("uslup", command.Tool);
        Assert.Equal(action, command.Arguments["islem"]);
    }

    [Theory]
    [InlineData("daha detaylı anlat")]
    [InlineData("bunu kısaca anlat")]
    public void LeavesOneOffRequestsToTheModel(string text) =>
        Assert.NotEqual("uslup", QuickIntents.Match(text)?.Tool);
}
