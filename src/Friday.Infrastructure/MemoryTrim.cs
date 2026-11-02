using System.Runtime;
using System.Runtime.InteropServices;

namespace Friday.Infrastructure;

/// <summary>
/// Modeller uykuya geçince Friday'in boşta tuttuğu belleği işletim sistemine geri verir. Yönetilen yığın sıkıştırılır,
/// sonra çalışma kümesi kırpılır; Görev Yöneticisi'nde görünen bellek bu kadar düşer. Bir sonraki kısayolda ihtiyaç
/// olan sayfalar milisaniyeler içinde geri gelir.
/// </summary>
public static partial class MemoryTrim
{
    public static void Now()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        _ = NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), -1, -1);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll")]
        internal static partial nint GetCurrentProcess();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);
    }
}
