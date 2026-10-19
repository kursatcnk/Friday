using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Modelin yazdığı metni sesli okunacak hale getirir: markdown, emoji ve bağlantıları temizler.</summary>
public static partial class SpokenText
{
    public static string Clean(string text)
    {
        var cleaned = MarkdownLinkPattern().Replace(text, "$1");
        cleaned = UrlPattern().Replace(cleaned, "bağlantı");
        cleaned = ListMarkerPattern().Replace(cleaned, "");
        cleaned = cleaned.Replace("**", "", StringComparison.Ordinal)
            .Replace("__", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal)
            .Replace("#", "", StringComparison.Ordinal)
            .Replace("*", "", StringComparison.Ordinal);

        var builder = new StringBuilder(cleaned.Length);
        foreach (var rune in cleaned.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            // Emoji ve semboller okunmaz; Türkçe harfler ve noktalama kalır.
            if (category is UnicodeCategory.OtherSymbol or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                || rune.Value is 0xFE0F or 0x200D)
            {
                continue;
            }

            builder.Append(rune.ToString());
        }

        return WhitespacePattern().Replace(NumbersToWords(builder.ToString()), " ").Trim();
    }

    /// <summary>Saatleri, yüzdeleri, ondalıkları ve tam sayıları Türkçe okunuşlarına çevirir.</summary>
    public static string NumbersToWords(string text)
    {
        var result = TimePattern().Replace(text, match => TurkishNumbers.TimeToWords(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
        // Türkçede nokta binlik ayırıcıdır: "1.250" bin iki yüz elli.
        result = ThousandsPattern().Replace(result, match => match.Value.Replace(".", "", StringComparison.Ordinal));
        result = PercentPattern().Replace(result, match => "yüzde " + (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value));
        result = DecimalPattern().Replace(result, match =>
            $"{Words(match.Groups[1].Value)} virgül {FractionWords(match.Groups[2].Value)}");
        result = NegativePattern().Replace(result, "eksi ");
        return IntegerPattern().Replace(result, match => Words(match.Value));
    }

    private static string Words(string digits) =>
        digits.Length > 15 ? string.Join(' ', digits.Select(digit => TurkishNumbers.ToWords(digit - '0')))
            : TurkishNumbers.ToWords(long.Parse(digits, CultureInfo.InvariantCulture));

    /// <summary>"05" → "sıfır beş": ondalık kısmın baştaki sıfırları okunur.</summary>
    private static string FractionWords(string digits)
    {
        var zeros = digits.Length - digits.TrimStart('0').Length;
        var rest = digits.TrimStart('0');
        var parts = Enumerable.Repeat("sıfır", zeros).ToList();
        if (rest.Length > 0)
        {
            parts.Add(Words(rest));
        }

        return string.Join(' ', parts);
    }

    [GeneratedRegex(@"\b([01]?\d|2[0-3]):([0-5]\d)\b")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"\b\d{1,3}(?:\.\d{3})+\b(?![.,]\d)")]
    private static partial Regex ThousandsPattern();

    [GeneratedRegex(@"%\s?(\d+(?:[.,]\d+)?)|(\d+(?:[.,]\d+)?)\s?%")]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"\b(\d+)[.,](\d+)\b")]
    private static partial Regex DecimalPattern();

    [GeneratedRegex(@"(?<=^|\s)-(?=\d)")]
    private static partial Regex NegativePattern();

    [GeneratedRegex(@"\d+")]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex MarkdownLinkPattern();

    [GeneratedRegex(@"^\s*(?:[-•]|\d+[.)])\s+", RegexOptions.Multiline)]
    private static partial Regex ListMarkerPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
