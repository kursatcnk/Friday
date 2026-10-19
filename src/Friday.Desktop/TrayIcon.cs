namespace Friday.Desktop;

/// <summary>Sistem tepsisindeki simge: pencere kapatılsa da Friday kısayolu dinlemeye devam eder.</summary>
public sealed partial class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _image;

    public TrayIcon(string name, Action show, Action exit)
    {
        _image = DrawIcon();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) => show());
        menu.Items.Add("Çıkış", null, (_, _) => exit());

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _image,
            Text = name,
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                show();
            }
        };
    }

    /// <summary>Ayrı bir .ico dosyası gerekmesin diye simge çalışırken çizilir: mavi bir küre.</summary>
    private static System.Drawing.Icon DrawIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(2, 2, 28, 28);
            using var brush = new System.Drawing.Drawing2D.PathGradientBrush(path)
            {
                CenterColor = System.Drawing.Color.FromArgb(0x7F, 0xB2, 0xFF),
                SurroundColors = [System.Drawing.Color.FromArgb(0x1E, 0x3D, 0x73)],
                CenterPoint = new System.Drawing.PointF(12, 11)
            };
            graphics.FillEllipse(brush, 2, 2, 28, 28);
        }

        var handle = bitmap.GetHicon();
        using var temporary = System.Drawing.Icon.FromHandle(handle);
        var icon = (System.Drawing.Icon)temporary.Clone();
        NativeMethods.DestroyIcon(handle);
        return icon;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }

    private static partial class NativeMethods
    {
        [System.Runtime.InteropServices.LibraryImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static partial bool DestroyIcon(IntPtr handle);
    }
}
