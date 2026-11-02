using System.Windows;
using System.Windows.Media;
using Friday.Infrastructure;

namespace Friday.Desktop;

/// <summary>
/// Friday'in "yüzü": merkezde parlayan bir çekirdek ve çevresinde ses seviyesine göre uzayıp kısalan çubuklar.
/// Dinlerken kullanıcının, konuşurken Friday'in sesiyle dalgalanır; düşünürken çevresinde bir ışık döner, boştayken
/// yavaşça nefes alır. Renk duruma göre yumuşakça geçer.
/// </summary>
public sealed class AudioRing : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(AudioRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(AssistantState), typeof(AudioRing), new FrameworkPropertyMetadata(AssistantState.Idle, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Animasyonsuz çizim (anlık görüntü testleri için zaman sabitlenir).</summary>
    public static readonly DependencyProperty FrozenTimeProperty = DependencyProperty.Register(
        nameof(FrozenTime), typeof(double), typeof(AudioRing), new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const int Bars = 64;
    private readonly double[] _bars = new double[Bars];
    private readonly DateTime _start = DateTime.UtcNow;
    private Color _color = Palette(AssistantState.Idle);
    private double _smoothedLevel;
    private bool _animating;
    private TimeSpan _lastFrame;

    private static readonly TimeSpan IdleFrame = TimeSpan.FromMilliseconds(66);

    public AudioRing()
    {
        IsVisibleChanged += (_, _) => UpdateAnimation();
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => StopAnimation();
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
        AssistantState.Listening => Color.FromRgb(0xFF, 0x5C, 0x7A),
        AssistantState.Thinking => Color.FromRgb(0xA7, 0x8B, 0xFA),
        AssistantState.Speaking => Color.FromRgb(0x2E, 0xE6, 0xA6),
        AssistantState.Error => Color.FromRgb(0xFF, 0xB5, 0x47),
        AssistantState.Starting => Color.FromRgb(0x5A, 0x61, 0x72),
        _ => Color.FromRgb(0x38, 0xE1, 0xFF)
    };

    protected override void OnRender(DrawingContext drawingContext)
    {
        var drawing = drawingContext;
        var frozen = FrozenTime >= 0;
        var time = frozen ? FrozenTime : (DateTime.UtcNow - _start).TotalSeconds;
        var target = Palette(State);
        _color = frozen ? target : Lerp(_color, target, 0.12);
        _smoothedLevel = frozen ? Level : (_smoothedLevel * 0.6) + (Level * 0.4);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var core = size * 0.2;
        var ringStart = size * 0.27;
        var maxBar = size * 0.2;

        // Yumuşak dış parıltı: seviye arttıkça büyür.
        var glowRadius = (size * 0.42) + (_smoothedLevel * size * 0.06);
        var glow = new RadialGradientBrush(
            [new GradientStop(WithAlpha(_color, 90), 0), new GradientStop(WithAlpha(_color, 0), 1)]);
        glow.Freeze();
        drawing.DrawEllipse(glow, null, center, glowRadius, glowRadius);

        // Çubuklar.
        for (var index = 0; index < Bars; index++)
        {
            var angle = (index / (double)Bars * Math.PI * 2) - (Math.PI / 2);
            var targetLength = BarTarget(index, time);
            _bars[index] = frozen ? targetLength : (_bars[index] * 0.65) + (targetLength * 0.35);

            var brightness = State == AssistantState.Thinking ? 0.35 + (0.65 * Comet(index, time)) : 0.55 + (0.45 * _bars[index]);
            var pen = new Pen(new SolidColorBrush(WithAlpha(_color, (byte)(255 * brightness))), Math.Max(1.6, size / 60))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            var length = 2 + (_bars[index] * maxBar);
            var from = new Point(center.X + (Math.Cos(angle) * ringStart), center.Y + (Math.Sin(angle) * ringStart));
            var to = new Point(center.X + (Math.Cos(angle) * (ringStart + length)), center.Y + (Math.Sin(angle) * (ringStart + length)));
            drawing.DrawLine(pen, from, to);
        }

        // Çekirdek: parlak merkez, koyu kenar ve ince bir halka.
        var coreBrush = new RadialGradientBrush(
        [
            new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0),
            new GradientStop(_color, 0.35),
            new GradientStop(WithAlpha(Darken(_color, 0.55), 255), 1)
        ])
        { GradientOrigin = new Point(0.4, 0.35) };
        coreBrush.Freeze();
        var breathing = State == AssistantState.Idle ? Math.Sin(time * 1.6) * 0.04 : 0;
        var coreRadius = core * (1 + breathing + (_smoothedLevel * 0.12));
        drawing.DrawEllipse(coreBrush, null, center, coreRadius, coreRadius);
        var rim = new Pen(new SolidColorBrush(WithAlpha(_color, 140)), 1);
        rim.Freeze();
        drawing.DrawEllipse(null, rim, center, core * 1.18, core * 1.18);
    }

    /// <summary>Her çubuğun hedef uzunluğu (0–1).</summary>
    private double BarTarget(int index, double time)
    {
        // Çubuklara kendi "kişiliklerini" veren sabit tohum: hepsi aynı anda inip kalkmaz.
        var seed = Math.Sin(index * 12.9898) * 43758.5453;
        seed -= Math.Floor(seed);
        var wave = 0.5 + (0.5 * Math.Sin((time * (3 + (seed * 4))) + (index * 0.7)));
        return State switch
        {
            AssistantState.Listening or AssistantState.Speaking =>
                Math.Clamp((_smoothedLevel * (0.45 + (0.75 * wave))) + (0.04 * wave), 0, 1),
            AssistantState.Thinking => 0.08 + (0.32 * Comet(index, time)),
            AssistantState.Starting => 0.03,
            _ => 0.05 + (0.05 * wave)
        };
    }

    /// <summary>Düşünürken halkada dönen ışığın bu çubuktaki parlaklığı.</summary>
    private static double Comet(int index, double time)
    {
        var head = (time * 0.9) % 1.0;
        var position = index / (double)Bars;
        var distance = (head - position + 1) % 1.0;
        return Math.Pow(1 - distance, 6);
    }

    private void UpdateAnimation()
    {
        if (IsVisible && FrozenTime < 0 && !_animating)
        {
            CompositionTarget.Rendering += OnFrame;
            _animating = true;
        }
        else if (!IsVisible)
        {
            StopAnimation();
        }
    }

    private void StopAnimation()
    {
        if (_animating)
        {
            CompositionTarget.Rendering -= OnFrame;
            _animating = false;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is { WindowState: WindowState.Minimized })
        {
            return;
        }

        // Boştayken yalnızca yavaş bir nefes alma var: pencere öndeyse saniyede 15 kare, arka plandaysa hiç çizilmez.
        // Friday günün çoğunu boşta geçirir; bu sayede beklerken işlemciye neredeyse hiç dokunmaz.
        if (State is AssistantState.Idle or AssistantState.Starting or AssistantState.Error && e is RenderingEventArgs frame)
        {
            if (window is { IsActive: false } || frame.RenderingTime - _lastFrame < IdleFrame)
            {
                return;
            }

            _lastFrame = frame.RenderingTime;
        }

        InvalidateVisual();
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Darken(Color color, double amount) => Color.FromRgb(
        (byte)(color.R * (1 - amount)), (byte)(color.G * (1 - amount)), (byte)(color.B * (1 - amount)));

    private static Color Lerp(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + ((to.R - from.R) * amount)),
        (byte)(from.G + ((to.G - from.G) * amount)),
        (byte)(from.B + ((to.B - from.B) * amount)));
}
