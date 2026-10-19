using System.Windows.Controls;

namespace Friday.Desktop;

public partial class HudView : UserControl
{
    public HudView() => InitializeComponent();

    /// <summary>Anlık görüntü çekerken halka animasyonu sabit bir ana dondurulur.</summary>
    public void FreezeAnimation(double time) => Ring.FrozenTime = time;
}
