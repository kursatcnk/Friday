using System.Runtime.InteropServices;

namespace Friday.Infrastructure;

/// <summary>Panodaki metni Win32 API ile okur/yazar; arayüz iş parçacığına ihtiyaç duymaz.</summary>
public static partial class ClipboardText
{
    private const uint UnicodeText = 13;
    private const uint Movable = 0x0002;

    public static string Get()
    {
        if (!OpenWithRetry())
        {
            throw new InvalidOperationException("Pano şu an başka bir uygulama tarafından kullanılıyor.");
        }

        try
        {
            var handle = NativeMethods.GetClipboardData(UnicodeText);
            if (handle == IntPtr.Zero)
            {
                return "";
            }

            var pointer = NativeMethods.GlobalLock(handle);
            try
            {
                return Marshal.PtrToStringUni(pointer) ?? "";
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public static void Set(string text)
    {
        if (!OpenWithRetry())
        {
            throw new InvalidOperationException("Pano şu an başka bir uygulama tarafından kullanılıyor.");
        }

        try
        {
            NativeMethods.EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var memory = NativeMethods.GlobalAlloc(Movable, (nuint)bytes);
            var pointer = NativeMethods.GlobalLock(memory);
            Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            Marshal.WriteInt16(pointer, text.Length * 2, 0);
            NativeMethods.GlobalUnlock(memory);
            // Başarılıysa bellek artık panonundur; serbest bırakılmaz.
            if (NativeMethods.SetClipboardData(UnicodeText, memory) == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(memory);
                throw new InvalidOperationException("Panoya yazılamadı.");
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool OpenWithRetry()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(30);
        }

        return false;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool OpenClipboard(IntPtr owner);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseClipboard();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EmptyClipboard();

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetClipboardData(uint format);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr SetClipboardData(uint format, IntPtr memory);

        [LibraryImport("kernel32.dll")]
        internal static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

        [LibraryImport("kernel32.dll")]
        internal static partial IntPtr GlobalLock(IntPtr memory);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GlobalUnlock(IntPtr memory);

        [LibraryImport("kernel32.dll")]
        internal static partial IntPtr GlobalFree(IntPtr memory);
    }
}
