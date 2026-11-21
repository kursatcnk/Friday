using Friday.Core;

namespace Friday.UnitTests;

public class LastTimeTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0);

    private static readonly LastTime.Trace[] Traces =
    [
        new("Alışkanlık", "Koşu", new DateTime(2026, 11, 14, 12, 0, 0)),
        new("Alışkanlık", "Koşu", new DateTime(2026, 11, 18, 12, 0, 0)),
        new("Harcama", "Migros (Market) · 250 TL", new DateTime(2026, 11, 20, 19, 0, 0)),
        new("Harcama", "Kahve (Yeme-içme) · 90 TL", new DateTime(2026, 11, 21, 9, 0, 0)),
        new("İş", "Ahmet'i ara", new DateTime(2026, 11, 10, 11, 0, 0)),
        new("Günlük", "Annemle uzun uzun konuştuk.", new DateTime(2026, 10, 2, 22, 0, 0))
    ];

    [Theory]
    [InlineData("koşuya gittim", "Koşu", 18)]
    [InlineData("market alışverişi yaptım", "Migros (Market) · 250 TL", 20)]
    [InlineData("Ahmet'i aradım", "Ahmet'i ara", 10)]
    [InlineData("annemle konuştum", "Annemle uzun uzun konuştuk.", 2)]
    public void FindsTheNewestTrace(string subject, string text, int day)
    {
        var trace = LastTime.Find(subject, Traces);
        Assert.NotNull(trace);
        Assert.Equal(text, trace.Text);
        Assert.Equal(day, trace.When.Day);
    }

    [Fact]
    public void TellsWhenInPlainWords()
    {
        Assert.Equal("En son 3 gün önce, Çarşamba günü (18 Kasım): \"koşu\" alışkanlığını işaretlemiştin.",
            LastTime.Tell("koşuya gittim", LastTime.Find("koşuya gittim", Traces), Now));
        Assert.Equal("En son dün: harcama kaydın var: Migros (Market) · 250 TL.",
            LastTime.Tell("market", LastTime.Find("market", Traces), Now));
        Assert.StartsWith("\"yüzme\" ile ilgili hiçbir kaydımda bir iz yok", LastTime.Tell("yüzme", LastTime.Find("yüzme", Traces), Now), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en son ne zaman koşuya gittim", "koşuya gittim")]
    [InlineData("Son olarak ne zaman market alışverişi yaptım?", "market alışverişi yaptım")]
    [InlineData("Ahmet'i en son ne zaman aradım", "ahmet'i")]
    public void UnderstandsLastTimeQuestions(string text, string subject)
    {
        var command = QuickIntents.Match(text);
        Assert.NotNull(command);
        Assert.Equal("en_son", command.Tool);
        Assert.Equal(subject, command.Arguments["konu"]);
    }
}
