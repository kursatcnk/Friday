using System.Windows;
using System.Windows.Media;
using Friday.Core;
using Microsoft.Win32;

namespace Friday.Desktop;

/// <summary>
/// Görünüm: Windows'un açık/koyu uygulama ayarını ve vurgu rengini izleyen sakin bir tema. Eskiden Friday her zaman
/// neredeyse siyah bir zemin, parlayan bir küre ve turuncu vurguyla açılıyordu; bu, "yapay zekâ uygulaması" kalıbının
/// ta kendisiydi. Açık tema ılık kâğıt tonunda, koyu tema siyah yerine grafittir; vurgu yalnızca seçili öğe ve ana
/// düğmededir. Fırçalar yerinde boyanır, böylece açık pencereler de hemen güncellenir.
/// </summary>
internal static class Theme
{
    /// <summary>Windows'un vurgusu okunamazsa (eski sürüm, kurumsal kilit) kullanılan sakin mavi.</summary>
    private static readonly Color FallbackAccent = Color.FromRgb(0x2F, 0x6F, 0xD6);

    public static void Apply(ResourceDictionary resources, FridaySettings settings)
    {
        var light = settings.Theme switch { "acik" => true, "koyu" => false, _ => SystemUsesLight() };
        var accent = settings.AccentColor == "sistem" || settings.AccentColor.Length == 0
            ? SystemAccent() ?? FallbackAccent
            : ColorConverter.ConvertFromString(settings.AccentColor) is Color chosen ? chosen : FallbackAccent;
        // Açık zeminde çok açık bir vurgu (altın, pembe) okunmaz: biraz koyultulur.
        if (light && Luminance(accent) > 0.55)
        {
            accent = Mix(accent, Colors.Black, 0.28);
        }

        var palette = light ? Light : Dark;
        foreach (var (key, color) in palette)
        {
            Paint(resources, key, color);
        }

        Paint(resources, "AccentBrush", accent);
        Paint(resources, "AccentHoverBrush", Mix(accent, light ? Colors.Black : Colors.White, 0.12));
        Paint(resources, "AccentSoftBrush", WithAlpha(accent, light ? (byte)0x1F : (byte)0x26));
        Paint(resources, "SelectedLineBrush", WithAlpha(accent, 0x99));
        Paint(resources, "AccentInkBrush", Luminance(accent) > 0.6 ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
        Paint(resources, "ListeningBrush", accent);
        resources["AccentColor"] = accent;
        resources["ListeningColor"] = accent;
        IsLight = light;
    }

    /// <summary>Son uygulanan tema açık mı (çizimler buna göre renk seçer).</summary>
    public static bool IsLight { get; private set; }

    private static readonly (string Key, Color Color)[] Dark =
    [
        ("AppBackgroundBrush", Rgb(0x1C1C1F)), ("SidebarBrush", Rgb(0x18181B)), ("SurfaceBrush", Rgb(0x232327)),
        ("SurfaceRaisedBrush", Rgb(0x2A2A2F)), ("SurfaceHoverBrush", Rgb(0x313137)), ("LineBrush", Argb(0x1F, 0xFFFFFF)),
        ("LineStrongBrush", Argb(0x33, 0xFFFFFF)), ("CardBorderBrush", Argb(0x1F, 0xFFFFFF)), ("TextBrush", Rgb(0xECEBE7)),
        ("MutedBrush", Rgb(0xA7A49E)), ("FaintBrush", Rgb(0x7C7973)), ("UserBubbleBrush", Rgb(0x2E2E33)),
        ("DisabledBrush", Rgb(0x2E2E33)), ("SelectedBrush", Rgb(0x36363C)), ("TrackBrush", Rgb(0x3A3A40)),
        ("ScrollThumbBrush", Rgb(0x48484F)), ("OverlayBrush", Argb(0x99, 0x000000)), ("HoverWashBrush", Argb(0x12, 0xFFFFFF)),
        ("WarningBrush", Rgb(0xE9A15A)), ("WarningSoftBrush", Argb(0x1F, 0xE9A15A)), ("WarningLineBrush", Argb(0x40, 0xE9A15A)),
        ("CalmBrush", Rgb(0x6CC79F)), ("DangerBrush", Rgb(0xE5534B)), ("ThinkingBrush", Rgb(0xA7A49E)), ("SpeakingBrush", Rgb(0xECEBE7)),
    ];

    private static readonly (string Key, Color Color)[] Light =
    [
        ("AppBackgroundBrush", Rgb(0xF5F4F1)), ("SidebarBrush", Rgb(0xEDEBE6)), ("SurfaceBrush", Rgb(0xFFFFFF)),
        ("SurfaceRaisedBrush", Rgb(0xF1EFEB)), ("SurfaceHoverBrush", Rgb(0xE8E5DF)), ("LineBrush", Argb(0x1C, 0x1C1B19)),
        ("LineStrongBrush", Argb(0x33, 0x1C1B19)), ("CardBorderBrush", Argb(0x1C, 0x1C1B19)), ("TextBrush", Rgb(0x1E1D1B)),
        ("MutedBrush", Rgb(0x5B5853)), ("FaintBrush", Rgb(0x8A867F)), ("UserBubbleBrush", Rgb(0xEAE7E1)),
        ("DisabledBrush", Rgb(0xE2DFD9)), ("SelectedBrush", Rgb(0xE6E3DD)), ("TrackBrush", Rgb(0xD8D4CD)),
        ("ScrollThumbBrush", Rgb(0xC8C4BC)), ("OverlayBrush", Argb(0x59, 0x000000)), ("HoverWashBrush", Argb(0x0C, 0x000000)),
        ("WarningBrush", Rgb(0xA8580B)), ("WarningSoftBrush", Argb(0x1A, 0xA8580B)), ("WarningLineBrush", Argb(0x40, 0xA8580B)),
        ("CalmBrush", Rgb(0x1D7450)), ("DangerBrush", Rgb(0xC42B1C)), ("ThinkingBrush", Rgb(0x5B5853)), ("SpeakingBrush", Rgb(0x1E1D1B)),
    ];

    /// <summary>Windows "uygulama modu" açık mı (Ayarlar › Kişiselleştirme › Renkler).</summary>
    public static bool SystemUsesLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Windows'un vurgu rengi; okunamazsa null.</summary>
    public static Color? SystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                // DWORD sırası: A B G R.
                return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Sabit mavi kullanılır.
        }

        return null;
    }

    /// <summary>
    /// Fırça yenisiyle değiştirilir: arayüz fırçalara dinamik başvurduğu için açık pencereler de hemen boyanır. Yerinde
    /// boyamak işe yaramıyordu; stillerde kullanılan fırçaları WPF dondurur.
    /// </summary>
    private static void Paint(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    private static double Luminance(Color color) => ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + ((to.R - from.R) * amount)), (byte)(from.G + ((to.G - from.G) * amount)), (byte)(from.B + ((to.B - from.B) * amount)));

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Rgb(int value) => Color.FromRgb((byte)(value >> 16), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));

    private static Color Argb(byte alpha, int value) => Color.FromArgb(alpha, (byte)(value >> 16), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
}
