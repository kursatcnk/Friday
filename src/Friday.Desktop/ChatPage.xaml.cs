using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Friday.Desktop;

public partial class ChatPage : UserControl
{
    public ChatPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not PanelViewModel viewModel)
            {
                return;
            }

            viewModel.Lines.CollectionChanged += (_, e) =>
            {
                if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
                {
                    Dispatcher.BeginInvoke(Transcript.ScrollToEnd, DispatcherPriority.Background);
                }
            };
            viewModel.PropertyChanged += (_, e) =>
            {
                // Yeniden adlandırma açılınca yazı kutusu seçili gelir.
                if (e.PropertyName == nameof(PanelViewModel.IsRenaming) && viewModel.IsRenaming)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        RenameBox.Focus();
                        RenameBox.SelectAll();
                    }, DispatcherPriority.Input);
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

    public void FreezeAnimation(double time) => HeroRing.FrozenTime = time;

    public void FocusDraft() => DraftBox.Focus();
}
