using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Friday.Desktop;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not PanelViewModel viewModel)
            {
                return;
            }

            // PasswordBox güvenlik nedeniyle bağlanamaz; değeri elle eşitlenir.
            AzureKeyBox.Password = viewModel.AzureKey;
            AzureKeyBox.PasswordChanged += (_, _) => viewModel.AzureKey = AzureKeyBox.Password;
        };
    }

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly Dictionary<Border, string> _cardText = [];

    /// <summary>
    /// Ayarlarda arama: her kartın içindeki bütün yazılar (başlık, açıklama, seçenek adları) taranır; aranan her sözcüğü
    /// içeren kartlar kalır. Türkçe harfler ve büyük-küçük harf fark etmez.
    /// </summary>
    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        var words = Fold(SearchBox.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var shown = 0;
        foreach (var card in Sections.Children.OfType<Border>())
        {
            if (!_cardText.TryGetValue(card, out var text))
            {
                text = _cardText[card] = Fold(string.Join(' ', Texts(card)));
            }

            var match = words.All(word => text.Contains(word, StringComparison.Ordinal));
            card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            shown += match ? 1 : 0;
        }

        NoResults.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        Scroller.ScrollToTop();
    }

    private static IEnumerable<string> Texts(DependencyObject node)
    {
        switch (node)
        {
            case TextBlock { Text.Length: > 0 } block:
                yield return block.Text;
                break;
            case ContentControl { Content: string content }:
                yield return content;
                break;
            case TextBox box when System.Windows.Automation.AutomationProperties.GetName(box) is { Length: > 0 } name:
                yield return name;
                break;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            foreach (var text in Texts(child))
            {
                yield return text;
            }
        }
    }

    private static string Fold(string text) =>
        text.ToLower(Turkish).Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c').Trim();

    /// <summary>Bölüm kısayoluna basınca o kartın başına kayar.</summary>
    private void OnJump(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name } && FindName(name) is FrameworkElement target)
        {
            if (SearchBox.Text.Length > 0)
            {
                SearchBox.Text = "";
                UpdateLayout();
            }

            var offset = target.TransformToAncestor(Sections).Transform(new Point(0, 0)).Y;
            Scroller.ScrollToVerticalOffset(offset);
        }
    }
}
