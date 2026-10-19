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
        foreach (var pattern in new[] { YouTubeFirst(), YouTubeLast(), VideoLast() })
        {
            var match = pattern.Match(sentence);
            if (match.Success && match.Groups["q"].Value.Trim() is { Length: > 1 } query)
            {
                return new QuickCommand("video_ac", new Dictionary<string, string> { ["arama"] = query });
            }
        }

        return null;
    }

    /// <summary>
    /// Yalnızca son cümleye bakılır ("İyiyim. YouTube'dan X aç." → "YouTube'dan X aç"). Hitap, nezaket sözleri ve
    /// noktalama atılır; "dinlemek istiyorum", "açar mısın", "çal" gibi biçimler "aç"a çevrilir.
    /// </summary>
    private static string Normalize(string text)
    {
        var sentences = SentenceBreak().Split(text.Trim()).Where(part => part.Trim().Length > 0).ToArray();
        var sentence = (sentences.Length > 0 ? sentences[^1] : text).Trim().TrimEnd('.', '!', '?', ' ').ToLower(Turkish);
        sentence = Address().Replace(sentence, "");
        sentence = Politeness().Replace(" " + sentence, " ");
        sentence = OpenVerbs().Replace(sentence, " aç");
        return Whitespace().Replace(sentence, " ").Trim();
    }

    // "youtube'da lofi aç", "youtube'dan power to resist şarkısını aç"
    [GeneratedRegex(@"^youtube['’]?(?:da|de|ta|te|dan|den|tan|ten)\s+(?<q>.+?)(?:\s+(?:videosunu|klibini|şarkısını|şarkısının klibini|parçasını|müziğini))?\s+aç$")]
    private static partial Regex YouTubeFirst();

    // "power to resist'i youtube'dan aç"
    [GeneratedRegex(@"^(?<q>.+?)\s+youtube['’]?(?:da|de|ta|te|dan|den|tan|ten)\s+aç$")]
    private static partial Regex YouTubeLast();

    // "tarkan'ın kuzu kuzu klibini aç", "kedi videosu aç", "naruto kokuten şarkısını aç"
    [GeneratedRegex(@"^(?<q>.+?)\s+(?:videosunu|videosu|klibini|klibi|şarkısını|şarkısının klibini|parçasını|müziğini)\s+aç$")]
    private static partial Regex VideoLast();

    [GeneratedRegex(@"[.!?]+\s+")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"^(?:hey\s+)?friday[,\s]+")]
    private static partial Regex Address();

    [GeneratedRegex(@"\s+(?:lütfen|bana|benim için)(?=\s|$)")]
    private static partial Regex Politeness();

    // "aç" anlamına gelen söyleyişler: oynat, çal, başlat, dinlemek istiyorum, izlemek istiyorum, açar mısın…
    [GeneratedRegex(@"\s+(?:açar mısın|açabilir misin|açsana|açıver|aç|oynatır mısın|oynatabilir misin|oynatsana|oynat|çalar mısın|çalabilir misin|çalsana|çal|başlat|dinlemek istiyorum|dinlemek isterim|dinleyelim|dinlet|izlemek istiyorum|izlemek isterim|izleyelim|izlet)$")]
    private static partial Regex OpenVerbs();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
