namespace Friday.Desktop;

/// <summary>Sistem tepsisindeki simge: pencere kapatılsa da Friday kısayolu dinlemeye devam eder.</summary>
public sealed class TrayIcon : IDisposable
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

    /// <summary>Uygulamanın simgesi; tepsi için ekranın DPI'ına uygun boyuttaki kare seçilir.</summary>
    private static System.Drawing.Icon DrawIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Friday.ico"));
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
