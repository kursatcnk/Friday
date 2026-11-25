using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Friday.Infrastructure;

namespace Friday.Desktop;

/// <summary>
/// Friday'in "yüzü": cam gibi parlayan bir çekirdek ve çevresinde sesle akışkan biçimde dalgalanan üç ince çizgi.
/// Dinlerken kullanıcının, konuşurken Friday'in sesiyle büyür; düşünürken çizgiler hızlanıp çekirdeğin etrafında
/// döner, boştayken yavaşça nefes alır. Renk duruma göre yumuşakça geçer.
/// </summary>
public sealed class AudioRing : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(AudioRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(AssistantState), typeof(AudioRing), new FrameworkPropertyMetadata(
            AssistantState.Idle, FrameworkPropertyMetadataOptions.AffectsRender, (ring, _) => ((AudioRing)ring).UpdateAnimation()));

    /// <summary>Animasyonsuz çizim (anlık görüntü testleri için zaman sabitlenir).</summary>
    public static readonly DependencyProperty FrozenTimeProperty = DependencyProperty.Register(
        nameof(FrozenTime), typeof(double), typeof(AudioRing), new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const int Points = 96;
    private static readonly TimeSpan IdleFrame = TimeSpan.FromMilliseconds(66);

    private readonly DateTime _start = DateTime.UtcNow;
    private Color _color = Palette(AssistantState.Idle);
    private double _smoothedLevel;
    private double _energy;
    private readonly DispatcherTimer _idleTimer;
    private bool _animating;
    private Window? _window;

    public AudioRing()
    {
        // Boştaki nefes için seyrek bir zamanlayıcı: CompositionTarget.Rendering'e bağlı kalmak, kare çizilmese de WPF'in
        // çizim döngüsünü ekranın yenileme hızında (ör. 144 Hz) uyanık tutar.
        _idleTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = IdleFrame };
        _idleTimer.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) => UpdateAnimation();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public AssistantState State
    {
        get => (AssistantState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public double FrozenTime
    {
        get => (double)GetValue(FrozenTimeProperty);
        set => SetValue(FrozenTimeProperty, value);
    }

    public static Color Palette(AssistantState state) => state switch
    {
        AssistantState.Listening => Color.FromRgb(0xFF, 0x8A, 0x5B),
        AssistantState.Thinking => Color.FromRgb(0xB9, 0xA8, 0xFF),
        AssistantState.Speaking => Color.FromRgb(0xF2, 0xCC, 0x8F),
        AssistantState.Error => Color.FromRgb(0xFF, 0x6B, 0x6B),
        AssistantState.Starting => Color.FromRgb(0x5E, 0x5C, 0x57),
        _ => Color.FromRgb(0xC9, 0xC1, 0xB1)
    };

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var frozen = FrozenTime >= 0;
        var time = frozen ? FrozenTime : (DateTime.UtcNow - _start).TotalSeconds;
        var target = Palette(State);
        _color = frozen ? target : Lerp(_color, target, 0.1);
        _smoothedLevel = frozen ? Level : (_smoothedLevel * 0.55) + (Level * 0.45);
        // Durumun "enerjisi": boşta sakin, dinlerken ve konuşurken sese göre, düşünürken sabit bir canlılık.
        var wanted = State switch
        {
            AssistantState.Listening or AssistantState.Speaking => 0.25 + (_smoothedLevel * 1.4),
            AssistantState.Thinking => 0.55,
            AssistantState.Starting => 0.05,
            _ => 0.12
        };
        _energy = frozen ? wanted : (_energy * 0.85) + (wanted * 0.15);

        // Sade bir ses göstergesi: beş ince, yuvarlak uçlu çubuk. Boşta neredeyse durgun ve soluk, dinlerken ve konuşurken
        // sesin gücüyle, düşünürken sırayla yavaşça dalgalanır. Eskiden burada parlayan bir küre ve hâle vardı; "yapay
        // zekâ uygulaması" görüntüsünün en belirgin parçasıydı.
        var color = State switch
        {
            AssistantState.Listening => Brush("AccentBrush", _color),
            AssistantState.Speaking => Brush("TextBrush", _color),
            AssistantState.Thinking => Brush("MutedBrush", _color),
            AssistantState.Error => Brush("DangerBrush", _color),
            _ => Brush("FaintBrush", _color)
        };
        const int bars = 5;
        var width = Math.Max(2, size * 0.075);
        var gap = width * 1.15;
        var total = (bars * width) + ((bars - 1) * gap);
        var left = (ActualWidth - total) / 2;
        var middle = ActualHeight / 2;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        for (var bar = 0; bar < bars; bar++)
        {
            // Ortadaki çubuk en uzun; dalga her çubukta biraz gecikir.
            var shape = 1 - (Math.Abs(bar - ((bars - 1) / 2.0)) * 0.18);
            var wave = State == AssistantState.Thinking
                ? 0.5 + (0.5 * Math.Sin((time * 4.2) - (bar * 0.9)))
                : 0.6 + (0.4 * Math.Sin((time * 7.5) + (bar * 1.7)));
            var height = size * Math.Clamp(0.12 + (_energy * 0.62 * shape * wave), 0.1, 0.78);
            var rect = new Rect(left + (bar * (width + gap)), middle - (height / 2), width, height);
            drawingContext.DrawRoundedRectangle(brush, null, rect, width / 2, width / 2);
        }
    }

    /// <summary>Temadaki fırçanın rengi; bulunamazsa verilen renk.</summary>
    private Color Brush(string key, Color fallback) => TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;

    /// <summary>Kapalı, yumuşak bir eğri: yarıçapı açıya göre birkaç sinüsle dalgalanır.</summary>
    private static StreamGeometry Blob(Point center, double radius, double amplitude, double phase, int layer)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index <= Points; index++)
            {
                var angle = index / (double)Points * Math.PI * 2;
                var wave = (0.55 * Math.Sin((2 * angle) + phase))
                           + (0.3 * Math.Sin((3 * angle) - (phase * 1.3) + layer))
                           + (0.15 * Math.Sin((5 * angle) + (phase * 0.7) - layer));
                var r = radius + (amplitude * wave);
                var point = new Point(center.X + (Math.Cos(angle) * r), center.Y + (Math.Sin(angle) * r));
                if (index == 0)
                {
                    context.BeginFigure(point, isFilled: false, isClosed: true);
                }
                else
                {
                    context.LineTo(point, isStroked: true, isSmoothJoin: true);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private void Attach()
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.Activated += OnWindowChanged;
            _window.Deactivated += OnWindowChanged;
            _window.StateChanged += OnWindowChanged;
        }

        UpdateAnimation();
    }

    private void Detach()
    {
        if (_window is not null)
        {
            _window.Activated -= OnWindowChanged;
            _window.Deactivated -= OnWindowChanged;
            _window.StateChanged -= OnWindowChanged;
            _window = null;
        }

        SetMode(animate: false, breathe: false);
    }

    private void OnWindowChanged(object? sender, EventArgs e) => UpdateAnimation();

    /// <summary>
    /// Dinlerken, düşünürken ve konuşurken her karede çizilir. Boştayken yalnızca yavaş bir nefes var: pencere öndeyse
    /// saniyede 15 kare, arka plandaysa ya da simge durumundaysa hiç çizilmez. Friday günün çoğunu boşta geçirir; bu
    /// sayede beklerken işlemciye ve ekran kartına neredeyse hiç dokunmaz.
    /// </summary>
    private void UpdateAnimation()
    {
        var shown = IsVisible && FrozenTime < 0 && _window is not { WindowState: WindowState.Minimized };
        var idle = State is AssistantState.Idle or AssistantState.Starting or AssistantState.Error;
        SetMode(animate: shown && !idle, breathe: shown && idle && _window is not { IsActive: false });
    }

    private void SetMode(bool animate, bool breathe)
    {
        if (animate != _animating)
        {
            if (animate)
            {
                CompositionTarget.Rendering += OnFrame;
            }
            else
            {
                CompositionTarget.Rendering -= OnFrame;
            }

            _animating = animate;
        }

        if (breathe != _idleTimer.IsEnabled)
        {
            _idleTimer.IsEnabled = breathe;
        }

        if (!animate && !breathe)
        {
            InvalidateVisual();
        }
    }

    private void OnFrame(object? sender, EventArgs e) => InvalidateVisual();

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Darken(Color color, double amount) => Color.FromRgb(
        (byte)(color.R * (1 - amount)), (byte)(color.G * (1 - amount)), (byte)(color.B * (1 - amount)));

    private static Color Lerp(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + ((to.R - from.R) * amount)),
        (byte)(from.G + ((to.G - from.G) * amount)),
        (byte)(from.B + ((to.B - from.B) * amount)));
}
