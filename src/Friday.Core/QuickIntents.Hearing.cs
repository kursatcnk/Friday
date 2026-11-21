using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>"Ekinsu adını öğren", "hangi adları tanıyorsun" (bkz. <see cref="HearingVocabulary"/>).</summary>
public static partial class QuickIntents
{
    private static QuickCommand? Hearing(string sentence)
    {
        if (LearnName().Match(sentence) is { Success: true } learn && learn.Groups["w"].Value.Trim() is { Length: > 1 } word)
        {
            return Command("kulak", ("islem", "ogren"), ("ad", word));
        }

        if (ForgetName().Match(sentence) is { Success: true } forget && forget.Groups["w"].Value.Trim() is { Length: > 1 } old)
        {
            return Command("kulak", ("islem", "unut"), ("ad", old));
        }

        return KnownNames().IsMatch(sentence) ? Command("kulak", ("islem", "listele")) : null;
    }

    [GeneratedRegex(@"^(?<w>[^\s,]+(?: [^\s,]+){0,2}) (?:adını|ismini) (?:öğren|tanı|ezberle|doğru duy)$|^(?:şu |bu |yeni bir )?(?:adı|ismi) öğren:? (?<w>.+)$")]
    private static partial Regex LearnName();

    [GeneratedRegex(@"^(?<w>[^\s,]+(?: [^\s,]+){0,2}) (?:adını|ismini) (?:unut|öğrendiklerinden çıkar)$")]
    private static partial Regex ForgetName();

    [GeneratedRegex(@"^hangi (?:adları|isimleri) (?:tanıyorsun|biliyorsun|öğrendin|doğru duyuyorsun)$|^(?:kulağın|kulağın neleri biliyor|öğrendiğin adlar neler|öğrendiğin isimler neler)$")]
    private static partial Regex KnownNames();
}
