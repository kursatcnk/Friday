using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Birleşik komutlar: her parçası bir hızlı komutsa parçalanır; serbest metin ve genel sorular bölünmez.</summary>
public class CompoundCommandTests
{
    private static readonly string[] Routines = ["derin çalışma"];

    private static IReadOnlyList<CompoundPart>? Split(string text)
    {
        QuickCommand? Match(string value) => QuickIntents.Match(value, Routines);
        return CompoundCommand.Split(text, Match(text), Match);
    }

    [Theory]
    [InlineData("market listeme süt ekle, yarın sabah 9'da dişçiyi aramamı hatırlat ve dolar kaç", "liste+hatirlatici+doviz")]
    [InlineData("hava nasıl ve dolar kaç", "hava_durumu+doviz")]
    [InlineData("saat kaç, hava nasıl ve dolar kaç", "saat_ve_tarih+hava_durumu+doviz")]
    [InlineData("10 dakika sonra çayı hatırlat ve dolar kaç", "zamanlayici+doviz")]
    [InlineData("sesi kıs sonra müziği durdur", "medya+medya")]
    [InlineData("müziği durdur ve bilgisayarı kilitle", "medya+sistem")]
    [InlineData("yapılacaklarımda ne var ve market listemde ne var", "yapilacak+liste")]
    [InlineData("yarın hava nasıl olacak, bir de euro kaç", "hava_durumu+doviz")]
    [InlineData("sesi yüzde 30 yap ve sıradaki şarkıya geç", "medya+medya")]
    [InlineData("derin çalışma modunu başlat sonra rahatsız etme modunu aç", "rutin+rahatsiz_etme")]
    [InlineData("pomodoro başlat ardından bir saat rahatsız etme", "ritim+rahatsiz_etme")]
    public void SplitsIntoQuickCommands(string text, string tools)
    {
        var parts = Split(text);
        Assert.NotNull(parts);
        Assert.Equal(tools, string.Join('+', parts.Select(part => part.Command.Tool)));
    }

    [Theory]
    // Ulaç ("açıp") ve paylaşılan fiil ("Chrome'u ve Spotify'ı aç") parçayı tek başına söylenir hale getirir.
    [InlineData("Spotify'ı açıp sesi kıs", "Spotify'ı aç|sesi kıs")]
    [InlineData("Chrome'u ve Spotify'ı aç", "Chrome'u aç|Spotify'ı aç")]
    [InlineData("müziği durdurup bilgisayarı kilitle", "müziği durdur|bilgisayarı kilitle")]
    public void SaysEachPartOnItsOwn(string text, string spoken)
    {
        var parts = Split(text);
        Assert.NotNull(parts);
        Assert.Equal(spoken.Split('|'), parts.Select(part => part.Text));
    }

    [Theory]
    // Kısa bir duraklamayla ayrı söz olarak gelen ikinci parça bağlaçla başlar.
    [InlineData("ve sesi kıs", "medya")]
    [InlineData("bir de dolar kaç", "doviz")]
    [InlineData("ayrıca hava nasıl", "hava_durumu")]
    [InlineData("ve sonra müziği durdur", "medya")]
    [InlineData("ardından bilgisayarı kilitle", "sistem")]
    [InlineData("ve yarın 9'da dişçiyi hatırlat", "hatirlatici")]
    // Whisper'ın sık yanlış yazışları.
    [InlineData("Sesi kız.", "medya")]
    [InlineData("Bir de eurokaç?", "doviz")]
    public void ASecondPartSpokenAfterAPauseIsStillACommand(string text, string tool) =>
        Assert.Equal(tool, QuickIntents.Match(text)?.Tool);

    [Theory]
    [InlineData("hatırlat bana yarın süt ve ekmek almam lazım")]
    [InlineData("not al: toplantı perşembe ve cuma")]
    [InlineData("market listeme süt ve yumurta ekle")]
    [InlineData("1,5 saat sonra hatırlat")]
    [InlineData("dolar ve euro kaç")]
    [InlineData("Ankara ve İstanbul arası kaç kilometre")]
    [InlineData("kedi ve köpek arasındaki fark ne")]
    [InlineData("Ayşe ve Mehmet'e mail yaz")]
    [InlineData("dolar kaç ve neden yükseliyor")]
    [InlineData("bugün ne yapsam ve neden")]
    [InlineData("dolar kaç")]
    public void LeavesSingleRequestsWhole(string text) => Assert.Null(Split(text));
}
