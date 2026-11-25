using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Friday.Infrastructure;

/// <summary>
/// Telefondan Friday: yerel ağda küçük bir web sayfası. Telefonun tarayıcısından yazılan soru Friday'e gider, cevap
/// telefonda görünür; rutin, müzik ve ses düğmeleri de vardır. Yalnızca ev ağından (özel IP) ve her istekte gizli
/// anahtarla çalışır; varsayılan kapalıdır. Ek bir sunucu ya da indirme yoktur.
/// </summary>
public sealed class RemotePanel : IDisposable
{
    private const int MaxRequestBytes = 16 * 1024;

    /// <summary>Aynı anda açık bağlantı sınırı: yarım bırakılan bağlantılar sunucuyu tüketemez.</summary>
    private const int MaxConnections = 8;

    /// <summary>Bir isteğin başlığı bu sürede gelmezse bağlantı kapanır (async okumada ReceiveTimeout işlemez).</summary>
    private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(10);

    /// <summary>Bu kadar yanlış anahtardan sonra panel bir süre kimseyi kabul etmez (anahtar denemeye karşı).</summary>
    private const int MaxFailures = 20;

    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _connections = new(MaxConnections, MaxConnections);
    private readonly Queue<DateTime> _failures = new();
    private DateTime _lockedUntil;

    /// <summary>Türkçe harfler kaçışsız yazılır; sayfa cevabı textContent ile gösterdiği için HTML olarak yorumlanmaz.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Func<string, bool, CancellationToken, Task<string>> _ask;
    private readonly Func<IReadOnlyList<string>> _routines;
    private readonly SemaphoreSlim _one = new(1, 1);
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private byte[] _key = [];

    public RemotePanel(Func<string, bool, CancellationToken, Task<string>> ask, Func<IReadOnlyList<string>> routines)
    {
        _ask = ask;
        _routines = routines;
    }

    public bool IsRunning => _listener is not null;

    public int Port { get; private set; }

    /// <summary>Yeni bir gizli anahtar (telefon bağlantısındaki "k=" değeri).</summary>
    public static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    /// <summary>Bu bilgisayarın ev ağındaki adresleri: "http://192.168.1.23:8765/?k=…".</summary>
    public static IReadOnlyList<string> Addresses(int port, string key) =>
    [
        .. NetworkInterface.GetAllNetworkInterfaces()
            .Where(card => card.OperationalStatus == OperationalStatus.Up && card.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(card => card.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(address))
            .Select(address => $"http://{address}:{port}/?k={key}")
            .Distinct()
    ];

    /// <param name="bind">Varsayılan bütün ağ kartları; testlerde yalnızca bu bilgisayar (127.0.0.1).</param>
    public void Start(int port, string key, IPAddress? bind = null)
    {
        Stop();
        _key = Encoding.ASCII.GetBytes(key);
        _stop = new CancellationTokenSource();
        _listener = new TcpListener(bind ?? IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptAsync(_listener, _stop.Token);
        FridayLog.Info($"Telefondan erişim açık: port {port}");
    }

    public void Stop()
    {
        _stop?.Cancel();
        _listener?.Stop();
        _listener = null;
        _stop?.Dispose();
        _stop = null;
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            if (!_connections.Wait(0, CancellationToken.None))
            {
                // Sınır dolu: yeni bağlantı beklemeden kapatılır.
                client.Dispose();
                continue;
            }

            _ = ServeLimitedAsync(client, cancellationToken);
        }
    }

    private async Task ServeLimitedAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            await ServeAsync(client, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connections.Release();
        }
    }

