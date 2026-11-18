using System.Windows.Controls;

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
            }
        };
    }

    /// <summary>Anlık görüntü çekerken halkalar sabit bir ana dondurulur.</summary>
    public void FreezeAnimation(double time)
    {
        NavRing.FrozenTime = time;
        TodayRing.FrozenTime = time;
        ChatView.FreezeAnimation(time);
    }
}
