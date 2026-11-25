using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Hafıza bilgilerinin ekrandaki hali. Model bilgileri "Kullanıcının arabasının muayenesi 28 Kasım'da" diye üçüncü
/// kişiden kaydediyor; arayüzde bu, makinenin kendi notunu okutmak gibi duruyordu. Gösterirken "Arabanın muayenesi 28
/// Kasım'da", "Kızının adı Ela", "Kahveyi şekersiz içer" olur. Kayıt değişmez.
/// </summary>
public static partial class MemoryText
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string ForDisplay(string text)
    {
        var clean = text.Trim();
        if (Owner().Match(clean) is { Success: true } owner)
        {
            return TurkishGrammar.Capitalize(Possessive(owner.Groups["word"].Value) + owner.Groups["rest"].Value);
        }

        return Subject().Match(clean) is { Success: true } subject ? TurkishGrammar.Capitalize(subject.Groups["rest"].Value) : clean;
    }

    /// <summary>Üçüncü kişi iyelik → ikinci kişi: arabası → araban, arabasının → arabanın, kızı → kızın (kızının aynı kalır).</summary>
    private static string Possessive(string word)
    {
        var lower = word.ToLower(Turkish);
        foreach (var (pattern, replacement) in Rules)
        {
            if (Regex.IsMatch(lower, pattern))
            {
                return Regex.Replace(lower, pattern, replacement);
            }
        }

        return lower;
    }

    private static readonly (string Pattern, string Replacement)[] Rules =
    [
        (@"^(.+?)s([ıiuü])n([ıiuü])n$", "$1n$3n"),
        (@"^(.+?)s([ıiuü])n([ae])$", "$1n$3"),
        (@"^(.+?)s([ıiuü])n([ıiuü])$", "$1n$3"),
        (@"^(.+?)s([ıiuü])$", "$1n"),
        (@"^(.+[^aeıioöuü])([ıiuü])$", "$1$2n"),
    ];

    [GeneratedRegex(@"^Kullanıcının (?<word>\p{L}+)(?<rest>.*)$")]
    private static partial Regex Owner();

    [GeneratedRegex(@"^Kullanıcı(?:ya|yı)? (?<rest>.+)$")]
    private static partial Regex Subject();
}
