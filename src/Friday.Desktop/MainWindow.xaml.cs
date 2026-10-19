using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Friday.Core;

namespace Friday.Desktop;

public sealed partial class MainWindow : Window, IDisposable
{
    private const int DarkTitleBar = 20;

    private readonly GlobalHotkey _hotkey;
    private readonly IReadOnlyList<HotkeyGesture> _candidates;
    private bool _exiting;

    /// <param name="candidates">Sırayla denenecek kısayollar; ilki ayarlardaki, diğerleri o doluysa yedekler.</param>
    public MainWindow(PanelViewModel viewModel, IReadOnlyList<HotkeyGesture> candidates, Action onHotkey)
    {
        InitializeComponent();
        DataContext = viewModel;
        _candidates = candidates;
        _hotkey = new GlobalHotkey(this, onHotkey);
    }

    /// <summary>Kaydedilebilen kısayol; hepsi doluysa null. Pencere ilk gösterildiğinde belirlenir.</summary>
    public HotkeyGesture? ActiveHotkey { get; private set; }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>Uygulamadan çıkarken pencerenin tepsiye saklanmak yerine gerçekten kapanmasını sağlar.</summary>
    public void PrepareExit() => _exiting = true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Koyu temaya uyan koyu başlık çubuğu.
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        NativeMethods.DwmSetWindowAttribute(handle, DarkTitleBar, ref enabled, sizeof(int));

        // Uyarı kutusu açılmaz: kutu açıkken başlatma bekler. Hangi kısayolun alındığı pencerede gösterilir.
        ActiveHotkey = _candidates.FirstOrDefault(_hotkey.Register);
    }

    /// <summary>Pencereyi kapatmak Friday'i kapatmaz; tepsiye küçülür. Çıkış tepsi menüsündedir.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose() => _hotkey.Dispose();

    private static partial class NativeMethods
    {
        [LibraryImport("dwmapi.dll")]
        internal static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
