using System.Runtime.InteropServices;

namespace Friday.Infrastructure;

/// <summary>Klavye girişi: kısayollar ve Unicode metin (Türkçe karakterler dahil) SendInput ile gönderilir.</summary>
internal static partial class Keys
{
    public const byte Control = 0x11;
    public const byte Alt = 0x12;

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
    }
}
