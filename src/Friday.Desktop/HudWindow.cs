using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Friday.Infrastructure;

namespace Friday.Desktop;

/// <summary>
/// Ekranın altında beliren cam kapsül. Odağı çalmaz, tıklamaları alttaki uygulamaya geçirir ve ekran
/// görüntülerine girmez (Friday ekrana bakarken kendini görmez). İş bitince kendiliğinden kaybolur.
/// </summary>
public sealed partial class HudWindow : Window
{
    private const int ExtendedStyle = -20;
    private const int Transparent = 0x20;
    private const int ToolWindow = 0x80;
    private const int NoActivate = 0x08000000;
    private const uint ExcludeFromCapture = 0x11;

    private readonly TranslateTransform _slide = new();
    private readonly System.Windows.Threading.DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private bool _shown;

    public HudWindow(HudViewModel viewModel)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Friday HUD";
        DataContext = viewModel;
        Content = new HudView { RenderTransform = _slide };
        Opacity = 0;
        _hideTimer.Tick += (_, _) => Disappear();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HudViewModel.State))
            {
                OnStateChanged(viewModel.State);
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongW(handle, ExtendedStyle);
        _ = NativeMethods.SetWindowLongW(handle, ExtendedStyle, style | Transparent | ToolWindow | NoActivate);
        _ = NativeMethods.SetWindowDisplayAffinity(handle, ExcludeFromCapture);
    }

    private void OnStateChanged(AssistantState state)
    {
        if (state is AssistantState.Listening or AssistantState.Thinking or AssistantState.Speaking or AssistantState.Error)
        {
            _hideTimer.Stop();
            Appear();
        }
        else if (_shown)
        {
            // Son cevap bir süre ekranda kalır, sonra kapsül kaybolur.
            _hideTimer.Stop();
            _hideTimer.Start();
        }
    }

    private void Appear()
    {
        if (_shown)
        {
            return;
        }

        _shown = true;
        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Left + ((area.Width - ActualWidth) / 2);
        Top = area.Bottom - ActualHeight - 12;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    private void Disappear()
    {
        _hideTimer.Stop();
        if (!_shown)
        {
            return;
        }

        _shown = false;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (!_shown)
            {
                Hide();
            }
        };
        BeginAnimation(OpacityProperty, fade);
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 18, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial int GetWindowLongW(IntPtr window, int index);

        [LibraryImport("user32.dll")]
        internal static partial int SetWindowLongW(IntPtr window, int index, int value);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    }
}
