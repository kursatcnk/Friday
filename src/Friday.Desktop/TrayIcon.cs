namespace Friday.Desktop;

/// <summary>Sistem tepsisindeki simge: pencere kapatılsa da Friday kısayolu dinlemeye devam eder.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _image;
    private readonly System.Windows.Forms.ContextMenuStrip _menu;

    public TrayIcon(string name, Action show, Action exit)
    {
        _image = DrawIcon();
        var menu = _menu = new System.Windows.Forms.ContextMenuStrip();
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

    /// <summary>"Çıkış"ın üstüne, her açılışta yazısı yenilenen bir öğe ekler.</summary>
    public void AddItem(Func<string> label, Action click)
    {
        var item = new System.Windows.Forms.ToolStripMenuItem(label(), null, (_, _) => click());
        _menu.Items.Insert(Math.Max(0, _menu.Items.Count - 1), item);
        _menu.Opening += (_, _) => item.Text = label();
    }

    /// <summary>"Çıkış"ın üstüne, her açılışta yeniden doldurulan bir alt menü ekler (ör. rutinler).</summary>
    public void AddSubmenu(string label, Func<IEnumerable<string>> items, Action<string> click)
    {
        var parent = new System.Windows.Forms.ToolStripMenuItem(label);
        _menu.Items.Insert(Math.Max(0, _menu.Items.Count - 1), parent);
        _menu.Opening += (_, _) =>
        {
            parent.DropDownItems.Clear();
            foreach (var name in items())
            {
                var text = name;
                parent.DropDownItems.Add(System.Globalization.CultureInfo.GetCultureInfo("tr-TR").TextInfo.ToTitleCase(text), null, (_, _) => click(text));
            }

            parent.Enabled = parent.DropDownItems.Count > 0;
        };
    }

    /// <summary>
    /// "Çıkış"ın üstüne, her açılışta o anki duruma göre yeniden kurulan bir alt menü ekler (ör. ritim: odaktayken
    /// duraklat/uzat/bitir, moladayken döndüm). Boş etiket ayraçtır; liste boşsa alt menü soluk görünür.
    /// </summary>
    public void AddActions(string label, Func<IEnumerable<(string Label, Action Click)>> items)
    {
        var parent = new System.Windows.Forms.ToolStripMenuItem(label);
        _menu.Items.Insert(Math.Max(0, _menu.Items.Count - 1), parent);
        _menu.Opening += (_, _) =>
        {
            parent.DropDownItems.Clear();
            foreach (var (text, click) in items())
            {
                if (text.Length == 0)
                {
                    parent.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
                    continue;
                }

                var action = click;
                parent.DropDownItems.Add(text, null, (_, _) => action());
            }

            parent.Enabled = parent.DropDownItems.Count > 0;
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
