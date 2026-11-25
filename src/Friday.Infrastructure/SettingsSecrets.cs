using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Friday.Infrastructure;

/// <summary>
/// Ayar dosyasındaki sırlar (telefondan erişim anahtarı, takvimin gizli iCal adresi, Azure anahtarı) diske Windows'un
/// kullanıcıya bağlı şifrelemesiyle (DPAPI) yazılır: dosya başka bir kullanıcıya, başka bir bilgisayara ya da bir yedeğe
/// kopyalansa da okunamaz. Bellekteki ayar nesnesi değişmez; yalnızca dosyadaki değerler "dpapi:" ile başlar. Eski düz
/// değerler okunur ve bir sonraki kayıtta şifrelenir.
/// </summary>
internal static partial class SettingsSecrets
{
    private const string Prefix = "dpapi:";

    private static readonly string[] Names = ["RemoteKey", "CalendarUrl", "AzureSpeechKey"];

    /// <summary>Kaydedilecek JSON'daki sırları şifreler.</summary>
    public static void Protect(JsonObject settings)
    {
        foreach (var name in Names)
        {
            if (settings[name]?.GetValue<string>() is { Length: > 0 } value && !value.StartsWith(Prefix, StringComparison.Ordinal))
            {
                settings[name] = Prefix + Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), encrypt: true));
            }
        }
    }

    /// <summary>Okunan JSON'daki şifreli sırları çözer; çözülemiyorsa (başka kullanıcı ya da bilgisayar) boş bırakır.</summary>
    public static void Unprotect(JsonObject settings)
    {
        foreach (var name in Names)
        {
            if (settings[name]?.GetValue<string>() is { } value && value.StartsWith(Prefix, StringComparison.Ordinal))
            {
                try
                {
                    settings[name] = Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value[Prefix.Length..]), encrypt: false));
                }
                catch (Exception exception) when (exception is FormatException or InvalidOperationException)
                {
                    FridayLog.Warn($"Ayarlardaki {name} çözülemedi (başka bir kullanıcının ya da bilgisayarın dosyası); boş bırakıldı");
                    settings[name] = "";
                }
            }
        }
    }

    private static byte[] Transform(byte[] data, bool encrypt)
    {
        var input = new NativeMethods.Blob { Size = data.Length, Data = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
        var output = default(NativeMethods.Blob);
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var done = encrypt
                ? NativeMethods.CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NativeMethods.UiForbidden, ref output)
                : NativeMethods.CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NativeMethods.UiForbidden, ref output);
            if (!done)
            {
                throw new InvalidOperationException($"DPAPI hatası {Marshal.GetLastPInvokeError()}");
            }

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                NativeMethods.LocalFree(output.Data);
            }
        }
    }

    private static partial class NativeMethods
    {
        public const int UiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        public struct Blob
        {
            public int Size;
            public IntPtr Data;
        }

        [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);

        [LibraryImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);

        [LibraryImport("kernel32.dll")]
        public static partial IntPtr LocalFree(IntPtr memory);
    }
}
