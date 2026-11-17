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
}
