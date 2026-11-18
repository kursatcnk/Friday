using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Nöbetçiler: "indirme bitince", "bilgisayar boşa çıkınca", "internet gelince", "ekran kartı 85 dereceyi geçerse" haber
/// verir. Yalnızca bir nöbet varken 5 sn'de bir yoklar; nöbet yokken saat durur ve işlemciye dokunmaz. Bir nöbet en fazla
/// 8 saat sürer.
/// </summary>
public sealed partial class WatchService(PrivacyLedger ledger) : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(8);
    private static readonly Guid DownloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");

    private readonly object _gate = new();
    private readonly List<ActiveWatch> _active = [];
    private Timer? _timer;
    private (ulong Idle, ulong Total)? _lastCpu;
    private int _ticking;
    private int _tickCount;

    /// <summary>Söylenecek haber ("İndirme bitti: film.mkv.").</summary>
    public event EventHandler<string>? Fired;

    public IReadOnlyList<WatchRequest> Active
    {
        get
        {
            lock (_gate)
            {
                return [.. _active.Select(watch => watch.Request)];
            }
        }
    }

    public async Task<string> StartAsync(WatchRequest request, CancellationToken cancellationToken)
    {
        var watch = new ActiveWatch(request, DateTime.Now);
        string reply;
        switch (request.Kind)
        {
            case WatchKind.Download:
                var folder = Downloads();
                var partial = Directory.Exists(folder) ? Directory.GetFiles(folder).Where(DownloadFiles.IsPartial).ToList() : [];
                watch.Known = Directory.Exists(folder) ? [.. Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>()] : [];
                watch.SawPartial = partial.Count > 0;
                reply = partial.Count switch
                {
                    0 => "Şu an süren bir indirme görmüyorum; yeni bir indirme başlayıp bitince haber veririm.",
                    1 => $"Tamam, {DownloadFiles.FinalName(partial[0])} inince haber veririm.",
                    _ => $"Tamam, {partial.Count} indirme sürüyor; hepsi bitince haber veririm."
                };
                break;
            case WatchKind.Idle:
                var cpu = await SystemProbe.CpuBusyAsync(TimeSpan.FromMilliseconds(600), cancellationToken).ConfigureAwait(false);
                var gpu = await SystemProbe.GpuAsync(cancellationToken).ConfigureAwait(false);
                var busy = Math.Max(cpu, gpu?.Utilization ?? 0);
                watch.Idle.Update(busy, DateTime.Now);
                reply = busy >= IdleDetector.BusyAbove
                    ? "Tamam, iş bitip bilgisayar boşa çıkınca haber veririm."
                    : $"Şu an yük düşük (işlemci yüzde {cpu:0}{(gpu is null ? "" : $", ekran kartı yüzde {gpu.Utilization}")}). Bir iş başlayıp bitince haber veririm.";
                break;
            case WatchKind.Internet:
                if (await OnlineAsync().ConfigureAwait(false))
                {
                    return "İnternet zaten bağlı.";
                }

                ledger.Record(DataFlow.Outbound, "Nöbet", "İnternet gelene kadar 10 sn'de bir 1.1.1.1 adresine ping atılıyor");
                reply = "Tamam, internet gelince haber veririm.";
                break;
            default:
                var reading = await SystemProbe.GpuAsync(cancellationToken).ConfigureAwait(false);
                if (reading is null)
                {
                    return "Ekran kartının sıcaklığını okuyamıyorum (nvidia-smi bulunamadı).";
                }

                reply = reading.Temperature >= request.Threshold
                    ? $"Ekran kartı şu an zaten {reading.Temperature} derece; düşüp yeniden {request.Threshold:0}'ı geçerse haber veririm."
                    : $"Tamam, ekran kartı şu an {reading.Temperature} derece; {request.Threshold:0} dereceyi geçerse haber veririm.";
                watch.Armed = reading.Temperature < request.Threshold;
                break;
        }

        lock (_gate)
        {
            _active.RemoveAll(existing => existing.Request.Kind == request.Kind);
            _active.Add(watch);
            _timer ??= new Timer(_ => _ = TickAsync(), null, Tick, Tick);
        }

        ledger.Record(DataFlow.Local, "Nöbet", $"{TurkishGrammar.Capitalize(request.Describe())} bekliyor");
        return reply;
    }

    public string List()
    {
        var active = Active;
        return active.Count == 0
            ? "Şu an beklediğim bir şey yok."
            : $"Şunları bekliyorum: {TurkishGrammar.Join([.. active.Select(watch => watch.Describe())])}.";
    }

    public string CancelAll()
    {
        int count;
        lock (_gate)
        {
            count = _active.Count;
            _active.Clear();
            StopIfIdle();
        }

        return count == 0 ? "Zaten beklediğim bir şey yoktu." : "Tamam, nöbeti bıraktım.";
    }

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        try
        {
            List<ActiveWatch> watches;
            lock (_gate)
            {
                watches = [.. _active];
            }

            _tickCount++;
            var now = DateTime.Now;
            var cpuNow = SystemProbe.CpuTimes();
            var cpu = _lastCpu is { } last ? SystemProbe.CpuBusy(last, cpuNow) : 0;
            _lastCpu = cpuNow;
            GpuReading? gpu = null;
            if (watches.Any(watch => watch.Request.Kind is WatchKind.Idle or WatchKind.GpuTemperature))
            {
                gpu = await SystemProbe.GpuAsync(CancellationToken.None).ConfigureAwait(false);
            }

            foreach (var watch in watches)
            {
                var message = now - watch.Started > MaxAge ? "" : await CheckAsync(watch, now, cpu, gpu).ConfigureAwait(false);
                if (message is null)
                {
                    continue;
                }

                lock (_gate)
                {
                    _active.Remove(watch);
                }

                if (message.Length > 0)
                {
                    ledger.Record(DataFlow.Local, "Nöbet", message);
                    Fired?.Invoke(this, message);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            FridayLog.Warn($"Nöbet yoklaması: {exception.Message}");
        }
        finally
        {
            lock (_gate)
            {
                StopIfIdle();
            }

            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    /// <returns>Söylenecek haber; nöbet sürüyorsa null.</returns>
    private async Task<string?> CheckAsync(ActiveWatch watch, DateTime now, double cpu, GpuReading? gpu)
    {
        switch (watch.Request.Kind)
        {
            case WatchKind.Download:
                var folder = Downloads();
                if (!Directory.Exists(folder))
                {
                    return null;
                }

                var files = Directory.GetFiles(folder);
                if (files.Any(DownloadFiles.IsPartial))
                {
                    watch.SawPartial = true;
                    return null;
                }

                // Biten indirmeler: nöbetten sonra ortaya çıkan ya da değişen, yarım olmayan dosyalar.
                var finished = files
                    .Where(file => !watch.Known.Contains(Path.GetFileName(file)) || File.GetLastWriteTime(file) >= watch.Started)
                    .Where(file => !DownloadFiles.IsPartial(file) && !Path.GetFileName(file).StartsWith('.'))
                    .OrderByDescending(File.GetLastWriteTime)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .ToList();
                if (finished.Count == 0)
                {
                    return watch.SawPartial ? "İndirme bitti." : null;
                }

                return finished.Count == 1
                    ? $"İndirme bitti: {finished[0]}."
                    : $"İndirmeler bitti: {TurkishGrammar.Join([.. finished.Take(3)])}{(finished.Count > 3 ? $" ve {finished.Count - 3} dosya daha" : "")}.";
            case WatchKind.Idle:
                var busy = Math.Max(cpu, gpu?.Utilization ?? 0);
                return watch.Idle.Update(busy, now) ? "İş bitmiş görünüyor: işlemci ve ekran kartı boşa çıktı." : null;
            case WatchKind.Internet:
                if (_tickCount % 2 != 0)
                {
                    return null;
                }

                return await OnlineAsync().ConfigureAwait(false) ? "İnternet geri geldi." : null;
            default:
                if (gpu is null)
                {
                    return null;
                }

                if (gpu.Temperature < watch.Request.Threshold - 3)
                {
                    watch.Armed = true;
                    return null;
                }

                return watch.Armed && gpu.Temperature >= watch.Request.Threshold
                    ? string.Create(CultureInfo.InvariantCulture, $"Dikkat: ekran kartı {gpu.Temperature} derece oldu, sınır {watch.Request.Threshold:0}.")
                    : null;
        }
    }

    private void StopIfIdle()
    {
        if (_active.Count == 0)
        {
            _timer?.Dispose();
            _timer = null;
            _lastCpu = null;
        }
    }

    private static async Task<bool> OnlineAsync()
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return false;
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", 1500).ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch (PingException)
        {
            return false;
        }
    }

    /// <summary>Kullanıcının İndirilenler klasörü (başka bir sürücüye taşınmış olabilir).</summary>
    private static string Downloads()
    {
        if (NativeMethods.SHGetKnownFolderPath(DownloadsFolder, 0, IntPtr.Zero, out var pointer) == 0 && pointer != IntPtr.Zero)
        {
            try
            {
                return Marshal.PtrToStringUni(pointer) ?? "";
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _active.Clear();
            StopIfIdle();
        }
    }

    private sealed class ActiveWatch(WatchRequest request, DateTime started)
    {
        public WatchRequest Request { get; } = request;

        public DateTime Started { get; } = started;

        public IdleDetector Idle { get; } = new();

        public HashSet<string> Known { get; set; } = [];

        public bool SawPartial { get; set; }

        /// <summary>Sıcaklık nöbeti: sınırın altına inince yeniden kurulur, aynı uyarı art arda gelmez.</summary>
        public bool Armed { get; set; } = true;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("shell32.dll")]
        internal static partial int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);
    }
}
