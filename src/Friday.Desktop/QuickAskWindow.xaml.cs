using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Friday.Desktop;

/// <summary>Hızlı komut kutusunda bir öneri: rutin, son sorulan ya da hazır komut.</summary>
public sealed record QuickAskItem(string Glyph, string Text, string Kind);

/// <summary>
/// Hızlı komut kutusu (Raycast/Spotlight gibi): kısayolla her yerden açılır, yazılan Enter'la Friday'e gider ve kutu
/// kapanır; cevap köşedeki küçük pencerede görünür ve okunur. Yazdıkça rutinler, son sorular ve hazır komutlar süzülür.
/// </summary>
public partial class QuickAskWindow : Window
{
    private const int MaxSuggestions = 7;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly Func<IReadOnlyList<QuickAskItem>> _source;
    private readonly Action<string> _submit;
    private IReadOnlyList<QuickAskItem> _all = [];

    public QuickAskWindow(Func<IReadOnlyList<QuickAskItem>> source, Action<string> submit)
    {
        InitializeComponent();
        _source = source;
        _submit = submit;
        Input.TextChanged += (_, _) => Filter();
        Input.PreviewKeyDown += OnKey;
        Suggestions.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (Suggestions.SelectedItem is QuickAskItem item)
            {
                Send(item.Text);
            }
        };
        Deactivated += (_, _) => Hide();
    }

    /// <summary>Kapalıysa ekranın üst üçte birinde açar, açıksa kapatır.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        _all = _source();
        Input.Text = "";
        Filter();
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.2;
        Show();
        Activate();
        Dispatcher.BeginInvoke(() => Keyboard.Focus(Input), DispatcherPriority.Input);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Tab when Suggestions.SelectedItem is QuickAskItem item:
                Input.Text = item.Text;
                Input.CaretIndex = Input.Text.Length;
                e.Handled = true;
                break;
            case Key.Enter:
                var text = Suggestions.SelectedItem is QuickAskItem selected ? selected.Text : Input.Text.Trim();
                if (text.Length > 0)
                {
                    Send(text);
                }

                e.Handled = true;
                break;
        }
    }

    private void Move(int step)
    {
        if (Suggestions.Items.Count == 0)
        {
            return;
        }

        var index = Suggestions.SelectedIndex + step;
        Suggestions.SelectedIndex = index < -1 ? Suggestions.Items.Count - 1 : index >= Suggestions.Items.Count ? -1 : index;
        if (Suggestions.SelectedItem is { } item)
        {
            Suggestions.ScrollIntoView(item);
        }
    }

    private void Send(string text)
    {
        Hide();
        _submit(text);
    }

    private void Filter()
    {
        var query = Input.Text.Trim().ToLower(Turkish);
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var matches = (query.Length == 0
                ? _all
                : _all.Where(item => item.Text.ToLower(Turkish).Contains(query, StringComparison.Ordinal)))
            .Take(MaxSuggestions)
            .ToList();
        Suggestions.ItemsSource = matches;
        Suggestions.SelectedIndex = -1;
        ListArea.Visibility = matches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
