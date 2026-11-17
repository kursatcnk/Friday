using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Friday.Core;

namespace Friday.Desktop;

/// <summary>
/// Windows'un RegisterHotKey API'siyle sistem genelinde kısayol. Bu yalnızca tuş vuruşunu bildirir;
/// klavye dinlenmez, başka tuşlar okunmaz.
/// </summary>
public sealed partial class GlobalHotkey : IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const uint NoRepeat = 0x4000;

    private readonly Window _window;
    private readonly int _id;
    private readonly Action _pressed;
    private HwndSource? _source;
    private bool _registered;

    /// <param name="id">Aynı pencerede birden çok kısayol için ayrı kimlik.</param>
    public GlobalHotkey(Window window, Action pressed, int id = 0x4652)
    {
        _window = window;
        _pressed = pressed;
        _id = id;
    }

    /// <summary>Şu an kayıtlı kısayol; yoksa null.</summary>
    public HotkeyGesture? Current { get; private set; }

    /// <summary>Kısayolu kaydeder (öncekini bırakır); başka bir uygulama aynı kısayolu tutuyorsa false döner.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        var handle = new WindowInteropHelper(_window).EnsureHandle();
        if (_source is null)
        {
            _source = HwndSource.FromHwnd(handle);
            _source.AddHook(WndProc);
        }

        Unregister();
        Key key;
        try
        {
            key = (Key)new KeyConverter().ConvertFromInvariantString(gesture.Key)!;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or FormatException)
        {
            return false;
        }

        var modifiers = (uint)gesture.Modifiers | NoRepeat;
        _registered = NativeMethods.RegisterHotKey(handle, _id, modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        Current = _registered ? gesture : null;
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _source is not null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, _id);
        }

        _registered = false;
        Current = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == HotkeyMessage && wParam.ToInt32() == _id)
        {
            _pressed();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
