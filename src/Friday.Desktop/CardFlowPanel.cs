using System.Windows;
using System.Windows.Controls;

namespace Friday.Desktop;

/// <summary>
/// Bugün ekranının kart düzeni: kartlar kullanıcının seçtiği sırayla dizilir, gizlenenler yer kaplamaz. Geniş kartlar
/// tam satır, dar kartlar ikişer yan yana durur; bir satırdaki kartlar aynı yükseklikte olur. Yeni kart eklemek için
/// XAML'de <c>local:CardFlowPanel.Key="..."</c> vermek yeter.
/// </summary>
public sealed class CardFlowPanel : Panel
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(CardFlowPanel), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty WideProperty = DependencyProperty.RegisterAttached(
        "Wide", typeof(bool), typeof(CardFlowPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(CardFlowPanel), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private IReadOnlyList<string> _order = [];

    public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

    public static bool GetWide(DependencyObject element) => (bool)element.GetValue(WideProperty);

    public static void SetWide(DependencyObject element, bool value) => element.SetValue(WideProperty, value);

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Görünecek kartların anahtarları, sırasıyla. Boşsa XAML'deki sıra kullanılır. "intro" her zaman en üsttedir.</summary>
    public IReadOnlyList<string> Order
    {
        get => _order;
        set
        {
            _order = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        var half = Math.Max(0, (width - Gap) / 2);
        var shown = Shown();
        foreach (UIElement child in InternalChildren)
        {
            if (!shown.Contains(child))
            {
                child.Measure(new Size(0, 0));
            }
        }

        var height = 0.0;
        foreach (var row in Rows(shown))
        {
            var rowHeight = 0.0;
            foreach (var child in row)
            {
                child.Measure(new Size(row.Count == 1 && GetWide(child) ? width : half, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            height += rowHeight + Gap;
        }

        return new Size(width, Math.Max(0, height - Gap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var half = Math.Max(0, (finalSize.Width - Gap) / 2);
        var shown = Shown();
        foreach (UIElement child in InternalChildren)
        {
            if (!shown.Contains(child))
            {
                child.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        var top = 0.0;
        foreach (var row in Rows(shown))
        {
            var rowHeight = row.Max(child => child.DesiredSize.Height);
            if (row.Count == 1 && GetWide(row[0]))
            {
                row[0].Arrange(new Rect(0, top, finalSize.Width, rowHeight));
            }
            else
            {
                for (var index = 0; index < row.Count; index++)
                {
                    row[index].Arrange(new Rect(index * (half + Gap), top, half, rowHeight));
                }
            }

            top += rowHeight + Gap;
        }

        return finalSize;
    }

    /// <summary>Gösterilecek kartlar sırasıyla: önce "intro" (görünürse), sonra Order'daki anahtarlar.</summary>
    private List<UIElement> Shown()
    {
        var children = InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed).ToList();
        if (_order.Count == 0)
        {
            return children;
        }

        var shown = children.Where(child => GetKey(child) == "intro").ToList();
        foreach (var key in _order)
        {
            shown.AddRange(children.Where(child => GetKey(child) == key));
        }

        return shown;
    }

    /// <summary>Geniş kart tek başına bir satır; dar kartlar ikişer. Geniş karttan önce tek kalan dar kart yarım satır olur.</summary>
    private static IEnumerable<List<UIElement>> Rows(List<UIElement> shown)
    {
        List<UIElement>? pending = null;
        foreach (var child in shown)
        {
            if (GetWide(child))
            {
                if (pending is not null)
                {
                    yield return pending;
                    pending = null;
                }

                yield return [child];
            }
            else if (pending is null)
            {
                pending = [child];
            }
            else
            {
                pending.Add(child);
                yield return pending;
                pending = null;
            }
        }

        if (pending is not null)
        {
            yield return pending;
        }
    }
}
