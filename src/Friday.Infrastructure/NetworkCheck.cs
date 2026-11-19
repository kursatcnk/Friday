using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Bağlantı kontrolü: etkin bağdaştırıcı, modeme ve 1.1.1.1'e ping, ad çözme süresi. Birkaç saniye sürer.</summary>
public static class NetworkCheck
{
    public static async Task<NetworkProbe> ProbeAsync(CancellationToken cancellationToken)
    {
        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up
                && item.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                && item.GetIPProperties().GatewayAddresses.Any(gateway => gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
            .OrderBy(item => item.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
            .FirstOrDefault();
        if (adapter is null)
        {
            return new NetworkProbe("", null, null, 1, null);
        }

        var name = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "kablolu ağ" : adapter.Name;
        var gateway = adapter.GetIPProperties().GatewayAddresses.First(item => item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Address;
        var gatewayTask = PingAsync(gateway, 3);
        var internetTask = PingAsync(IPAddress.Parse("1.1.1.1"), 5);
        var dnsTask = ResolveAsync(cancellationToken);
        await Task.WhenAll(gatewayTask, internetTask, dnsTask).ConfigureAwait(false);
        var (gatewayMs, _) = gatewayTask.Result;
        var (internetMs, loss) = internetTask.Result;
        return new NetworkProbe(name, gatewayMs, internetMs, loss, dnsTask.Result);
    }

    private static async Task<(double? Average, double Loss)> PingAsync(IPAddress address, int count)
    {
        using var ping = new Ping();
        var times = new List<long>();
        for (var index = 0; index < count; index++)
        {
            try
            {
                var reply = await ping.SendPingAsync(address, 1000).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    times.Add(reply.RoundtripTime);
                }
            }
            catch (PingException)
            {
                // Ulaşılamayan adres kayıp sayılır.
            }
        }

        return (times.Count == 0 ? null : times.Average(), 1 - (times.Count / (double)count));
    }

    private static async Task<double?> ResolveAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var clock = Stopwatch.StartNew();
        try
        {
            // Var olmayan bir adı sormak Windows'ta yerel ağ aramalarına düşüp saniyeler sürüyor; bilinen adlardan biri sorulur.
            string[] names = ["www.wikipedia.org", "www.cloudflare.com", "www.microsoft.com", "www.google.com"];
            var addresses = await Dns.GetHostAddressesAsync(names[Random.Shared.Next(names.Length)], timeout.Token).ConfigureAwait(false);
            return addresses.Length > 0 ? clock.Elapsed.TotalMilliseconds : null;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
