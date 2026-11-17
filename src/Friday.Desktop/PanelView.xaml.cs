using System.Collections.Specialized;
using System.Windows.Controls;

namespace Friday.Desktop;

public partial class PanelView : UserControl
{
    public PanelView()
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
            viewModel.Lines.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    Transcript.ScrollToEnd();
                }
            };
        };

        // Cevap akarken balon uzar; en alt görünür kalsın.
        Transcript.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange > 0)
            {
                Transcript.ScrollToEnd();
            }
        };
    }

    /// <summary>Anlık görüntü çekerken halkalar sabit bir ana dondurulur.</summary>
    public void FreezeAnimation(double time)
    {
        NavRing.FrozenTime = time;
        HeroRing.FrozenTime = time;
        TodayRing.FrozenTime = time;
    }
}
