using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Türkçe okunuşla söylenen uygulama adlarının Başlat menüsündeki adlarla sesçe eşleşmesi.</summary>
public class PhoneticNameTests
{
    private static readonly string[] Installed =
    [
        "WhatsApp", "Google Chrome", "Spotify", "Excel", "Word", "Discord", "Telegram", "Notepad", "Microsoft Teams", "Outlook",
        "Steam", "Visual Studio Code", "Adobe Photoshop 2024", "Hesap Makinesi", "Paint", "Zoom", "Slack", "Notion", "Obsidian",
        "Firefox", "Microsoft Edge", "PowerPoint", "Takvim", "Ayarlar", "Dosya Gezgini", "Kayıt Defteri Düzenleyicisi"
    ];

    [Theory]
    // QuickIntents eki atar ("vatsapı" → "vatsap"); Whisper'ın yazışları da gelir ("zipotifiyo").
    [InlineData("vatsap", "WhatsApp")]
    [InlineData("krom", "Google Chrome")]
    [InlineData("spotifa", "Spotify")]
    [InlineData("spotifay", "Spotify")]
    [InlineData("zipotifiyo", "Spotify")]
    [InlineData("eksel", "Excel")]
    [InlineData("vord", "Word")]
    [InlineData("diskord", "Discord")]
    [InlineData("tims", "Microsoft Teams")]
    [InlineData("autluk", "Outlook")]
    [InlineData("stim", "Steam")]
    [InlineData("fotoşop", "Adobe Photoshop 2024")]
    [InlineData("noşın", "Notion")]
    [InlineData("firefoks", "Firefox")]
    [InlineData("poverpoynt", "PowerPoint")]
    [InlineData("telegram", "Telegram")]
    [InlineData("obsidyın", "Obsidian")]
    public void FindsTheAppByHowItSounds(string spoken, string app) => Assert.Equal(app, PhoneticName.Best(spoken, Installed));

    [Theory]
    // Uygulama olmayan sözler, çok kısa iskeletler ve belirsiz adlar eşleşmez.
    [InlineData("pencere")]
    [InlineData("dosya")]
    [InlineData("kapı")]
    [InlineData("ışıklar")]
    [InlineData("müzik")]
    [InlineData("kahve")]
    [InlineData("ekran")]
    public void LeavesOtherWordsAlone(string spoken) => Assert.Null(PhoneticName.Best(spoken, Installed));

    [Fact]
    public void ASpokenConsonantEndingDoesNotMatchANameEndingInAVowel()
    {
        // Gerçek bir Başlat menüsünde "krom" önce "Razer Chroma" ile eşleşiyordu.
        Assert.Null(PhoneticName.Best("krom", ["Razer Chroma", "Steam"]));
        Assert.Equal("Google Chrome", PhoneticName.Best("krom", ["Razer Chroma", "Google Chrome"]));
    }

    [Theory]
    [InlineData("vatsapı aç", "vatsap")]
    [InlineData("Zipotifiyo'a aç", "zipotifiyo")]
    [InlineData("kromu aç", "krom")]
    public void TheQuickPathPassesTheSpokenName(string text, string name)
    {
        var command = QuickIntents.Match(text);
        Assert.Equal("uygulama_ac", command?.Tool);
        Assert.Equal(name, command!.Arguments["ad"]);
    }

    [Theory]
    [InlineData("WhatsApp", "vtsp")]
    [InlineData("vatsapı", "vtsp")]
    [InlineData("Google Chrome", "glkrm")]
    [InlineData("kromu", "krm")]
    [InlineData("Spotify", "sptf")]
    [InlineData("Excel", "ksl")]
    public void ReducesANameToItsConsonants(string name, string key) => Assert.Equal(key, PhoneticName.Key(name));
}
