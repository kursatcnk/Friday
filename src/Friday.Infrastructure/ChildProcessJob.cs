using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Friday.Infrastructure;

/// <summary>
/// Friday'in başlattığı sunucuları (Ollama, Piper, XTTS) bir Windows iş nesnesine (job object) bağlar. İşin
/// tutamacı yalnızca Friday'de durur; Friday nasıl kapanırsa kapansın (çıkış, çökme, Görev Yöneticisi'nden
/// sonlandırma) Windows tutamacı kapatır ve işteki bütün süreçleri öldürür. Sonradan açılan alt süreçler,
/// örneğin Ollama'nın model yüklerken açtığı llama-server, işe kendiliğinden girer.
/// Kullanıcı için açılan uygulamalar (Not Defteri, tarayıcı, LinguaFlow) işe eklenmez; Friday kapanınca açık kalır.
/// </summary>
public sealed partial class ChildProcessJob : IDisposable
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private readonly SafeJobHandle _handle;

    /// <summary>Uygulama boyunca yaşar ve kapatılmaz; tutamacı Friday'in süreci bitince Windows kapatır.</summary>
    public static ChildProcessJob Shared { get; } = new();

    public ChildProcessJob()
    {
        _handle = NativeMethods.CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            throw new InvalidOperationException($"İş nesnesi oluşturulamadı (hata {Marshal.GetLastPInvokeError()}).");
        }

        var limits = new ExtendedLimit { BasicLimit = new BasicLimit { LimitFlags = KillOnJobClose } };
        if (!NativeMethods.SetInformationJobObject(_handle, ExtendedLimitInformation, ref limits, Marshal.SizeOf<ExtendedLimit>()))
        {
            var error = Marshal.GetLastPInvokeError();
            _handle.Dispose();
            throw new InvalidOperationException($"İş nesnesi ayarlanamadı (hata {error}).");
        }
    }

    /// <summary>Süreci işe ekler. Süreç zaten kapanmışsa false döner; bu durumda kapatılacak bir şey kalmamıştır.</summary>
    public bool TryAdd(Process process) =>
        !process.HasExited && NativeMethods.AssignProcessToJobObject(_handle, process.SafeHandle);

    /// <summary>İşi kapatır; içindeki bütün süreçler hemen sonlanır.</summary>
    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit BasicLimit;
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial SafeJobHandle CreateJobObjectW(IntPtr attributes, IntPtr name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetInformationJobObject(SafeJobHandle job, int infoClass, ref ExtendedLimit info, int length);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr handle);
    }
}
