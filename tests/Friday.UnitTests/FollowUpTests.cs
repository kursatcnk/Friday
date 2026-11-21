using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Eksik söylenmiş devam komutlarının son hızlı komutun üzerine kurulması.</summary>
public class FollowUpTests
{
    private static string? Resolve(string last, string text) =>
        FollowUp.Resolve(text, last, QuickIntents.Match(last)!, value => QuickIntents.Match(value)) is { } follow ? follow.Text : null;

    [Theory]
    [InlineData("dolar kaç", "peki euro", "euro kaç")]
    [InlineData("dolar kaç", "ya sterlin?", "sterlin kaç")]
    [InlineData("dolar kaç", "peki euro ne kadar", "euro kaç")]
    [InlineData("market listeme süt ekle", "yumurta da", "market listeme yumurta ekle")]
    [InlineData("market listeme süt ekle", "bir de ekmek", "market listeme ekmek ekle")]
    [InlineData("market listeme süt ekle", "peynir de ekle", "market listeme peynir ekle")]
    [InlineData("İstanbul'da hava nasıl", "ya Ankara'da", "Ankara'da hava nasıl")]
    [InlineData("İstanbul'da hava nasıl", "peki İzmir", "İzmir'de hava nasıl")]
    [InlineData("İstanbul'da hava nasıl", "peki yarın", "yarın İstanbul'da hava nasıl")]
    [InlineData("İstanbul'da hava nasıl", "ya hafta sonu", "hafta sonu İstanbul'da hava nasıl")]
    [InlineData("hava nasıl", "peki yarın", "yarın hava nasıl")]
    [InlineData("yapılacaklarıma fatura öde ekle", "kira da", "yapılacaklarıma kira ekle")]
    [InlineData("yapılacaklarıma fatura öde ekle", "bir de market alışverişi", "yapılacaklarıma market alışverişi ekle")]
    [InlineData("yılbaşına kaç gün kaldı", "peki bayrama", "bayrama kaç gün kaldı")]
    // Whisper'ın yazışları: virgül, düşen "ya", bitişik "da", cümle başı büyük harf.
    [InlineData("dolar kaç", "Peki, euro.", "euro kaç")]
    [InlineData("İstanbul'da hava nasıl", "Ankara'da", "Ankara'da hava nasıl")]
    [InlineData("yapılacaklarıma fatura öde ekle", "Kirada", "yapılacaklarıma kira ekle")]
    [InlineData("market listeme süt ekle", "Yumurta da.", "market listeme yumurta ekle")]
    public void BuildsOnTheLastCommand(string last, string text, string expected) => Assert.Equal(expected, Resolve(last, text));

    [Theory]
    // Devam olduğu söylenmeyen sözler listeye eklenmez, şehir sanılmaz; anlamsız değerler kalıba uymaz.
    [InlineData("market listeme süt ekle", "tamam")]
    [InlineData("market listeme süt ekle", "listede ne var")]
    [InlineData("İstanbul'da hava nasıl", "güzel")]
    [InlineData("dolar kaç", "teşekkürler")]
    [InlineData("dolar kaç", "neden")]
    [InlineData("dolar kaç", "peki neden yükseliyor")]
    [InlineData("dolar kaç", "Ankara'da")]
    public void IgnoresWhatIsNotAFollowUp(string last, string text) => Assert.Null(Resolve(last, text));

    [Fact]
    public void OnlyFollowsToolsWhereItMakesSense() =>
        Assert.Null(FollowUp.Resolve("peki ikincisi", "sesi kıs", QuickIntents.Match("sesi kıs")!, value => QuickIntents.Match(value)));
}
