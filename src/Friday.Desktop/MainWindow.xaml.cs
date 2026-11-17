using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Friday.Core;

namespace Friday.Desktop;

public sealed partial class MainWindow : Window, IDisposable
{
    private const int DarkTitleBar = 20;

    private readonly GlobalHotkey _hotkey;
    private readonly GlobalHotkey _quickAsk;
    private readonly IReadOnlyList<HotkeyGesture> _candidates;
    private readonly HotkeyGesture? _quickAskGesture;

    /// <param name="candidates">Sırayla denenecek kısayollar; ilki ayarlardaki, diğerleri o doluysa yedekler.</param>
    /// <param name="quickAsk">Hızlı komut kutusunun kısayolu; null ise kapalı.</param>
    public MainWindow(PanelViewModel viewModel, IReadOnlyList<HotkeyGesture> candidates, Action onHotkey, HotkeyGesture? quickAsk, Action onQuickAsk)
    {
        InitializeComponent();
        DataContext = viewModel;
        _candidates = candidates;
        _hotkey = new GlobalHotkey(this, onHotkey);
        _quickAskGesture = quickAsk;
        _quickAsk = new GlobalHotkey(this, onQuickAsk, 0x4653);
    }

    /// <summary>Kaydedilebilen kısayol; hepsi doluysa null. Pencere ilk gösterildiğinde belirlenir.</summary>
    public HotkeyGesture? ActiveHotkey { get; private set; }

    /// <summary>Hızlı komut kutusunun kayıtlı kısayolu; kapalıysa ya da başka uygulamadaysa null.</summary>
    public HotkeyGesture? ActiveQuickAsk => _quickAsk.Current;

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>Konuşma kısayolunu canlı değiştirir; alınamazsa eskisi geri kaydedilir ve false döner.</summary>
    public bool ChangeHotkey(HotkeyGesture gesture)
    {
        var previous = ActiveHotkey;
        if (_hotkey.Register(gesture))
        {
            ActiveHotkey = gesture;
            return true;
        }

        if (previous is not null)
        {
            _hotkey.Register(previous);
        }

        return false;
    }

    /// <summary>Hızlı komut kutusunun kısayolunu değiştirir; null kapatır.</summary>
    public bool ChangeQuickAsk(HotkeyGesture? gesture)
    {
        if (gesture is null)
        {
            _quickAsk.Unregister();
            return true;
        }

        return _quickAsk.Register(gesture);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Koyu temaya uyan koyu başlık çubuğu.
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        NativeMethods.DwmSetWindowAttribute(handle, DarkTitleBar, ref enabled, sizeof(int));

        // Uyarı kutusu açılmaz: kutu açıkken başlatma bekler. Hangi kısayolun alındığı pencerede gösterilir.
        ActiveHotkey = _candidates.FirstOrDefault(_hotkey.Register);
        if (_quickAskGesture is not null && _quickAskGesture != ActiveHotkey)
        {
            _quickAsk.Register(_quickAskGesture);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        _hotkey.Dispose();
        _quickAsk.Dispose();
    }

    private static partial class NativeMethods
    {
        [LibraryImport("dwmapi.dll")]
        internal static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
