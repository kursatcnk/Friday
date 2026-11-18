using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Friday.Infrastructure;

public sealed record GpuReading(string Name, int Temperature, int Utilization);

/// <summary>
/// Durum raporu ve nöbetçiler için hafif ölçümler: işlemci yükü (iki an arasındaki sistem süreleri), bellek doluluğu ve
/// ekran kartı (nvidia-smi). Süreç listesi gezilmez; bir ölçüm milisaniyeler sürer.
/// </summary>
public static partial class SystemProbe
{
    /// <summary>İşlemcinin boşta ve toplam süresi (100 ns birimi).</summary>
    public static (ulong Idle, ulong Total) CpuTimes()
    {
        NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user);
        // Çekirdek süresi boşta geçen süreyi de içerir.
        return ((ulong)idle, (ulong)kernel + (ulong)user);
    }

    /// <summary>İki ölçüm arasındaki işlemci yükü (yüzde).</summary>
    public static double CpuBusy((ulong Idle, ulong Total) before, (ulong Idle, ulong Total) after)
    {
        var total = after.Total - before.Total;
        var idle = after.Idle - before.Idle;
        return total == 0 ? 0 : Math.Clamp(100.0 * (total - idle) / total, 0, 100);
    }

    public static async Task<double> CpuBusyAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        var before = CpuTimes();
        await Task.Delay(window, cancellationToken).ConfigureAwait(false);
        return CpuBusy(before, CpuTimes());
    }

    /// <summary>Kullanılan fiziksel belleğin yüzdesi.</summary>
    public static int MemoryLoad()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status) ? (int)status.MemoryLoad : 0;
    }

    /// <summary>NVIDIA ekran kartının sıcaklığı ve kullanımı; kart ya da nvidia-smi yoksa null.</summary>
    public static async Task<GpuReading?> GpuAsync(CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo("nvidia-smi", "--query-gpu=name,temperature.gpu,utilization.gpu --format=csv,noheader,nounits")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = ChildProcessJob.Shared.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return Parse(output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>"NVIDIA GeForce RTX 5070 Ti, 54, 3" ya da yeni sürücülerdeki "…, 39.02, 0" → okuma.</summary>
    public static GpuReading? Parse(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length >= 3
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature)
               && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var utilization)
            ? new GpuReading(parts[0], (int)Math.Round(temperature), (int)Math.Round(utilization))
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetSystemTimes(out long idle, out long kernel, out long user);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GlobalMemoryStatusEx(ref MemoryStatus status);
    }
}
