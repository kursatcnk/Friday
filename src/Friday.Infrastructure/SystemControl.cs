using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Friday.Core;
using Microsoft.Win32;

namespace Friday.Infrastructure;

/// <summary>
/// Sesle istenen dar bilgisayar işleri: kilitle, uyku, ekranı kapat, masaüstünü göster, koyu/açık tema, ekran görüntüsü,
/// zamanlı kapatma ve iptali. Genel komut çalıştırma yoktur; her iş tek ve belirli bir Windows çağrısıdır. Kapatma en az
/// bir dakika sonraya kurulur ve "kapatmayı iptal et" ile geri alınır.
/// </summary>
internal static partial class SystemControl
{
    private const int NoShutdownPending = 1116;

    /// <summary>Kurulu zamanlı kapanma; yoksa null.</summary>
    public static DateTime? ShutdownAt { get; private set; }

    /// <summary>Cümle okunurken iş yapılmasın diye kısa bir gecikmeyle çalıştırır.</summary>
    private static void Later(TimeSpan delay, Action action) =>
        _ = Task.Delay(delay).ContinueWith(_ => action(), TaskScheduler.Default);

    public static string Lock()
    {
        Later(TimeSpan.FromSeconds(1.2), () => NativeMethods.LockWorkStation());
        return "Bilgisayarı kilitliyorum.";
    }

    public static string Sleep()
    {
        Later(TimeSpan.FromSeconds(3), () => NativeMethods.SetSuspendState(false, false, false));
        return "Uyku moduna alıyorum. İyi dinlenmeler.";
    }

    public static string MonitorOff()
    {
        // Fare ya da klavye ekranı yeniden açar.
        Later(TimeSpan.FromSeconds(2), () => NativeMethods.PostMessageW(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2)));
        return "Ekranı kapatıyorum; fareyi oynatınca geri gelir.";
    }

    public static string ShowDesktop()
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null || Activator.CreateInstance(type) is not { } shell)
        {
            return "Masaüstünü gösteremedim.";
        }

        try
        {
            type.InvokeMember("MinimizeAll", BindingFlags.InvokeMethod, null, shell, null, CultureInfo.InvariantCulture);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return "Pencereleri küçülttüm.";
    }

    /// <summary>Windows'un ve uygulamaların koyu ya da açık temasını değiştirir (kullanıcı ayarı, yönetici izni gerekmez).</summary>
    public static string SetTheme(bool dark)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: true))
        {
            key.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        }

        // Açık pencereler temayı hemen alsın.
        NativeMethods.SendMessageTimeoutW(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x0002, 200, out _);
        return dark ? "Koyu temaya geçtim." : "Açık temaya geçtim.";
    }

    /// <summary>Bütün ekranların tam çözünürlüklü görüntüsünü PNG olarak kaydeder.</summary>
    /// <returns>Kaydedilen dosya.</returns>
    public static string Screenshot(string folder)
    {
        var left = NativeMethods.GetSystemMetrics(76);
        var top = NativeMethods.GetSystemMetrics(77);
        var width = NativeMethods.GetSystemMetrics(78);
        var height = NativeMethods.GetSystemMetrics(79);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"ekran-{DateTime.Now:yyyy-MM-dd-HHmmss}.png");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height));
        }

        bitmap.Save(file, ImageFormat.Png);
        return file;
    }

    public static string ScheduleShutdown(int minutes)
    {
        minutes = Math.Clamp(minutes, 1, 720);
        var result = RunShutdown($"/s /t {minutes * 60} /c \"Friday: bilgisayar {minutes} dakika sonra kapanacak. İptal için Friday'e 'kapatmayı iptal et' de.\"");
        if (result != 0)
        {
            return "Kapatmayı kuramadım; zaten kurulu bir kapanma olabilir. Önce \"kapatmayı iptal et\" de.";
        }

        ShutdownAt = DateTime.Now.AddMinutes(minutes);
        var at = ShutdownAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        return minutes == 1
            ? "Bilgisayar 1 dakika sonra kapanacak. Açık işlerini kaydet; vazgeçersen \"kapatmayı iptal et\" de."
            : $"Tamam, bilgisayar {TurkishGrammar.Duration(minutes)} sonra, {at}'{RhythmText.TimeSuffix(ShutdownAt.Value)} kapanacak. Vazgeçersen \"kapatmayı iptal et\" de.";
    }

    public static string CancelShutdown()
    {
        var result = RunShutdown("/a");
        ShutdownAt = null;
        return result == NoShutdownPending ? "Kurulu bir kapanma yoktu." : "Kapatmayı iptal ettim.";
    }

    private static int RunShutdown(string arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(start);
        if (process is null)
        {
            return -1;
        }

        process.WaitForExit(5000);
        return process.HasExited ? process.ExitCode : -1;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool LockWorkStation();

        [LibraryImport("powrprof.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static partial bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force,
            [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        [LibraryImport("user32.dll")]
        internal static partial int GetSystemMetrics(int index);
    }
}
