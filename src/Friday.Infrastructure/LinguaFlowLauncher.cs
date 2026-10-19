using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Friday.Infrastructure;

/// <summary>
/// LinguaFlow'u açar ve çeviriyi başlatır/durdurur. LinguaFlow'da Ctrl+Enter başlat/durdur kısayoludur; Friday
/// pencereyi öne alıp bu kısayolu gönderir, LinguaFlow'da bir değişiklik gerekmez.
/// </summary>
public static partial class LinguaFlowLauncher
{
    private const string ProcessName = "LinguaFlow.Desktop";

    public static async Task<string> ToggleAsync(string exePath, bool start, CancellationToken cancellationToken)
    {
        var running = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        if (running is null)
        {
            if (!start)
            {
                return "LinguaFlow zaten kapalı.";
            }

            if (!File.Exists(exePath))
            {
                return $"LinguaFlow bulunamadı: {exePath}. settings.json içindeki LinguaFlowPath ayarını kontrol et.";
            }

            running = Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)!
            });
            if (running is null)
            {
                return "LinguaFlow başlatılamadı.";
            }
        }

        // Pencere oluşup modeller hazırlanana kadar bekle.
        for (var attempt = 0; attempt < 40 && running.MainWindowHandle == IntPtr.Zero; attempt++)
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            running.Refresh();
        }

        if (running.MainWindowHandle == IntPtr.Zero)
        {
            return "LinguaFlow açıldı ama penceresi bulunamadı.";
        }

        await Task.Delay(800, cancellationToken).ConfigureAwait(false);
        // Arka plandaki bir süreç pencereyi öne alabilsin diye Alt tuşu kısaca basılır (Windows kuralı).
        NativeMethods.keybd_event(0x12, 0, 0, 0);
        NativeMethods.keybd_event(0x12, 0, 2, 0);
        NativeMethods.ShowWindow(running.MainWindowHandle, 9);
        NativeMethods.SetForegroundWindow(running.MainWindowHandle);
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);

        // Ctrl+Enter başka bir uygulamaya giderse (ör. sohbet uygulaması) mesaj gönderebilir; yalnızca LinguaFlow
        // gerçekten öndeyse gönderilir.
        if (NativeMethods.GetForegroundWindow() != running.MainWindowHandle)
        {
            running.Dispose();
            return "LinguaFlow açıldı ama öne getirilemedi; çeviriyi başlatmak için Başlat'a bas.";
        }

        NativeMethods.keybd_event(0x11, 0, 0, 0);
        NativeMethods.keybd_event(0x0D, 0, 0, 0);
        NativeMethods.keybd_event(0x0D, 0, 2, 0);
        NativeMethods.keybd_event(0x11, 0, 2, 0);
        running.Dispose();
        return start
            ? "LinguaFlow açıldı ve çeviri başlatıldı; altyazılar ekranın altında görünecek."
            : "LinguaFlow'da çeviri durduruldu.";
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetForegroundWindow(IntPtr window);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ShowWindow(IntPtr window, int command);
    }
}
