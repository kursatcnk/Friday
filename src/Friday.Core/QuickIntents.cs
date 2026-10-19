using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Kalıbı belli bir komutun doğrudan çalıştırılacak aracı.</summary>
public sealed record QuickCommand(string Tool, IReadOnlyDictionary<string, string> Arguments);

/// <summary>
/// "YouTube'da X aç", "X klibini aç" gibi kalıbı belli komutları dil modeline sormadan tanır. Model böyle bir komutta
/// aracı çağırmadan önce saniyelerce düşünebiliyor; hızlı yol bunu atlar. Kalıba uymayan her şey modele gider.
/// </summary>
public static partial class QuickIntents
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static QuickCommand? Match(string text)
    {
        var sentence = Normalize(text);
        foreach (var pattern in new[] { YouTubeFirst(), VideoLast() })
        {
            var match = pattern.Match(sentence);
            if (match.Success && match.Groups["q"].Value.Trim() is { Length: > 1 } query)
            {
                return new QuickCommand("video_ac", new Dictionary<string, string> { ["arama"] = query });
            }
        }

        return null;
    }

    /// <summary>Hitap ("Friday,"), nezaket sözleri ve noktalama atılır; Türkçe küçük harfe çevrilir.</summary>
    private static string Normalize(string text)
    {
        var sentence = text.Trim().TrimEnd('.', '!', '?', ' ').ToLower(Turkish);
        sentence = Address().Replace(sentence, "");
        sentence = Politeness().Replace(sentence, "");
        // "açar mısın", "açsana" → "aç"; "oynatır mısın" → "oynat"
        sentence = PoliteOpen().Replace(sentence, " aç");
        sentence = PolitePlay().Replace(sentence, " oynat");
        return Whitespace().Replace(sentence, " ").Trim();
    }

    // "youtube'da lofi aç", "youtube'dan tarkan kuzu kuzu oynat"
    [GeneratedRegex(@"^youtube['’]?(?:da|ta|dan|tan)\s+(?<q>.+?)\s+(?:videosunu\s+|klibini\s+|şarkısını\s+)?(?:aç|oynat|başlat|izlet|çal)$")]
    private static partial Regex YouTubeFirst();

    // "tarkan'ın kuzu kuzu klibini aç", "kedi videosu aç", "x şarkısının klibini oynat"
    [GeneratedRegex(@"^(?<q>.+?)\s+(?:videosunu|videosu|klibini|klibi|şarkısının klibini)\s+(?:aç|oynat|başlat|izlet)$")]
    private static partial Regex VideoLast();

    [GeneratedRegex(@"^(?:hey\s+)?friday[,\s]+")]
    private static partial Regex Address();

    [GeneratedRegex(@"\s+(?:lütfen|bana|benim için)(?=\s|$)")]
    private static partial Regex Politeness();

    [GeneratedRegex(@"\s+(?:açar mısın|açabilir misin|açsana|açıver)$")]
    private static partial Regex PoliteOpen();

    [GeneratedRegex(@"\s+(?:oynatır mısın|oynatabilir misin|oynatsana)$")]
    private static partial Regex PolitePlay();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
