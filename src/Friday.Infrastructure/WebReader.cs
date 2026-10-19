using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Friday.Infrastructure;

public sealed record WebResult(string Title, string Url, string Snippet);

/// <summary>
/// İnterneti Friday'in kendisi okur: DuckDuckGo'nun düz HTML sürümünde arar, sayfaları indirip düz metne çevirir.
/// Tarayıcı açılmaz; okunan metin yerel modele verilir. API anahtarı gerekmez.
/// </summary>
public sealed partial class WebReader(HttpClient http)
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36";

    public async Task<IReadOnlyList<WebResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var html = await GetAsync(new Uri("https://html.duckduckgo.com/html/?kl=tr-tr&q=" + Uri.EscapeDataString(query)), cancellationToken)
            .ConfigureAwait(false);
        var results = new List<WebResult>();
        foreach (Match match in ResultPattern().Matches(html))
        {
            var url = DecodeRedirect(WebUtility.HtmlDecode(match.Groups["url"].Value));
            // Reklam sonuçları atlanır.
            if (url is null || url.Contains("duckduckgo.com/y.js", StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(new WebResult(Clean(match.Groups["title"].Value), url, Clean(match.Groups["snippet"].Value)));
            if (results.Count == 6)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>Sayfayı indirip okunabilir metnini çıkarır (menü, betik ve stiller atılır).</summary>
    /// <param name="focus">Verilirse sayfanın başı yerine bu soruyla en ilgili paragraflar seçilir.</param>
    public async Task<string> ReadAsync(Uri url, int maxCharacters, CancellationToken cancellationToken, string? focus = null)
    {
        var html = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        return Extract(html, maxCharacters, focus);
    }

    internal static string Extract(string html, int maxCharacters, string? focus)
    {
        html = NoisePattern().Replace(html, " ");
        var title = Clean(TitlePattern().Match(html).Groups[1].Value);
        var blocks = BlockPattern().Matches(html)
            .Select(match => Clean(match.Groups[2].Value))
            .Where(text => text.Length > 25)
            .Distinct()
            .ToList();

        if (!string.IsNullOrWhiteSpace(focus))
        {
            // Soruyla ortak sözcük sayısına göre puanla; soru sayı istiyorsa (kaç, fiyat, kur, skor…) rakam içeren
            // paragraflar öne çıkar. Seçilenler sayfadaki sırasıyla verilir.
            var terms = focus.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(AssistantTools.Simplify)
                .Where(term => term.Length > 2)
                .Select(term => term.Length > 5 ? term[..5] : term)
                .ToHashSet();
            var wantsNumber = NumericQuestion().IsMatch(focus);
            var ranked = blocks
                .Select((block, index) =>
                {
                    var simple = AssistantTools.Simplify(block);
                    var score = terms.Count(term => simple.Contains(term, StringComparison.Ordinal)) * 2
                                + (wantsNumber && block.Any(char.IsDigit) ? 2 : 0)
                                - (block.Length > 600 ? 1 : 0);
                    return (block, index, score);
                })
                .Where(item => item.score > 0)
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.index);
            var chosen = new List<(string block, int index, int score)>();
            var length = 0;
            foreach (var item in ranked)
            {
                if (length > maxCharacters)
                {
                    break;
                }

                chosen.Add(item);
                length += item.block.Length;
            }

            if (chosen.Count > 0)
            {
                blocks = [.. chosen.OrderBy(item => item.index).Select(item => item.block)];
            }
        }

        var text = new StringBuilder(title.Length > 0 ? title + "\n" : "");
        foreach (var block in blocks)
        {
            text.AppendLine(block);
            if (text.Length > maxCharacters)
            {
                break;
            }
        }

        var result = text.ToString();
        return result.Length > maxCharacters ? result[..maxCharacters] : result;
    }

    private async Task<string> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.AcceptLanguage.ParseAdd("tr-TR,tr;q=0.9,en;q=0.6");
        using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
    }

    /// <summary>DuckDuckGo bağlantıları kendi yönlendiricisinden geçer; asıl adres uddg parametresindedir.</summary>
    internal static string? DecodeRedirect(string link)
    {
        if (link.StartsWith("//", StringComparison.Ordinal))
        {
            link = "https:" + link;
        }

        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsoluteUri;
        }

        var target = uri.Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(part => part[0] == "uddg");
        return target is { Length: 2 } ? Uri.UnescapeDataString(target[1]) : null;
    }

    internal static string Clean(string html) =>
        WhitespacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(html, " ")), " ").Trim();

    [GeneratedRegex("""<a[^>]+class="result__a"[^>]+href="(?<url>[^"]+)"[^>]*>(?<title>.*?)</a>.*?(?:class="result__snippet"[^>]*>(?<snippet>.*?)</a>|</div>)""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ResultPattern();

    [GeneratedRegex(@"<(script|style|noscript|svg|nav|footer|header|aside|form)[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NoisePattern();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(@"<(p|h1|h2|h3|li|td|blockquote)[^>]*>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\b(kaç|kac|ne kadar|fiyat|kur|skor|sonuç|sonuc|puan|yüzde|derece|tarih|saat|nüfus|yaş)", RegexOptions.IgnoreCase)]
    private static partial Regex NumericQuestion();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
