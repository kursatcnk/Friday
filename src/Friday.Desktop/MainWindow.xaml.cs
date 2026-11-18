using System.Globalization;
using System.IO;
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

    /// <summary>Pencerenin son yeri ve boyutu bu dosyada tutulur; bir sonraki açılışta aynı yerde açılır.</summary>
    public string? BoundsFile { get; init; }

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
        LoadBounds();
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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveBounds();
        base.OnClosing(e);
    }

    /// <summary>Son konum ekranlardan birinin içindeyse oraya açılır; ekran çıkarıldıysa ortada açılır.</summary>
    private void LoadBounds()
    {
        try
        {
            if (BoundsFile is null || !File.Exists(BoundsFile))
            {
                return;
            }

            var parts = File.ReadAllText(BoundsFile).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                return;
            }

            var values = parts.Take(4).Select(part => double.Parse(part, CultureInfo.InvariantCulture)).ToArray();
            var bounds = new Rect(values[0], values[1], Math.Max(MinWidth, values[2]), Math.Max(MinHeight, values[3]));
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var visible = Rect.Intersect(bounds, screen);
            if (visible.IsEmpty || visible.Width < 200 || visible.Height < 120)
            {
                return;
            }

            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top, Width, Height) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            if (parts[4] == "1")
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch (Exception exception) when (exception is IOException or FormatException or UnauthorizedAccessException)
        {
            // Bozuk ya da okunamayan dosya: varsayılan boyutta ortada açılır.
        }
    }

    private void SaveBounds()
    {
        if (BoundsFile is null || RestoreBounds.IsEmpty)
        {
            return;
        }

        try
        {
            var bounds = RestoreBounds;
            File.WriteAllText(BoundsFile, string.Create(CultureInfo.InvariantCulture,
                $"{bounds.Left:0} {bounds.Top:0} {bounds.Width:0} {bounds.Height:0} {(WindowState == WindowState.Maximized ? 1 : 0)}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Yer hatırlanmazsa bir dahaki açılış ortada olur; başka bir şey bozulmaz.
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
