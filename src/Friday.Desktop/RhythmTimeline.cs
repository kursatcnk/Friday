using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Friday.Core;

namespace Friday.Desktop;

/// <summary>
/// Günün zaman çizelgesi: ince bir şerit üzerinde çalışma (grafit) ve odak (kor) aralıkları; aradaki boşluklar molalar.
/// Şerit günün ilk etkinliğinden (en geç 08:00) gecenin 24:00'üne kadar uzanır, saat etiketleri altında durur.
/// </summary>
public sealed class RhythmTimeline : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<RhythmSegment>), typeof(RhythmTimeline),
        new FrameworkPropertyMetadata(Array.Empty<RhythmSegment>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NowProperty = DependencyProperty.Register(
        nameof(Now), typeof(DateTime), typeof(RhythmTimeline), new FrameworkPropertyMetadata(DateTime.Now, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface Labels = new("Segoe UI Variable Small");

    public IReadOnlyList<RhythmSegment> Segments
    {
        get => (IReadOnlyList<RhythmSegment>)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public DateTime Now
    {
        get => (DateTime)GetValue(NowProperty);
        set => SetValue(NowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 46);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var day = Now.Date;
        var firstHour = Segments.Count > 0 ? Math.Min(8, Segments.Min(segment => segment.Start).Hour) : 8;
        var start = day.AddHours(firstHour);
        var end = day.AddHours(24);
        double X(DateTime time) => Math.Clamp((time - start).TotalMinutes / (end - start).TotalMinutes, 0, 1) * width;

        const double top = 6;
        const double height = 14;
        var track = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
        track.Freeze();
        drawingContext.DrawRoundedRectangle(track, null, new Rect(0, top, width, height), 7, 7);

        var work = new SolidColorBrush(Color.FromRgb(0x6E, 0x6A, 0x62));
        var focus = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x5B));
        work.Freeze();
        focus.Freeze();
        foreach (var segment in Segments)
        {
            var left = X(segment.Start);
            var right = Math.Max(left + 2, X(segment.End));
            drawingContext.DrawRoundedRectangle(segment.Kind == RhythmSegmentKind.Focus ? focus : work, null,
                new Rect(left, top + 2, right - left, height - 4), 5, 5);
        }

        // Şu an: ince beyaz bir çizgi.
        var nowPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0xF2, 0xF1, 0xEE)), 1.5);
        nowPen.Freeze();
        var nowX = X(Now);
        drawingContext.DrawLine(nowPen, new Point(nowX, top - 3), new Point(nowX, top + height + 3));

        var label = new SolidColorBrush(Color.FromRgb(0x6E, 0x6C, 0x67));
        label.Freeze();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var hour = firstHour; hour <= 24; hour += hour < 12 ? 2 : 3)
        {
            var text = new FormattedText(hour == 24 ? "24" : hour.ToString("00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Labels, 10.5, label, dpi);
            var x = Math.Clamp(X(day.AddHours(hour)) - (text.Width / 2), 0, width - text.Width);
            drawingContext.DrawText(text, new Point(x, top + height + 8));
        }
    }
}
