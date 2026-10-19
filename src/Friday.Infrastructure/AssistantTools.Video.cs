using System.Text.Json;
using System.Text.RegularExpressions;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>"X videosunu aç": YouTube'da arar ve ilk videoyu tarayıcıda açar. API anahtarı gerekmez.</summary>
public sealed partial class AssistantTools
{
    private const string BrowserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36";

    private async Task<string> OpenVideoAsync(string query, CancellationToken cancellationToken)
    {
        var text = query.Trim();
        if (text.Length == 0)
        {
            return "Hangi videonun açılacağı söylenmedi.";
        }

        // Doğrudan bir YouTube adresi verildiyse arama yapılmaz.
        if (Uri.TryCreate(text, UriKind.Absolute, out var direct) && direct.Scheme is "http" or "https"
            && (direct.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) || direct.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase)))
        {
            Launch(direct.AbsoluteUri);
            ledger.Record(DataFlow.Outbound, "Tarayıcı", $"{direct.Host} açıldı");
            return "Video tarayıcıda açıldı.";
        }

        var search = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(text);
        ledger.Record(DataFlow.Outbound, "YouTube araması", $"youtube.com · \"{text}\"");
        (string Id, string Title)? video;
        try
        {
            video = await FindFirstVideoAsync(new Uri(search), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            video = null;
        }

        if (video is not { } found)
        {
            // Video seçilemezse en azından arama sonuçları açılır.
            Launch(search);
            return $"Tek bir video seçemedim; \"{text}\" için YouTube arama sonuçlarını açtım.";
        }

        Launch($"https://www.youtube.com/watch?v={found.Id}");
        ledger.Record(DataFlow.Outbound, "Tarayıcı", $"youtube.com · \"{found.Title}\" açıldı");
        return $"\"{found.Title}\" videosu tarayıcıda açıldı.";
    }

    /// <summary>
    /// Arama sayfası ~1,5 MB ve ilk video sayfanın ortalarında. Sayfa akış olarak okunur; ilk video bulunduğu anda
    /// indirme kesilir.
    /// </summary>
    private async Task<(string Id, string Title)?> FindFirstVideoAsync(Uri search, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, search);
        request.Headers.UserAgent.ParseAdd(BrowserAgent);
        request.Headers.AcceptLanguage.ParseAdd("tr-TR,tr;q=0.9,en;q=0.6");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var page = new System.Text.StringBuilder();
        var buffer = new char[65_536];
        int read;
        while ((read = await reader.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            page.Append(buffer, 0, read);
            var text = page.ToString();
            var start = text.IndexOf("\"videoRenderer\"", StringComparison.Ordinal);
            // Başlığın da gelmesi için ilk videonun ardından birkaç KB beklenir.
            if (start >= 0 && text.Length - start > 6_000)
            {
                return FirstVideo(text[start..]);
            }
        }

        return FirstVideo(page.ToString());
    }

    /// <summary>YouTube arama sayfasındaki ilk normal videonun kimliği ve başlığı (reklam ve kısa videolar farklı yapıdadır).</summary>
    internal static (string Id, string Title)? FirstVideo(string html)
    {
        var match = VideoPattern().Match(html);
        if (!match.Success)
        {
            return null;
        }

        string title;
        try
        {
            // Başlık JSON metni olarak kaçışlıdır (&, \" …).
            title = JsonSerializer.Deserialize<string>("\"" + match.Groups["title"].Value + "\"") ?? "";
        }
        catch (JsonException)
        {
            title = match.Groups["title"].Value;
        }

        return (match.Groups["id"].Value, title);
    }

    [GeneratedRegex("""
        "videoRenderer":\{"videoId":"(?<id>[\w-]{11})".*?"title":\{"runs":\[\{"text":"(?<title>(?:[^"\\]|\\.)*)"
        """, RegexOptions.Singleline)]
    private static partial Regex VideoPattern();
}
