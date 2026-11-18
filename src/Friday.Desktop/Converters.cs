using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Friday.Desktop;

/// <summary>true → Collapsed, false → Visible (boş durum görünümleri için).</summary>
public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>0 → Visible, diğer sayılar → Collapsed ("henüz adım yok" gibi boş liste yazıları için).</summary>
public sealed class ZeroVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Boş olmayan yazı görünür, boş yazı gizlenir.</summary>
public sealed class TextVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>[kesir 0–1, kabın genişliği] → çubuğun genişliği.</summary>
public sealed class FractionWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [double fraction, double width] && width > 0 ? Math.Clamp(fraction, 0, 1) * width : 0.0;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
