using System.Net;
using System.Text.Json;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed partial class AssistantTools
{
    /// <summary>
    /// Vikipedi özeti: önce başlık olarak denenir, bulunamazsa Vikipedi'nin kendi aramasıyla en yakın madde seçilir.
    /// Türkçe madde yoksa İngilizcesine bakılır. Yalnızca konu adı gönderilir.
    /// </summary>
    private async Task<string> EncyclopediaAsync(string topic, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return "Hangi konuya bakacağımı söylemedin.";
        }

        foreach (var language in new[] { "tr", "en" })
        {
            var title = await WikiTitleAsync(language, topic.Trim(), cancellationToken).ConfigureAwait(false);
            if (title is null)
            {
                continue;
            }

            var url = $"https://{language}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Friday/1.0 (local assistant)");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
            {
                continue;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "disambiguation")
            {
                continue;
            }

            var extract = root.TryGetProperty("extract", out var text) ? text.GetString() ?? "" : "";
            if (extract.Length == 0)
            {
                continue;
            }

            ledger.Record(DataFlow.Outbound, "Vikipedi", $"\"{topic}\" konusu arandı ({language}.wikipedia.org)");
            var clipped = extract.Length > 900 ? extract[..extract.LastIndexOf('.', 900)] + "." : extract;
            return $"Vikipedi ({(language == "tr" ? "Türkçe" : "İngilizce")}), \"{title}\": {clipped}";
        }

        return $"Vikipedi'de \"{topic}\" için madde bulamadım; internette_ara ile bakılabilir.";
    }

    private async Task<string?> WikiTitleAsync(string language, string topic, CancellationToken cancellationToken)
    {
        var url = $"https://{language}.wikipedia.org/w/api.php?action=opensearch&limit=1&namespace=0&format=json&search={Uri.EscapeDataString(topic)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Friday/1.0 (local assistant)");
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return document.RootElement is { ValueKind: JsonValueKind.Array } root && root.GetArrayLength() > 1
               && root[1] is { ValueKind: JsonValueKind.Array } titles && titles.GetArrayLength() > 0
            ? titles[0].GetString()
            : null;
    }
}
