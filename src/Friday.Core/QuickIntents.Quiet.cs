using System.Text.RegularExpressions;

namespace Friday.Core;

public static partial class QuickIntents
{
    /// <summary>
    /// Rahatsız etme: "1 saat rahatsız etme", "beni rahatsız etme", "sessiz moda geç", "yarım saat sessiz ol",
    /// "rahatsız etmeyi kapat", "sessiz moddan çık". Hatırlatmalar yine gelir; kendiliğinden konuşmalar susar.
    /// </summary>
    private static QuickCommand? Quiet(string sentence)
    {
        if (QuietOffRequest().IsMatch(sentence))
        {
            return Command("rahatsiz_etme", ("islem", "kapat"));
        }

        if (QuietOnRequest().Match(sentence) is not { Success: true } match)
        {
            return null;
        }

        var minutes = 60;
        if (match.Groups["n"].Success)
        {
            var amount = match.Groups["n"].Value switch
            {
                "yarım" => 0.5,
                "bir" => 1,
                "iki" => 2,
                "üç" => 3,
                "dört" => 4,
                var digits => double.TryParse(digits, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 1
            };
            minutes = (int)Math.Round(match.Groups["u"].Value == "saat" ? amount * 60 : amount);
        }

        return Command("rahatsiz_etme", ("islem", "ac"), ("dakika", Math.Clamp(minutes, 1, 24 * 60).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [GeneratedRegex(@"^(?:(?<n>\d+|yarım|bir|iki|üç|dört) (?<u>saat|dakika) )?(?:(?:beni )?rahatsız etme(?: modunu aç| moduna geç| modu)?|sessiz moda geç|sessiz modu aç|sessiz ol)$")]
    private static partial Regex QuietOnRequest();

    [GeneratedRegex(@"^(?:rahatsız etme(?:yi| modunu) (?:kapat|bitir|iptal et)|sessiz mod(?:u kapat|dan çık)|artık rahatsız edebilirsin)$")]
    private static partial Regex QuietOffRequest();
}
