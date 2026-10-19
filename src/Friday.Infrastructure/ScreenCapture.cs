using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Friday.Infrastructure;

public sealed record CapturedScreen(byte[] Jpeg, string Title, int Width, int Height);

/// <summary>
/// Yalnızca istendiğinde aktif pencerenin ya da ana ekranın görüntüsünü alır. Görüntü bellekte JPEG olarak tutulur,
/// diske yazılmaz ve yalnızca yerel dil modeline verilir.
/// </summary>
public static partial class ScreenCapture
{
    private const int MaxSide = 1280;
    private const int ExtendedFrameBounds = 9;

    public static CapturedScreen Capture(bool wholeScreen)
    {
        // Friday'in kendi penceresi öndeyse ondan sonraki pencere alınır; Friday kendini görmez.
        var window = Desktop.Foreground()?.Handle ?? IntPtr.Zero;
        Rectangle bounds;
        var title = "Ekran";
        Bitmap? bitmap = null;
        if (!wholeScreen && window != IntPtr.Zero && TryGetBounds(window, out bounds))
        {
            title = WindowTitle(window);
            // Pencere, üstünü başka bir pencere (ör. Friday'in paneli) kapatsa bile kendi içeriğiyle çizdirilir.
            bitmap = RenderWindow(window, bounds);
        }
        else
        {
            var monitor = NativeMethods.MonitorFromWindow(window, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            NativeMethods.GetMonitorInfoW(monitor, ref info);
            bounds = Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        }

        if (bitmap is null)
        {
            bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        using var _ = bitmap;

        // Model için 1280 piksel yeter; büyük görüntü yalnızca gecikme ve VRAM demektir.
        var scale = Math.Min(1.0, (double)MaxSide / Math.Max(bounds.Width, bounds.Height));
        var size = new Size(Math.Max(1, (int)(bounds.Width * scale)), Math.Max(1, (int)(bounds.Height * scale)));
        using var resized = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(bitmap, new Rectangle(Point.Empty, size));
        }

        using var stream = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
        resized.Save(stream, encoder, parameters);
        return new CapturedScreen(stream.ToArray(), title, size.Width, size.Height);
    }

    /// <summary>
    /// PrintWindow (PW_RENDERFULLCONTENT) pencereyi kendi içeriğiyle çizer; GPU ile çizilen tarayıcılar da dahil.
    /// Pencere çerçevesi gölgeyle birlikte çizildiği için görünen sınırlara kırpılır.
    /// </summary>
    private static Bitmap? RenderWindow(IntPtr window, Rectangle visible)
    {
        if (!NativeMethods.GetWindowRect(window, out var full))
        {
            return null;
        }

        var width = full.Right - full.Left;
        var height = full.Bottom - full.Top;
        using var whole = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(whole))
        {
            var hdc = graphics.GetHdc();
            var ok = NativeMethods.PrintWindow(window, hdc, 2);
            graphics.ReleaseHdc(hdc);
            if (!ok)
            {
                return null;
            }
        }

        var crop = new Rectangle(visible.Left - full.Left, visible.Top - full.Top, visible.Width, visible.Height);
        crop.Intersect(new Rectangle(0, 0, width, height));
        return crop.Width > 0 && crop.Height > 0 ? whole.Clone(crop, PixelFormat.Format24bppRgb) : null;
    }

    private static bool TryGetBounds(IntPtr window, out Rectangle bounds)
    {
        // DWM sınırları pencerenin görünen kenarlarını verir (gölgesiz).
        if (NativeMethods.DwmGetWindowAttribute(window, ExtendedFrameBounds, out var rect, Marshal.SizeOf<NativeRect>()) != 0)
        {
            bounds = Rectangle.Empty;
            return false;
        }

        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 50 && bounds.Height > 50;
    }

    private static unsafe string WindowTitle(IntPtr window)
    {
        var buffer = stackalloc char[256];
        var length = NativeMethods.GetWindowTextW(window, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : "Pencere";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        internal static unsafe partial int GetWindowTextW(IntPtr window, char* text, int maxCount);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

        [LibraryImport("dwmapi.dll")]
        internal static partial int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect value, int size);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(IntPtr window, out NativeRect rect);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);
    }
}
