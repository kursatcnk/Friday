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

    /// <summary>Bölüm kısayoluna basınca o kartın başına kayar.</summary>
    private void OnJump(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name } && FindName(name) is FrameworkElement target)
        {
            var offset = target.TransformToAncestor(Sections).Transform(new Point(0, 0)).Y;
            Scroller.ScrollToVerticalOffset(offset);
        }
    }
}
