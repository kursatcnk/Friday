using System.Text.RegularExpressions;

namespace Friday.Core;

public static partial class QuickIntents
{
    /// <summary>"Bugün ne kadar bilgisayardaydım", "ekran sürem", "dün en çok ne kullandım", "bugün ne kadar YouTube izledim".</summary>
    private static QuickCommand? ScreenTime(string sentence)
    {
        if (AppTimeRequest().Match(sentence) is { Success: true } app)
        {
            return Command("ekran_suresi", ("gun", app.Groups["d"].Value == "dün" ? "dun" : "bugun"), ("uygulama", app.Groups["a"].Value.Trim()));
        }

        if (ScreenTimeRequest().Match(sentence) is { Success: true } match)
        {
            return Command("ekran_suresi", ("gun", match.Value.Contains("dün", StringComparison.Ordinal) ? "dun" : "bugun"));
        }

        return null;
    }

    [GeneratedRegex(@"^(?:(?:bugün|bugünkü|dün|dünkü) )?ekran süre(?:m|si|mi)(?: ne kadar| ne| nedir)?$|^(?:bugün|dün) (?:ne kadar|kaç saat) (?:bilgisayar(?:da|ın başında|daydım|başındaydım)|ekranda|ekran başında)(?: kaldım| oturdum| geçirdim)?$|^(?:bugün |dün )?en çok (?:hangi uygulamayı|ne|neyi) kullandım$|^(?:bugün |dün )?zamanımı (?:nerede|neye) harcadım$")]
    private static partial Regex ScreenTimeRequest();

    [GeneratedRegex(@"^(?<d>bugün|dün) ne kadar (?<a>[\p{L}\d+ ]{2,25}?) (?:izledim|kullandım|baktım|oynadım|açık kaldı|dinledim)$")]
    private static partial Regex AppTimeRequest();
}
