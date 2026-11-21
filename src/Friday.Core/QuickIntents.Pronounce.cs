using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>"Claude'u Klod diye oku", "Netflix kelimesini Netfliks diye söyle", "Claude'un okunuşunu unut".</summary>
public static partial class QuickIntents
{
    private static QuickCommand? Pronounce(string sentence)
    {
        if (TeachSound().Match(sentence) is { Success: true } teach)
        {
            return Command("okunus", ("islem", "ekle"), ("kelime", teach.Groups["x"].Value), ("okunus", teach.Groups["y"].Value));
        }

        return ForgetSound().Match(sentence) is { Success: true } forget
            ? Command("okunus", ("islem", "unut"), ("kelime", forget.Groups["x"].Value))
            : null;
    }

    // Kelime ya ekiyle kesmeli ("claude'u") ya da "kelimesini/adını" ile söylenir; böylece sıradan cümleler karışmaz.
    [GeneratedRegex(@"^(?:(?<x>[^\s'’]+)['’]\S*|(?<x>[^\s'’]+(?: [^\s'’]+)?) (?:kelimesini|adını|ismini|sözcüğünü)) (?<y>.+?) (?:diye|şeklinde) (?:oku|söyle|telaffuz et|seslendir)$")]
    private static partial Regex TeachSound();

    [GeneratedRegex(@"^(?<x>[^\s'’]+)['’]\S* okunuşunu (?:unut|sil|sıfırla)$")]
    private static partial Regex ForgetSound();
}
