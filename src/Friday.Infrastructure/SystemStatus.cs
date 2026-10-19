using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Friday.Infrastructure;

/// <summary>"Bilgisayar neden yavaş?" sorusu için CPU, RAM, disk ve GPU özetini çıkarır.</summary>
public static partial class SystemStatus
{
    public static async Task<string> DescribeAsync(CancellationToken cancellationToken)
    {
        // CPU kullanımı iki ölçüm arasındaki işlemci süresinden hesaplanır.
        var first = Snapshot();
        var clock = Stopwatch.StartNew();
        await Task.Delay(700, cancellationToken).ConfigureAwait(false);
        var second = Snapshot();
        var elapsed = clock.Elapsed.TotalMilliseconds * Environment.ProcessorCount;

        var usage = second
            .Where(process => first.ContainsKey(process.Key))
            .Select(process => (process.Value.Name, Cpu: (process.Value.Cpu - first[process.Key].Cpu).TotalMilliseconds / elapsed * 100, process.Value.Memory))
            .ToList();
        var byName = usage
            .GroupBy(process => DisplayName(process.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => (Name: group.Key, Cpu: group.Sum(item => item.Cpu), Memory: group.Sum(item => item.Memory)))
            .ToList();

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        NativeMethods.GlobalMemoryStatusEx(ref memory);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"İşlemci toplam yüzde {Math.Round(Math.Min(100, byName.Sum(item => item.Cpu)))}. ");
        text.Append(CultureInfo.InvariantCulture,
            $"RAM: {Gb(memory.TotalPhys - memory.AvailPhys)} / {Gb(memory.TotalPhys)} GB dolu (yüzde {memory.MemoryLoad}). ");
        text.Append("En çok işlemci kullananlar: ");
        text.AppendJoin(", ", byName.OrderByDescending(item => item.Cpu).Take(4)
            .Select(item => string.Create(CultureInfo.InvariantCulture, $"{item.Name} yüzde {Math.Round(item.Cpu)}")));
        text.Append(". En çok bellek kullananlar: ");
        text.AppendJoin(", ", byName.OrderByDescending(item => item.Memory).Take(4)
            .Select(item => string.Create(CultureInfo.InvariantCulture, $"{item.Name} {Gb((ulong)item.Memory)} GB")));
        text.Append(". Diskler: ");
        text.AppendJoin(", ", DriveInfo.GetDrives().Where(drive => drive is { IsReady: true, DriveType: DriveType.Fixed })
            .Select(drive => string.Create(CultureInfo.InvariantCulture, $"{drive.Name.TrimEnd('\\')} {Gb((ulong)drive.AvailableFreeSpace)} GB boş")));
        text.Append('.');
        if (await GpuAsync(cancellationToken).ConfigureAwait(false) is { } gpu)
        {
            text.Append(' ').Append(gpu);
        }

        return text.ToString();
    }

    /// <summary>Friday'in kendi süreçleri kullanıcıya "kapat" diye önerilmesin diye adlandırılır.</summary>
    private static string DisplayName(string process) => process.ToLowerInvariant() switch
    {
        "llama-server" or "ollama" => "Friday'ın kendi dil modeli (kapatılmamalı)",
        "friday" => "Friday",
        _ => process
    };

    private static Dictionary<int, (string Name, TimeSpan Cpu, long Memory)> Snapshot()
    {
        var result = new Dictionary<int, (string, TimeSpan, long)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    result[process.Id] = (process.ProcessName, process.TotalProcessorTime, process.WorkingSet64);
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Sistem süreçlerinin süresi okunamaz; atlanır.
                }
            }
        }

        return result;
    }

    private static async Task<string?> GpuAsync(CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo("nvidia-smi", "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu,fan.speed,power.draw --format=csv,noheader,nounits")
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
            var parts = output.Split(',', StringSplitOptions.TrimEntries);
            return parts.Length < 7 ? null :
                $"Ekran kartı {parts[0]}: {parts[4]} derece, fan yüzde {parts[5]}, {parts[6]} watt, yüzde {parts[1]} meşgul, {parts[2]} / {parts[3]} MB bellek dolu (Friday'in modelleri dahil). " +
                "İşlemci ve disk sıcaklığını Windows yönetici izni olmadan vermiyor.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string Gb(ulong bytes) => (bytes / 1024.0 / 1024 / 1024).ToString("0.0", CultureInfo.InvariantCulture);

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
        internal static partial bool GlobalMemoryStatusEx(ref MemoryStatus status);
    }
}
