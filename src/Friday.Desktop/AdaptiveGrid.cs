using System.Windows;
using System.Windows.Controls;

namespace Friday.Desktop;

/// <summary>
/// Kartları genişliğe göre sütunlara dizer: sığan sütun sayısını <see cref="MinItemWidth"/>'ten hesaplar, kartları eşit
/// genişliğe yayar ve bir satırdaki kartları aynı yüksekliğe getirir. Ajanda'da sabit genişlikli kartlar pencerenin
/// sağında büyük bir boşluk bırakıyordu.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Bir kartın en dar hâli; sütun sayısı buna göre seçilir.</summary>
    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <summary>Kartlar arasındaki boşluk (yatay ve dikey).</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    private (int Columns, double ItemWidth) Layout(double width)
    {
        if (double.IsInfinity(width) || width <= 0)
        {
            return (1, MinItemWidth);
        }

        var columns = Math.Clamp((int)Math.Floor((width + Gap) / (MinItemWidth + Gap)), 1, Math.Max(1, MaxColumns));
        return (columns, Math.Max(0, (width - (Gap * (columns - 1))) / columns));
    }

    private List<double> _rowHeights = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, itemWidth) = Layout(availableSize.Width);
        var children = InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed).ToList();
        _rowHeights = [];
        for (var start = 0; start < children.Count; start += columns)
        {
            var row = 0.0;
            foreach (var child in children.Skip(start).Take(columns))
            {
                child.Measure(new Size(itemWidth, double.PositiveInfinity));
                row = Math.Max(row, child.DesiredSize.Height);
            }

            _rowHeights.Add(row);
        }

        var height = _rowHeights.Sum() + (Gap * Math.Max(0, _rowHeights.Count - 1));
        var width = double.IsInfinity(availableSize.Width) ? (itemWidth * columns) + (Gap * (columns - 1)) : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Layout(finalSize.Width);
        var children = InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed).ToList();
        var y = 0.0;
        for (var index = 0; index < children.Count; index++)
        {
            var row = index / columns;
            if (index % columns == 0 && row > 0)
            {
                y += _rowHeights[row - 1] + Gap;
            }

            var height = row < _rowHeights.Count ? _rowHeights[row] : children[index].DesiredSize.Height;
            children[index].Arrange(new Rect((index % columns) * (itemWidth + Gap), y, itemWidth, height));
        }

        return finalSize;
    }
}
