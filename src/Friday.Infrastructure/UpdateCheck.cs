using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed record UpdateInfo(bool Available, string Message, string? Url);

/// <summary>
/// "Güncellemeleri denetle": GitHub'daki son sürümün numarasına bakar. Yalnızca düğmeye basınca çalışır; GitHub'a
/// kimlik ya da kullanım bilgisi gönderilmez, istek gizlilik defterine yazılır.
/// </summary>
public static class UpdateCheck
{
    public const string Repository = "kursatcnk/Friday";

    public static async Task<UpdateInfo> CheckAsync(HttpClient http, Version current, PrivacyLedger ledger, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Friday", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        ledger.Record(DataFlow.Outbound, "Güncelleme", "GitHub'da son sürüm numarasına bakıldı");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateInfo(false, "Sürüm bilgisi alınamadı; biraz sonra yeniden dene.", null);
            }

            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            var tag = json?["tag_name"]?.GetValue<string>() ?? "";
            var url = json?["html_url"]?.GetValue<string>();
            return Compare(current, tag, url);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new UpdateInfo(false, "İnternete ulaşılamadı; güncelleme denetlenemedi.", null);
        }
    }

    /// <summary>"v0.2.1" ile çalışan sürümü karşılaştırır.</summary>
    public static UpdateInfo Compare(Version current, string tag, string? url)
    {
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
        {
            return new UpdateInfo(false, "Yayınlanmış bir sürüm bulunamadı.", null);
        }

        var running = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        return latest > running
            ? new UpdateInfo(true, string.Create(CultureInfo.InvariantCulture, $"Yeni sürüm var: {latest}. Sende {running} çalışıyor."), url)
            : new UpdateInfo(false, string.Create(CultureInfo.InvariantCulture, $"Güncelsin ({running})."), url);
    }
}
