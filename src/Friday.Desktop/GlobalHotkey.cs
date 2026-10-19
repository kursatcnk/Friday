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
    private const int Id = 0x4652;
    private const uint NoRepeat = 0x4000;

    private readonly Window _window;
    private readonly Action _pressed;
    private HwndSource? _source;
    private bool _registered;

    public GlobalHotkey(Window window, Action pressed)
    {
        _window = window;
        _pressed = pressed;
    }

    /// <summary>Kısayolu kaydeder; başka bir uygulama aynı kısayolu tutuyorsa false döner.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        var handle = new WindowInteropHelper(_window).EnsureHandle();
        if (_source is null)
        {
            _source = HwndSource.FromHwnd(handle);
            _source.AddHook(WndProc);
        }

        var key = (Key)new KeyConverter().ConvertFromInvariantString(gesture.Key)!;
        var modifiers = (uint)gesture.Modifiers | NoRepeat;
        _registered = NativeMethods.RegisterHotKey(handle, Id, modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == HotkeyMessage && wParam.ToInt32() == Id)
        {
            _pressed();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered && _source is not null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, Id);
        }

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
