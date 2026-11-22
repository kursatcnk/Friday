using System.Globalization;
using System.Text;

namespace Friday.Core;

/// <summary>
/// Uygulama adlarının sesçe eşleşmesi. Türkçe konuşan biri "vatsapı aç", "kromu aç", "spotifayı aç", "ekseli aç" der;
/// Whisper da çoğu zaman böyle yazar (ses ölçümünde "Spotify'ı aç" → "Zipotifiyo'a aç"). Harf harf arama bunları
/// "WhatsApp", "Google Chrome", "Spotify", "Excel" ile eşleştiremiyordu. Burada ad ünsüz iskeletine indirilir: İngilizce
/// yazımın okunuşu Türkçeye çevrilir ("wh"→v, "ch"→k, "x"→ks, "tion"→şın), ünlüler ve "h", "y", "ğ" atılır, benzer
/// sesler birleşir (s/z, v/w). "whatsapp" ile "vatsap" "vtsp", "chrome" ile "krom" "krm" olur. Ünlüler atıldığı için
/// "vatsapı", "kromu" gibi hal ekleri de kendiliğinden düşer.
/// </summary>
public static class PhoneticName
{
    private const string Vowels = "aeıioöuü";

    /// <summary>Adın ünsüz iskeleti: "WhatsApp" → "vtsp", "Google Chrome" → "glkrm", "spotifa" → "sptf".</summary>
    public static string Key(string name)
    {
        var text = name.ToLower(CultureInfo.InvariantCulture).Replace('ı', 'i').Replace('İ', 'i');
        var spelled = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var letter = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            var after = index + 2 < text.Length ? text[index + 2] : '\0';
            if (!char.IsLetter(letter))
            {
                continue;
            }

            switch (letter)
            {
                case 't' when next == 'i' && after == 'o' && index + 3 < text.Length && text[index + 3] == 'n':
                    spelled.Append("sn");
                    index += 3;
                    continue;
                case 's' when next == 'c' && after == 'h':
                    spelled.Append('s');
                    index += 2;
                    continue;
                case 's' when next == 'h':
                case 'p' when next == 'h':
                case 't' when next == 'h':
                case 'w' when next == 'h':
                case 'c' when next == 'k':
                    spelled.Append(letter switch { 's' => 's', 'p' => 'f', 't' => 't', 'w' => 'v', _ => 'k' });
                    index++;
                    continue;
                case 'c' when next == 'h':
                    // "Chrome" k, "chat" ç.
                    spelled.Append(after is 'r' or 'l' ? 'k' : 'c');
                    index++;
                    continue;
                case 'c':
                    spelled.Append(next is 'e' or 'i' or 'y' ? 's' : 'k');
                    continue;
                case 'q':
                    spelled.Append('k');
                    continue;
                case 'x':
                    spelled.Append("ks");
                    continue;
                default:
                    spelled.Append(letter switch
                    {
                        'w' => 'v', 'z' => 's', 'ş' => 's', 'ç' => 'c', 'j' => 'c', 'ö' => 'o', 'ü' => 'u', 'ğ' => 'h', _ => letter
                    });
                    continue;
            }
        }

        var key = new StringBuilder(spelled.Length);
        foreach (var letter in spelled.ToString())
        {
            if (Vowels.Contains(letter) || letter is 'h' or 'y')
            {
                continue;
            }

            if (key.Length == 0 || key[^1] != letter)
            {
                key.Append(letter);
            }
        }

        return key.ToString();
    }

    /// <summary>
    /// Söylenen ada sesçe en yakın ad; belirgin bir eşleşme yoksa null. İskelet en az üç ünsüz olmalı; birebir aynı
    /// iskelet ya da (dört ünsüzden uzunsa) tek ünsüz farkı kabul edilir. Çok sözcüklü adlarda ("Google Chrome") her
    /// sözcük de ayrıca denenir. İki ad aynı yakınlıktaysa hiçbiri seçilmez.
    /// </summary>
    public static string? Best(string spoken, IEnumerable<string> names)
    {
        var wanted = Key(spoken);
        if (wanted.Length < 3)
        {
            return null;
        }

        var ending = EndsInVowel(spoken.Trim());
        var scored = names
            .Select(name => (Name: name, Distance: Parts(name).Where(part => ending || !EndsInVowel(part)).Select(Key).Where(key => key.Length >= 2)
                .Select(key => Distance(wanted, key)).DefaultIfEmpty(int.MaxValue).Min()))
            .Where(item => item.Distance == 0 || (item.Distance == 1 && wanted.Length >= 4))
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Name.Length)
            .ToList();
        if (scored.Count == 0)
        {
            return null;
        }

        return scored.Count > 1 && scored[1].Distance == scored[0].Distance && Key(scored[1].Name) != Key(scored[0].Name) && scored[0].Distance > 0
            ? null
            : scored[0].Name;
    }

    /// <summary>Adın sözcükleri ve kendisi ("Google Chrome" → "Google", "Chrome", "Google Chrome").</summary>
    private static IEnumerable<string> Parts(string name) =>
        name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries).Append(name);

    /// <summary>
    /// Okunan bir ünlüyle mi bitiyor? İngilizcedeki sondaki "e" okunmaz ("Chrome" krom). Ünsüzle biten bir söz ("krom")
    /// okunan ünlüyle biten bir adla ("Razer Chroma") eşleşmez; tersi olabilir (Türkçe söyleyiş ünlü ekler: "spotifa").
    /// </summary>
    private static bool EndsInVowel(string word)
    {
        var lower = word.ToLower(CultureInfo.InvariantCulture);
        return lower.Length > 0 && ("aıiouüöy".Contains(lower[^1]) || (lower[^1] == 'e' && lower.Length <= 3));
    }

    private static int Distance(string first, string second)
    {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];
        for (var column = 0; column <= second.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= first.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= second.Length; column++)
            {
                var cost = first[row - 1] == second[column - 1] ? 0 : 1;
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }
}
