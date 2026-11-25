using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Zincirleme hesap: "347 çarpı 28 kaç" → "bunu ikiye böl", "3 ile çarp", "100 ekle", "yarısı", "karesi", "yüzde 18'i".
/// Önceki soru yeniden hesaplanır, sonucuna yeni işlem uygulanır; soru dil modeline gitmez. Eskiden bu devam sorusu
/// modele gidiyor, model önceki sonucu yanlış bölüp "4.856" diyebiliyordu.
/// </summary>
public static partial class SpokenMath
{
    private static readonly Dictionary<string, double> Dative = new(StringComparer.Ordinal)
    {
        ["ikiye"] = 2, ["üçe"] = 3, ["dörde"] = 4, ["beşe"] = 5, ["altıya"] = 6, ["yediye"] = 7, ["sekize"] = 8, ["dokuza"] = 9,
        ["ona"] = 10, ["yirmiye"] = 20, ["yüze"] = 100, ["bine"] = 1000,
    };

    /// <summary>Önceki hesabın üzerine yeni işlem ("bunu ikiye böl"); devam değilse null. Dönen metin yeni hesap sorusudur.</summary>
    public static string? Continue(string text, string previousQuestion)
    {
        var said = text.Trim().TrimEnd('?', '.', '!').ToLower(Turkish);
        said = Spaces().Replace(said, " ");
        if (said.Split(' ').Length > 6 || Answer(previousQuestion) is not { } previous || Result().Match(previous) is not { Success: true } result
            || Number(result.Groups["n"].Value) is not { } value)
        {
            return null;
        }

        said = Referent().Replace(said, "").Trim();
        var number = value.ToString("0.######", Turkish);
        string? Operand(string word) =>
            Dative.TryGetValue(word, out var fixedValue) ? fixedValue.ToString(CultureInfo.InvariantCulture)
            : Number(DativeEnd().Replace(word, "")) is { } parsed ? parsed.ToString("0.######", Turkish)
            : null;

        if (Divide().Match(said) is { Success: true } divide && Operand(divide.Groups["n"].Value) is { } divisor)
        {
            return $"{number} bölü {divisor}";
        }

        if (Multiply().Match(said) is { Success: true } multiply && Operand(multiply.Groups["n"].Value) is { } factor)
        {
            return $"{number} çarpı {factor}";
        }

        if (Add().Match(said) is { Success: true } add && Operand(add.Groups["n"].Value) is { } added)
        {
            return $"{number} artı {added}";
        }

        if (Subtract().Match(said) is { Success: true } subtract && Operand(subtract.Groups["n"].Value) is { } taken)
        {
            return $"{number} eksi {taken}";
        }

        if (PercentTail().Match(said) is { Success: true } percent && Operand(percent.Groups["n"].Value) is { } rate)
        {
            return $"{number}'in yüzde {rate}'i kaç";
        }

        return said switch
        {
            "yarısı" or "yarısını al" or "yarısı kaç" or "yarısı ne kadar" or "yarısını söyle" => $"{number} bölü 2",
            "iki katı" or "iki katı kaç" or "iki katını al" => $"{number} çarpı 2",
            "karesi" or "karesi kaç" or "karesini al" => $"{number} çarpı {number}",
            _ => null
        };
    }

    [GeneratedRegex(@"(?<n>-?[\d.]+(?:,\d+)?)(?: [\p{L} ]+)? eder\.$")]
    private static partial Regex Result();

    [GeneratedRegex(@"^(?:peki |şimdi |sonra |ve )?(?:bunu|sonucu|onu|bunun|sonucun|çıkanı) ?")]
    private static partial Regex Referent();

    [GeneratedRegex(@"^(?<n>[\p{L}\d.,']+) böl(?:ersem|ünce|ersek)?(?: kaç| ne olur| kaç eder)?$")]
    private static partial Regex Divide();

    [GeneratedRegex(@"^(?<n>[\p{L}\d.,']+?)(?: ile|['’]?(?:la|le))? çarp(?:arsam|ınca)?(?: kaç| ne olur| kaç eder)?$")]
    private static partial Regex Multiply();

    [GeneratedRegex(@"^(?:üstüne |buna )?(?<n>[\p{L}\d.,']+?) (?:ekle|topla|ekleyince|eklersem|artır)(?: kaç| ne olur| kaç eder)?$")]
    private static partial Regex Add();

    [GeneratedRegex(@"^(?:bundan |sonuçtan )?(?<n>[\p{L}\d.,']+?) (?:çıkar|eksilt|azalt|çıkarınca|çıkarırsam)(?: kaç| ne olur| kaç eder)?$")]
    private static partial Regex Subtract();

    [GeneratedRegex(@"^yüzde (?<n>[\d.,]+|[\p{L}]+)['’]?(?:[ıiuü]|[sn][ıiuü]|[ıiuü]n[ıiuü])?(?: kaç| ne kadar| ne eder| nedir)?$")]
    private static partial Regex PercentTail();

    [GeneratedRegex(@"['’]?(?:ye|ya|e|a|ten|tan|den|dan|i|ı|u|ü|yi|yı)$")]
    private static partial Regex DativeEnd();
}
