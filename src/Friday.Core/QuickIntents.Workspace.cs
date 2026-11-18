using System.Text.RegularExpressions;

namespace Friday.Core;

public static partial class QuickIntents
{
    /// <summary>
    /// Masa düzenleri: "kodlama masasını kaydet", "bu düzeni oyun olarak kaydet", "masamı kur", "kodlama masasını aç",
    /// "masalarım", "oyun masasını sil".
    /// </summary>
    private static QuickCommand? Workspace(string sentence)
    {
        if (WorkspaceList().IsMatch(sentence))
        {
            return Command("masa_duzeni", ("islem", "listele"));
        }

        if (WorkspaceNamed().Match(sentence) is { Success: true } named)
        {
            return Command("masa_duzeni", ("islem", "kaydet"), ("ad", named.Groups["n"].Value.Trim()));
        }

        if (WorkspaceRequest().Match(sentence) is not { Success: true } match)
        {
            return null;
        }

        var name = match.Groups["n"].Success ? match.Groups["n"].Value.Trim() : "";
        var action = match.Groups["v"].Value switch
        {
            "kaydet" => "kaydet",
            "sil" => "sil",
            _ => "kur"
        };
        return Command("masa_duzeni", ("islem", action), ("ad", name.Length > 0 ? name : "masam"));
    }

    [GeneratedRegex(@"^(?:(?<n>[\p{L}\d ]{2,30}?) )?(?:masa(?:m|mı|mi|sı|sını|yı)|masa düzenimi|pencere düzenimi)(?: düzenini)? (?<v>kaydet|kur|aç|getir|geri getir|hazırla|sil)$")]
    private static partial Regex WorkspaceRequest();

    [GeneratedRegex(@"^(?:bu |şu anki )?(?:pencere )?düzeni (?<n>[\p{L}\d ]{2,30}?) (?:olarak|diye|adıyla) kaydet$")]
    private static partial Regex WorkspaceNamed();

    [GeneratedRegex(@"^(?:kayıtlı )?(?:masalarım|masalar|masa düzenlerim|masa düzenleri)(?: neler| hangileri)?$")]
    private static partial Regex WorkspaceList();
}
