using Friday.Core;

namespace Friday.UnitTests;

public class QuickIntentTests
{
    [Theory]
    [InlineData("YouTube'da lofi hip hop aç.", "lofi hip hop")]
    [InlineData("Friday, YouTube'dan Tarkan Kuzu Kuzu oynat", "tarkan kuzu kuzu")]
    [InlineData("Tarkan'ın Kuzu Kuzu klibini aç.", "tarkan'ın kuzu kuzu")]
    [InlineData("Barış Manço Gülpembe şarkısının klibini açar mısın?", "barış manço gülpembe")]
    [InlineData("komik kedi videosu aç", "komik kedi")]
    [InlineData("YouTube'da lütfen sakin piyano müziği açsana", "sakin piyano müziği")]
    // Kullanıcının gerçekten söylediği cümleler:
    [InlineData("İyiyim teşekkürler. Youtube'dan Power to Resist şarkısını dinlemek istiyorum.", "power to resist")]
    [InlineData("Bana YouTube'dan Naruto Kokuten şarkısını aç.", "naruto kokuten")]
    [InlineData("Naruto Kokuten şarkısını çal", "naruto kokuten")]
    [InlineData("Power to Resist'i YouTube'dan aç", "power to resist'i")]
    [InlineData("YouTube'da Dragon Ball müziği izlemek istiyorum", "dragon ball müziği")]
    public void RecognizesClearVideoCommands(string sentence, string query)
    {
        var command = QuickIntents.Match(sentence);
        Assert.NotNull(command);
        Assert.Equal("video_ac", command.Tool);
        Assert.Equal(query, command.Arguments["arama"]);
    }

    [Theory]
    [InlineData("YouTube'u aç")]
    [InlineData("Not defterini aç")]
    [InlineData("Bu videoda ne anlatılıyor?")]
    [InlineData("Video izlemek istiyorum ama hangisini bilmiyorum")]
    [InlineData("Saat kaç?")]
    [InlineData("Bana YouTube'dan")]
    [InlineData("Hesap makinesini aç")]
    public void LeavesEverythingElseToTheModel(string sentence) =>
        Assert.Null(QuickIntents.Match(sentence));
}
