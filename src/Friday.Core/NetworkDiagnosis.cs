namespace Friday.Core;

/// <summary>Bağlantı ölçümü: modeme (ağ geçidi) ve internete gecikme, kayıp oranı ve ad çözme (DNS) süresi; ulaşılamayan null.</summary>
public sealed record NetworkProbe(string Adapter, double? GatewayMs, double? InternetMs, double InternetLoss, double? DnsMs);

/// <summary>Ölçümden sesli okunacak teşhis: sorun nerede (Wi-Fi/modem, servis sağlayıcı, DNS) ve ne yapılabilir.</summary>
public static class NetworkDiagnosis
{
    public static string Describe(NetworkProbe probe)
    {
        if (probe.Adapter.Length == 0)
        {
            return "Bilgisayar hiçbir ağa bağlı görünmüyor. Wi-Fi'ı ya da kabloyu kontrol et.";
        }

        if (probe.GatewayMs is null && probe.InternetMs is null)
        {
            return $"{probe.Adapter} bağlı ama modeme ulaşamıyorum. Modemi yeniden başlatmayı ya da ağa yeniden bağlanmayı dene.";
        }

        if (probe.InternetMs is null)
        {
            return "Modem cevap veriyor ama internete çıkılamıyor; sorun büyük ihtimalle servis sağlayıcıda. Modemin internet ışığına bak, birkaç dakika sonra yeniden dene.";
        }

        if (probe.DnsMs is null)
        {
            return "İnternete ulaşılıyor ama site adları çözülemiyor; DNS sorunu var. Ağ ayarlarında DNS'i 1.1.1.1 ya da 8.8.8.8 yapmak genelde çözer.";
        }

        var ms = Math.Round(probe.InternetMs.Value);
        var quality = probe.InternetLoss >= 0.25 ? "dalgalı" : ms switch
        {
            < 40 => "çok iyi",
            < 80 => "iyi",
            < 150 => "biraz yavaş",
            _ => "yavaş"
        };
        var details = $"internete {ms} ms" + (probe.GatewayMs is { } gateway ? $", modeme {Math.Round(gateway)} ms" : "") + (probe.DnsMs is { } dns ? $", ad çözme {Math.Round(dns)} ms" : "");
        var advice = probe.InternetLoss >= 0.25
            ? " Paket kaybı var; görüntülü görüşmede donma olabilir. Wi-Fi'daysan modeme yaklaşmak ya da kablo kullanmak iyi gelir."
            : probe.GatewayMs is > 30 && probe.Adapter.Contains("Wi", StringComparison.OrdinalIgnoreCase)
                ? " Modeme gecikme yüksek; Wi-Fi sinyali zayıf olabilir."
                : "";
        return $"Bağlantı {quality} ({probe.Adapter}): {details}.{advice}";
    }
}
