using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Türkçe ses, yabancı adları Türkçe harf kurallarıyla okur ("Claude" → "cilaude"). Seslendirmeden önce bu adlar
/// Türkçe okunuşlarıyla değiştirilir; ekrandaki metin değişmez. "Claude'a" gibi ekler korunur.
/// </summary>
public static class Pronunciation
{
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Claude"] = "Klod",
        ["ChatGPT"] = "Çet Ci Pi Ti",
        ["OpenAI"] = "Opın Ey Ay",
        ["Gemini"] = "Cemini",
        ["Gemma"] = "Cema",
        ["Ollama"] = "Olama",
        ["llama-server"] = "lama server",
        ["Whisper"] = "Vispır",
        ["Friday"] = "Fraydey",
        ["Google"] = "Gugıl",
        ["YouTube"] = "Yutub",
        ["Chrome"] = "Krom",
        ["Spotify"] = "Spotifay",
        ["WhatsApp"] = "Vatsap",
        ["Discord"] = "Diskord",
        ["Steam"] = "Stim",
        ["Netflix"] = "Netfliks",
        ["Windows"] = "Vindovs",
        ["Microsoft"] = "Maykrosoft",
        ["Visual Studio"] = "Vijual Stüdyo",
        ["VS Code"] = "Vi Es Kod",
        ["GitHub"] = "Githab",
        ["Excel"] = "Eksel",
        ["PowerPoint"] = "Pavır Poynt",
        ["Word"] = "Vörd",
        ["Outlook"] = "Autluk",
        ["Teams"] = "Tims",
        ["Zoom"] = "Zum",
        ["iPhone"] = "Ayfon",
        ["Apple"] = "Epıl",
        ["LinguaFlow"] = "Lingua Flov",
        ["Python"] = "Paytın",
        ["JavaScript"] = "Cava Skript",
        ["Docker"] = "Dokır",
        ["Azure"] = "Ejur",
        ["Wi-Fi"] = "Vay Fay",
        ["Bluetooth"] = "Bluetut",
        ["download"] = "davnlod",
        ["update"] = "apdeyt",
        ["online"] = "onlayn",
        ["offline"] = "oflayn",
        ["email"] = "imeyl",
        ["e-mail"] = "imeyl",
        ["CPU"] = "si pi yu",
        ["GPU"] = "ci pi yu",
        ["VRAM"] = "vi ram",
        ["SSD"] = "es es di",
        ["USB"] = "yu es bi",
        ["PDF"] = "pe de fe",
        ["AI"] = "yapay zekâ",
        ["GB"] = "gigabayt",
        ["MB"] = "megabayt",
        ["km/sa"] = "kilometre",
    };

    /// <param name="overrides">Ayarlardaki kullanıcı eklemeleri; varsayılanları ezer.</param>
    public static string Apply(string text, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var table = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase);
        if (overrides is not null)
        {
            foreach (var (word, spoken) in overrides)
            {
                table[word] = spoken;
            }
        }

        // Uzun ifadeler önce: "Visual Studio" ile "Studio" çakışmasın.
        foreach (var (word, spoken) in table.OrderByDescending(entry => entry.Key.Length))
        {
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(word)}(?![\p{{L}}\p{{N}}])";
            text = Regex.Replace(text, pattern, spoken, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }

        return text;
    }
}
