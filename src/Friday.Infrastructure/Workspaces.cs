using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Friday.Infrastructure;

/// <summary>Kaydedilmiş bir pencere: hangi program, ekranın neresinde, ne boyutta, büyütülmüş mü.</summary>
public sealed record WorkspaceWindow(string Path, string Name, int Left, int Top, int Width, int Height, bool Maximized);

/// <summary>
/// Masa düzenleri: "kodlama masasını kaydet" o an açık pencereleri (program ve yerleri) kaydeder; "kodlama masasını
/// kur" eksik programları açar ve hepsini kaydedildikleri yere koyar. Pencere içerikleri ve başlıkları saklanmaz.
/// </summary>
public sealed class WorkspaceStore(string file)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();
    private Dictionary<string, List<WorkspaceWindow>>? _items;

    public IReadOnlyList<string> Names
    {
        get
        {
            lock (_gate)
            {
                return [.. Items.Keys.Order(StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), true))];
            }
        }
    }

    public IReadOnlyList<WorkspaceWindow>? Find(string name)
    {
        lock (_gate)
        {
            var key = Items.Keys.FirstOrDefault(item => AssistantTools.Simplify(item) == AssistantTools.Simplify(name));
            return key is null ? null : Items[key];
        }
    }

    public void Save(string name, IReadOnlyList<WorkspaceWindow> windows)
    {
        lock (_gate)
        {
            var key = Items.Keys.FirstOrDefault(item => AssistantTools.Simplify(item) == AssistantTools.Simplify(name)) ?? name;
            Items[key] = [.. windows];
            Write();
        }
    }

    public bool Remove(string name)
    {
        lock (_gate)
        {
            var key = Items.Keys.FirstOrDefault(item => AssistantTools.Simplify(item) == AssistantTools.Simplify(name));
            if (key is null || !Items.Remove(key))
            {
                return false;
            }

            Write();
            return true;
        }
    }

    private Dictionary<string, List<WorkspaceWindow>> Items => _items ??= Read();

    private Dictionary<string, List<WorkspaceWindow>> Read()
    {
        try
        {
            if (File.Exists(file))
            {
                return JsonSerializer.Deserialize<Dictionary<string, List<WorkspaceWindow>>>(File.ReadAllText(file)) ?? [];
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            FridayLog.Warn($"Masa düzenleri okunamadı: {exception.Message}");
        }

        return [];
    }

    private void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(Items, Json));
    }
}

public static partial class Desktop
{
    /// <summary>Program yolu bilinmeyen ya da yeniden açılamayan pencereler (Windows kabuğu, mağaza uygulaması çerçevesi).</summary>
    private static readonly HashSet<string> NotRestorable = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost", "SearchHost", "LockApp"
    };

    /// <summary>O an açık uygulama pencerelerinin yeri ve boyutu (Friday ve Windows kabuğu hariç).</summary>
    public static IReadOnlyList<WorkspaceWindow> CaptureLayout()
    {
        var windows = new List<WorkspaceWindow>();
        foreach (var window in Windows())
        {
            if (NotRestorable.Contains(window.Process) || ProcessPath(window.ProcessId) is not { } path)
            {
                continue;
            }

            var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (!NativeMethods.GetWindowPlacement(window.Handle, ref placement))
            {
                continue;
            }

            var bounds = placement.NormalPosition;
            windows.Add(new WorkspaceWindow(path, FriendlyName(path, window.Process), bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, placement.ShowCommand == ShowMaximized));
        }

        // Üstteki pencere en son yerleştirilsin ki kurulunca da üstte kalsın.
        windows.Reverse();
        return windows;
    }

    /// <summary>Eksik programları açar, pencereleri kaydedildikleri yere koyar; yerleştirilen pencere sayısını döndürür.</summary>
    public static async Task<int> RestoreLayoutAsync(IReadOnlyList<WorkspaceWindow> layout, CancellationToken cancellationToken)
    {
        var used = new HashSet<IntPtr>();
        var placed = 0;
        foreach (var saved in layout)
        {
            var handle = FindUnused(saved.Path, used);
            if (handle == IntPtr.Zero && File.Exists(saved.Path))
            {
                using var started = Process.Start(new ProcessStartInfo(saved.Path) { UseShellExecute = true });
                for (var attempt = 0; attempt < 40 && handle == IntPtr.Zero; attempt++)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    handle = FindUnused(saved.Path, used);
                }
            }

            if (handle == IntPtr.Zero)
            {
                continue;
            }

            used.Add(handle);
            var placement = new WindowPlacement
            {
                Length = Marshal.SizeOf<WindowPlacement>(),
                ShowCommand = saved.Maximized ? ShowMaximized : ShowNormal,
                NormalPosition = new NativeRect(saved.Left, saved.Top, saved.Left + saved.Width, saved.Top + saved.Height)
            };
            if (NativeMethods.SetWindowPlacement(handle, ref placement))
            {
                placed++;
            }
        }

        return placed;
    }

    private const int ShowNormal = 1;
    private const int ShowMaximized = 3;

    private static IntPtr FindUnused(string path, HashSet<IntPtr> used)
    {
        foreach (var window in Windows())
        {
            if (!used.Contains(window.Handle) && string.Equals(ProcessPath(window.ProcessId), path, StringComparison.OrdinalIgnoreCase))
            {
                return window.Handle;
            }
        }

        return IntPtr.Zero;
    }

    private static string? ProcessPath(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Yönetici olarak çalışan ya da kapanmış süreç: yolu okunamaz, düzene alınmaz.
            return null;
        }
    }

    private static string FriendlyName(string path, string fallback)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? fallback : description.Trim();
        }
        catch (FileNotFoundException)
        {
            return fallback;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private record struct NativeRect(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public NativeRect NormalPosition;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowPlacement(IntPtr handle, ref WindowPlacement placement);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPlacement(IntPtr handle, ref WindowPlacement placement);
    }
}
