using System.Runtime.InteropServices;

namespace Friday.Infrastructure;

/// <summary>Klavye girişi: kısayollar ve Unicode metin (Türkçe karakterler dahil) SendInput ile gönderilir.</summary>
internal static partial class Keys
{
    public const byte Control = 0x11;
    public const byte Alt = 0x12;
    public const byte Shift = 0x10;
    public const byte Left = 0x25;
    public const byte Right = 0x27;
    private const uint Extended = 0x0001;

    /// <summary>Shift, Ctrl, Alt, sol ve sağ Windows tuşu.</summary>
    private static readonly int[] Modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C];

    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;

    public static void Tap(byte key)
    {
        NativeMethods.keybd_event(key, 0, 0, 0);
        NativeMethods.keybd_event(key, 0, KeyUp, 0);
    }

    public static void Chord(byte modifier, byte key)
    {
        NativeMethods.keybd_event(modifier, 0, 0, 0);
        NativeMethods.keybd_event(key, 0, 0, 0);
        NativeMethods.keybd_event(key, 0, KeyUp, 0);
        NativeMethods.keybd_event(modifier, 0, KeyUp, 0);
    }

    /// <summary>Ok tuşları gibi genişletilmiş tuşlar ayrı bayrakla gönderilir (yoksa sayısal tuş takımı sanılabilir).</summary>
    public static void Tap(byte key, bool extended)
    {
        var flag = extended ? Extended : 0;
        NativeMethods.keybd_event(key, 0, flag, 0);
        NativeMethods.keybd_event(key, 0, flag | KeyUp, 0);
    }

    /// <summary>İki değiştirici tuşla bir kısayol (ör. Ctrl+Shift+Sol ok).</summary>
    public static void Chord(byte first, byte second, byte key)
    {
        var flag = key is Left or Right ? Extended : 0;
        NativeMethods.keybd_event(first, 0, 0, 0);
        NativeMethods.keybd_event(second, 0, 0, 0);
        NativeMethods.keybd_event(key, 0, flag, 0);
        NativeMethods.keybd_event(key, 0, flag | KeyUp, 0);
        NativeMethods.keybd_event(second, 0, KeyUp, 0);
        NativeMethods.keybd_event(first, 0, KeyUp, 0);
    }

    /// <summary>Ctrl, Alt, Shift ya da Windows tuşu hâlâ basılı mı?</summary>
    public static bool ModifiersDown() => Modifiers.Any(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0);

    /// <summary>Öndeki pencere bir komut satırı mı (konsol, Windows Terminal)?</summary>
    public static bool ForegroundIsConsole()
    {
        var window = NativeMethods.GetForegroundWindow();
        Span<char> name = stackalloc char[64];
        var length = NativeMethods.GetClassNameW(window, ref name[0], name.Length);
        var className = new string(name[..Math.Max(0, length)]);
        return className is "ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS" or "PseudoConsoleWindow" or "mintty";
    }

    /// <summary>Metni harf harf Unicode olarak yazar; klavye düzeninden bağımsızdır.</summary>
    public static void Text(string text)
    {
        var inputs = new Input[text.Length * 2];
        for (var index = 0; index < text.Length; index++)
        {
            inputs[index * 2] = new Input { Type = 1, Keyboard = new KeyboardInput { Scan = text[index], Flags = Unicode } };
            inputs[(index * 2) + 1] = new Input { Type = 1, Keyboard = new KeyboardInput { Scan = text[index], Flags = Unicode | KeyUp } };
        }

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint Extra;
    }

    /// <summary>INPUT yapısı; birleşim alanı fare girişinin boyutuna (en büyük üye) göre doldurulur.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public KeyboardInput Keyboard;
        private readonly ulong _padding;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

        [LibraryImport("user32.dll")]
        internal static partial uint SendInput(uint count, [In] Input[] inputs, int size);

        [LibraryImport("user32.dll")]
        internal static partial short GetAsyncKeyState(int key);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int GetClassNameW(IntPtr window, ref char name, int capacity);
    }
}
