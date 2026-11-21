using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Dikte edilen metnin temizliği: dolgu sesleri ("ııı", "eee", "hmm") atılır, söylenen noktalama yazıya çevrilir
/// ("virgül" → ",", "soru işareti" → "?", sondaki "nokta" → "."), noktalamadan sonra büyük harfle devam edilir.
/// İçerikte geçen "nokta" ("bu nokta önemli") korunur; yalnızca sondaki nokta işarettir.
/// </summary>
public static partial class DictationText
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Clean(string text)
    {
        var clean = Filler().Replace(text, " ");
        clean = Mark().Replace(clean, match => match.Groups["m"].Value.ToLower(Turkish) switch
        {
            "virgül" => ",",
            "noktalı virgül" => ";",
            "soru işareti" => "?",
            "ünlem" or "ünlem işareti" => "!",
            "iki nokta üst üste" or "iki nokta" => ":",
            _ => match.Value
        });

        // Sondaki "nokta" (Whisper'ın kendi noktası olsun olmasın) cümle sonudur.
        clean = EndDot().Replace(clean, ".");
        clean = SpaceBeforeMark().Replace(clean, "$1");
        clean = DoubleMarks().Replace(clean, "$1");
        clean = Spaces().Replace(clean, " ").Trim();
        clean = AfterSentence().Replace(clean, match => match.Groups["p"].Value + " " + match.Groups["c"].Value.ToUpper(Turkish));
        return clean.Length > 0 ? char.ToUpper(clean[0], Turkish) + clean[1..] : clean;
    }

    [GeneratedRegex(@"(?<![\p{L}])(?:ı{2,}|i{3,}|e{2,}|a{3,}|ı?mm+|hm+|ee+m+)[.,]?(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex Filler();

    [GeneratedRegex(@"\s*(?<![\p{L}])(?<m>noktalı virgül|virgül|soru işareti|ünlem işareti|ünlem|iki nokta üst üste)(?![\p{L}])[.,]?", RegexOptions.IgnoreCase)]
    private static partial Regex Mark();

    [GeneratedRegex(@"\s+nokta[.!?]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EndDot();

    [GeneratedRegex(@"\s+([,.;:?!])")]
    private static partial Regex SpaceBeforeMark();

    // Whisper'ın kendi noktalaması ile söylenen işaret yan yana gelirse söyleneni tut: "nasılsın.?" → "nasılsın?"
    [GeneratedRegex(@"[.,]([?!:;,])|([?!:;])[.,]")]
    private static partial Regex DoubleMarks();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(?<p>[.?!])\s*(?<c>\p{Ll})")]
    private static partial Regex AfterSentence();
}