    /// <summary>Yanlış anahtar sayılır; kısa sürede çok fazlaysa panel bir süre kilitlenir.</summary>
    private bool Locked(bool failed)
    {
        lock (_failures)
        {
            var now = DateTime.Now;
            if (failed)
            {
                _failures.Enqueue(now);
                while (_failures.Count > 0 && now - _failures.Peek() > LockFor)
                {
                    _failures.Dequeue();
                }

                if (_failures.Count >= MaxFailures)
                {
                    _lockedUntil = now + LockFor;
                    _failures.Clear();
                    FridayLog.Warn($"Telefondan erişim: çok fazla yanlış anahtar; {LockFor.TotalMinutes:0} dakika kilitlendi");
                }
            }

            return now < _lockedUntil;
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                if (client.Client.RemoteEndPoint is not IPEndPoint { Address: var remote } || !IsPrivate(remote))
                {
                    return;
                }

                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();
                using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                reading.CancelAfter(ReadLimit);
                var (method, target, headers, body) = await ReadRequestAsync(stream, reading.Token).ConfigureAwait(false);
                var path = target.Split('?')[0];
                if (method == "GET" && path == "/")
                {
                    await WriteAsync(stream, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (Locked(failed: false))
                {
                    await WriteJsonAsync(stream, "429 Too Many Requests", new JsonObject { ["error"] = "Çok fazla yanlış deneme; panel bir süre kapalı." }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!headers.TryGetValue("x-friday-key", out var key) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(key), _key))
                {
                    Locked(failed: true);
                    await WriteJsonAsync(stream, "403 Forbidden", new JsonObject { ["error"] = "Anahtar yanlış. Bağlantıyı Friday'in Ayarlar sayfasından yeniden aç." }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (method == "GET" && path == "/routines")
                {
                    var list = new JsonArray([.. _routines().Select(name => (JsonNode)JsonValue.Create(name)!)]);
                    await WriteJsonAsync(stream, "200 OK", new JsonObject { ["routines"] = list }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (method == "POST" && path == "/ask")
                {
                    var request = JsonNode.Parse(body) as JsonObject;
                    var text = request?["text"]?.GetValue<string>()?.Trim() ?? "";
                    var speak = request?["speak"]?.GetValue<bool>() ?? false;
                    if (text.Length is 0 or > 1000)
                    {
                        await WriteJsonAsync(stream, "400 Bad Request", new JsonObject { ["error"] = "Boş ya da çok uzun mesaj." }, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
                    string reply;
                    try
                    {
                        reply = await _ask(text, speak, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _one.Release();
                    }

                    await WriteJsonAsync(stream, "200 OK", new JsonObject { ["reply"] = reply }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await WriteAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Yok"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or JsonException or InvalidOperationException or FormatException
                                                  or OperationCanceledException)
            {
                // Yarım kalan, bozuk ya da zamanında gelmeyen istek: bağlantı kapanır, Friday etkilenmez.
            }
        }
    }

    private static async Task<(string Method, string Target, Dictionary<string, string> Headers, string Body)> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestBytes];
        var filled = 0;
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("Bağlantı kapandı.");
            }

            filled += read;
            headerEnd = buffer.AsSpan(0, filled).IndexOf("\r\n\r\n"u8);
            if (headerEnd < 0 && filled == buffer.Length)
            {
                throw new IOException("İstek çok büyük.");
            }
        }

        var lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var first = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        var length = headers.TryGetValue("content-length", out var value) && int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 0, MaxRequestBytes) : 0;
        var bodyStart = headerEnd + 4;
        while (filled - bodyStart < length && filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        var body = Encoding.UTF8.GetString(buffer, bodyStart, Math.Min(length, filled - bodyStart));
        return (first[0], first.Length > 1 ? first[1] : "/", headers, body);
    }

    private static Task WriteJsonAsync(NetworkStream stream, string status, JsonObject json, CancellationToken cancellationToken) =>
        WriteAsync(stream, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json.ToJsonString(JsonOptions)), cancellationToken);

    private static async Task WriteAsync(NetworkStream stream, string status, string type, byte[] body, CancellationToken cancellationToken)
    {
        var header = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ev ağı adresleri: 10.x, 172.16-31.x, 192.168.x, 169.254.x ve bu bilgisayar.</summary>
    private static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254));
    }

    public void Dispose()
    {
        Stop();
        _one.Dispose();
        _connections.Dispose();
    }

    private const string Page = """
        <!doctype html>
        <html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Friday</title>
        <style>
        :root{color-scheme:dark}*{box-sizing:border-box}body{margin:0;font-family:system-ui,Segoe UI,sans-serif;background:#0e0e10;color:#ece8e1}
        header{padding:16px 16px 8px;display:flex;align-items:center;gap:10px}.orb{width:26px;height:26px;border-radius:50%;background:radial-gradient(circle at 35% 30%,#fff6e9,#f2cc8f 50%,#6b5536)}
        h1{font-size:18px;margin:0}#log{padding:8px 16px;display:flex;flex-direction:column;gap:10px;min-height:40vh}
        .me,.fr{max-width:85%;padding:10px 12px;border-radius:14px;line-height:1.4;white-space:pre-wrap}.me{align-self:flex-end;background:#2a2622}.fr{align-self:flex-start;background:#18181b;border:1px solid #2b2b30}
        .chips{display:flex;flex-wrap:wrap;gap:8px;padding:8px 16px}button{font:inherit;color:#ece8e1;background:#1d1d21;border:1px solid #303036;border-radius:18px;padding:8px 12px}
        form{position:sticky;bottom:0;display:flex;gap:8px;padding:12px 16px;background:#0e0e10;border-top:1px solid #222}input[type=text]{flex:1;font:inherit;color:#ece8e1;background:#18181b;border:1px solid #303036;border-radius:20px;padding:10px 14px}
        .send{background:#ff8a5b;border-color:#ff8a5b;color:#1a120d;font-weight:600}label{font-size:13px;color:#a8a29a;padding:0 16px}
        </style></head><body>
        <header><div class="orb"></div><h1>Friday</h1></header>
        <div class="chips" id="chips"></div>
        <label><input type="checkbox" id="speak"> Cevabı bilgisayarda da sesli söyle</label>
        <div id="log"></div>
        <form id="f"><input type="text" id="q" placeholder="Friday'e yaz…" autocomplete="off"><button class="send">Gönder</button></form>
        <script>
        const p=new URLSearchParams(location.search);let key=p.get('k')||localStorage.getItem('fridayKey')||'';if(p.get('k')){localStorage.setItem('fridayKey',key);history.replaceState(null,'','/');}
        const log=document.getElementById('log');function add(t,c){const d=document.createElement('div');d.className=c;d.textContent=t;log.appendChild(d);d.scrollIntoView({behavior:'smooth'});return d;}
        async function ask(text){add(text,'me');const w=add('…','fr');try{const r=await fetch('/ask',{method:'POST',headers:{'Content-Type':'application/json','X-Friday-Key':key},body:JSON.stringify({text,speak:document.getElementById('speak').checked})});const j=await r.json();w.textContent=j.reply||j.error||'Cevap gelmedi.';}catch(e){w.textContent='Friday\'e ulaşılamadı. Bilgisayar açık ve aynı ağda mı?';}}
        document.getElementById('f').onsubmit=e=>{e.preventDefault();const q=document.getElementById('q');if(q.value.trim()){ask(q.value.trim());q.value='';}};
        const fixed=['Müziği oynat/durdur','Sonraki şarkı','Sesi kıs','Sesi aç','Ekran sürem','Bugün neler var?','Bilgisayarı kilitle'];
        function chip(t,send){const b=document.createElement('button');b.textContent=t;b.onclick=()=>ask(send||t);document.getElementById('chips').appendChild(b);}
        chip('Müziği oynat/durdur','müziği durdur');chip('Sonraki şarkı');chip('Sesi kıs');chip('Sesi aç');chip('Ekran sürem');chip('Bugün neler var?');chip('Bilgisayarı kilitle');
        fetch('/routines',{headers:{'X-Friday-Key':key}}).then(r=>r.json()).then(j=>(j.routines||[]).forEach(n=>chip('▶ '+n,n))).catch(()=>{});
        if(!key)add('Bağlantıyı Friday\'in Ayarlar › Telefondan erişim bölümündeki adresle aç.','fr');
        </script></body></html>
        """;
}
