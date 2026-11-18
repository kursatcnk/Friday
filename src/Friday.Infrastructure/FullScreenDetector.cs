using System.Runtime.InteropServices;

namespace Friday.Infrastructure;

/// <summary>
/// Oyun, tam ekran video ya da sunum açık mı? Windows'un bildirim durumunu sorar (bildirimleri susturan aynı bilgi);
/// pencereler taranmaz, ekran okunmaz. Bir çağrı mikrosaniyeler sürer.
/// </summary>
public static partial class FullScreenDetector
{
    // QUERY_USER_NOTIFICATION_STATE: 2 tam ekran uygulama, 3 Direct3D tam ekran (oyun), 4 sunum modu.
    private const int Busy = 2;
    private const int Direct3DFullScreen = 3;
    private const int PresentationMode = 4;

    public static bool IsBusy() =>
        NativeMethods.SHQueryUserNotificationState(out var state) == 0 && state is Busy or Direct3DFullScreen or PresentationMode;

    private static partial class NativeMethods
    {
        [LibraryImport("shell32.dll")]
        internal static partial int SHQueryUserNotificationState(out int state);
    }
}
