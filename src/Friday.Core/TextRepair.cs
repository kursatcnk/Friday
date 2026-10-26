using System.Text;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// UTF-8 metin bir kez yanlışlıkla Windows-1252 olarak okunup yeniden yazılınca Türkçe harfler bozulur:
/// "İstanbul" → "Ä°stanbul", "görev" → "gÃ¶rev". Bu bozulmayı geri alır; bozulmamış metne dokunmaz.
/// </summary>
public static partial class TextRepair
{
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    public static string FixMojibake(string text)
    {
        if (text.AsSpan().IndexOfAny('Ã', 'Ä', 'Å') < 0)
        {
            return text;
        }

        return Broken().Replace(text, match =>
        {
            try
            {
                var repaired = StrictUtf8.GetString(Windows1252.GetBytes(match.Value));
                return repaired.Length == 1 ? repaired : match.Value;
            }
            catch (DecoderFallbackException)
            {
                return match.Value;
            }
        });
    }

    /// <summary>İki baytlık bir UTF-8 harfinin Windows-1252'deki görünümü (ör. "Ã¶", "ÅŸ", "Ä±").</summary>
    [GeneratedRegex("[ÃÄÅ][\u0080-¿ŒœŠšŸŽžƒˆ˜–—‘-„†-•…‰‹›€™]")]
    private static partial Regex Broken();
}
