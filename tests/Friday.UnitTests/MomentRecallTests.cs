using Friday.Core;

namespace Friday.UnitTests;

public class MomentRecallTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 10, 0, 0);

    private static readonly PersonalRecord[] Records =
    [
        new("Hafıza", "Ahmet'e bütçe tablosunu göndereceğimi söyledim.", Now.AddDays(-3)),
        new("Hafıza", "Annem tansiyon ilacı kullanıyor.", Now.AddDays(-40)),
        new("Hafıza", "Kızım Ekinsu'nun piyano hocası Selin Hanım.", Now.AddDays(-10)),
        new("Not", "Müşteri logo için mavi tonları istedi, kırmızı olmasın.", Now.AddDays(-1)),
        new("İş", "Ahmet'i ara", Now.AddDays(-1)),
        new("İş", "Kira sözleşmesini yenile", Now.AddDays(-2)),
        new("Günlük", "Bugün spor salonuna gittim, çok yorucuydu.", Now.AddDays(-2))
    ];

    [Theory]
    [InlineData("ahmet'i ara", "Ahmet'e bütçe tablosunu göndereceğimi söyledim.")]
    [InlineData("Ekinsu'yu piyanodan al", "Kızım Ekinsu'nun piyano hocası Selin Hanım.")]
    [InlineData("selin hanımı ara", "Kızım Ekinsu'nun piyano hocası Selin Hanım.")]
    [InlineData("müşteri logo taslağını hazırla", "Müşteri logo için mavi tonları istedi, kırmızı olmasın.")]
    public void BringsForwardWhatMatters(string subject, string expected) =>
        Assert.Equal(expected, MomentRecall.Related(subject, Records)?.Text);

    [Theory]
    // Tek ve yaygın bir sözcük (ilaç, spor) ya da yalnızca fiil tutması gürültüdür.
    [InlineData("ilaç iç")]
    [InlineData("spor yap")]
    [InlineData("su iç")]
    [InlineData("annemi ara")]
    [InlineData("yarın sabah ara")]
    [InlineData("")]
    public void StaysQuietWithoutADistinctiveMatch(string subject) =>
        Assert.Null(MomentRecall.Related(subject, Records));

    [Fact]
    public void NeverRepeatsTheTaskItself()
    {
        PersonalRecord[] self = [new("İş", "Kira sözleşmesini yenile", Now)];
        Assert.Null(MomentRecall.Related("kira sözleşmesini yenile", self));
    }
}
