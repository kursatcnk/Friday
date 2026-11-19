using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Friday.Infrastructure;

/// <summary>Panodaki görseli (ekran alıntısı, kopyalanan resim) JPEG olarak okur; görsel yoksa null.</summary>
public static class ClipboardImage
{
    /// <summary>Uzun kenar bu boyuttan büyükse küçültülür; görme modeline gereğinden büyük resim gitmez.</summary>
    private const int MaxSide = 1600;

    public static byte[]? GetJpeg()
    {
        byte[]? result = null;
        // WPF panosu STA iş parçacığı ister; Friday'in arka plan iş parçacıkları MTA'dır.
        var thread = new Thread(() =>
        {
            try
            {
                if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } image)
                {
                    return;
                }

                BitmapSource source = image;
                var longest = Math.Max(image.PixelWidth, image.PixelHeight);
                if (longest > MaxSide)
                {
                    var scale = MaxSide / (double)longest;
                    source = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
                }

                var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
                encoder.Frames.Add(BitmapFrame.Create(source));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                result = stream.ToArray();
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException)
            {
                result = null;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5));
        return result;
    }
}
