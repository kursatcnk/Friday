using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Friday.Desktop;

public partial class PanelView : UserControl
{
    public PanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is PanelViewModel model)
            {
                TodayCards.Order = model.HomeOrder;
                model.HomeLayoutChanged += (_, _) => TodayCards.Order = model.HomeOrder;
                model.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PanelViewModel.IsPaletteOpen) && model.IsPaletteOpen)
                    {
                        Dispatcher.BeginInvoke(() => PaletteBox.Focus(), DispatcherPriority.Input);
                    }
                };
            }
        };
    }

    private void OnPaletteKey(object sender, KeyEventArgs e)
    {
        if (DataContext is not PanelViewModel model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                model.MovePalette(1);
                e.Handled = true;
                break;
            case Key.Up:
                model.MovePalette(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                model.RunPalette();
                e.Handled = true;
                break;
            case Key.Escape:
                model.IsPaletteOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OnPaletteClick(object sender, MouseButtonEventArgs e) => (DataContext as PanelViewModel)?.RunPalette();

    /// <summary>Paletin dışına tıklayınca kapanır; kartın içine tıklamak kapatmaz.</summary>
    private void OnPaletteBackdrop(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PanelViewModel model)
        {
            model.IsPaletteOpen = false;
        }
    }

    private void OnPaletteCard(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>Anlık görüntü çekerken halkalar sabit bir ana dondurulur.</summary>
    public void FreezeAnimation(double time)
    {
        NavRing.FrozenTime = time;
        TodayRing.FrozenTime = time;
        ChatView.FreezeAnimation(time);
    }
}
