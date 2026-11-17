using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Friday.Infrastructure;

namespace Friday.Desktop;

/// <summary>Ekranın altındaki kapsülün durumu.</summary>
public sealed partial class HudViewModel : ObservableObject
{
    private const int CaptionTail = 150;

    /// <summary>Araç adı → (Segoe Fluent simgesi, kısa etiket).</summary>
    private static readonly Dictionary<string, string> ToolGlyphs = new()
    {
        ["saat_ve_tarih"] = "",
        ["uygulama_ac"] = "",
        ["web_sitesi_ac"] = "",
        ["web_ara"] = "",
        ["medya"] = "",
        ["hava_durumu"] = "",
        ["zamanlayici"] = "",
        ["ekrana_bak"] = "",
        ["pano"] = "",
        ["hatirla"] = "",
        ["unut"] = "",
        ["dosya_bul"] = "",
        ["dosya_ac"] = "",
        ["sistem_durumu"] = "",
        ["rutin"] = "",
        ["gunluk_ozet"] = "",
        ["gecmis"] = "",
        ["linguaflow"] = "",
        ["masaustu"] = "",
        ["masaustu_kontrol"] = "",
        ["internette_ara"] = "",
        ["sayfayi_oku"] = "",
        ["doviz"] = "\uE825",
        ["video_ac"] = "\uE714",
        ["ritim"] = "\uE9D9",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(StateBrush), nameof(IsMicOpen), nameof(StateGlow))]
    private AssistantState _state = AssistantState.Idle;

    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserText))]
    private string _userText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCaption))]
    private string _caption = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTool))]
    private string _toolLabel = "";

    [ObservableProperty]
    private string _toolGlyph = "";

    public bool HasUserText => UserText.Length > 0;

    public bool HasCaption => Caption.Length > 0;

    public bool HasTool => ToolLabel.Length > 0;

    public bool IsMicOpen => State == AssistantState.Listening;

    public string StatusLabel => State switch
    {
        AssistantState.Listening => "Dinliyorum",
        AssistantState.Thinking => "Düşünüyorum",
        AssistantState.Speaking => "Konuşuyorum",
        AssistantState.Error => "Bir sorun var",
        AssistantState.Starting => "Hazırlanıyor",
        _ => "Hazır"
    };

    public Brush StateBrush => Frozen(new SolidColorBrush(AudioRing.Palette(State)));

    /// <summary>Kapsülün üst kenarındaki ışık çizgisi.</summary>
    public Brush StateGlow
    {
        get
        {
            var color = AudioRing.Palette(State);
            return Frozen(new LinearGradientBrush(
                [
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0),
                    new GradientStop(Color.FromArgb(200, color.R, color.G, color.B), 0.5),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1)
                ], 0));
        }
    }

    public void BeginTurn()
    {
        UserText = "";
        Caption = "";
        ToolLabel = "";
    }

    public void SetReply(string text) =>
        Caption = text.Length <= CaptionTail ? text : "…" + text[^CaptionTail..].TrimStart();

    public void SetTool(string tool)
    {
        ToolGlyph = ToolGlyphs.GetValueOrDefault(tool, "");
        ToolLabel = AssistantTools.DescribeActivity(tool);
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
