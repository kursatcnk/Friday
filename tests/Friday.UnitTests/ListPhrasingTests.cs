using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Listeye eklemenin söyleyişleri. Ölçüm: 15 söyleyişten 8'i hızlı yoldaydı; öğe önce söylenince, "koy/at" ile ya da liste adı olmadan dil modeline gidiyordu.</summary>
public class ListPhrasingTests
{
    [Theory]
    [InlineData("market listeme süt ekle", "market", "süt")]
    [InlineData("süt ekle market listesine", "market", "süt")]
    [InlineData("alışveriş listeme ekmek koy", "alışveriş", "ekmek")]
    [InlineData("listeye yumurta ekle", "market", "yumurta")]
    [InlineData("sütü market listesine ekle", "market", "sütü")]
    [InlineData("markete süt ekle", "market", "süt")]
    [InlineData("alışveriş listesine deterjan at", "alışveriş", "deterjan")]
    [InlineData("market listesine bir de peynir ekle", "market", "peynir")]
    [InlineData("listeme domates ekle", "market", "domates")]
    public void AddsInEveryOrder(string said, string list, string item)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("liste", command?.Tool);
        Assert.Equal("ekle", command!.Arguments["islem"]);
        Assert.Equal(list, command.Arguments["ad"]);
        Assert.Equal(item, command.Arguments["oge"]);
    }
}
