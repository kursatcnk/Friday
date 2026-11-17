using System.Globalization;

namespace Friday.Core;

/// <summary>Hazır cümle kurarken gereken küçük Türkçe kuralları: bulunma eki, liste bağlama, süre okuma.</summary>
public static class TurkishGrammar
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private const string BackVowels = "aıou";
    private const string FrontVowels = "eiöü";
    private const string Voiceless = "çfhkpsşt";

    /// <summary>Özel ada bulunma eki: İstanbul'da, İzmir'de, Kars'ta, Eskişehir'de, Tokat'ta.</summary>
    public static string Locative(string name)
    {
        var word = name.Trim();
        if (word.Length == 0)
        {
            return word;
        }

        var lower = word.ToLower(Turkish);
        var vowel = lower.LastOrDefault(letter => BackVowels.Contains(letter) || FrontVowels.Contains(letter));
        var back = vowel == default || BackVowels.Contains(vowel);
        var hard = Voiceless.Contains(lower[^1]);
        return $"{word}'{(hard ? "t" : "d")}{(back ? "a" : "e")}";
    }

    /// <summary>"a", "a ve b", "a, b ve c".</summary>
    public static string Join(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " ve " + items[^1]
    };

    /// <summary>Dakika cinsinden süreyi okunur hale getirir: 0,5 → "30 saniye", 90 → "1 saat 30 dakika".</summary>
    public static string Duration(double minutes)
    {
        var total = TimeSpan.FromMinutes(minutes);
        if (total.TotalMinutes < 1)
        {
            return $"{Math.Max(1, Math.Round(total.TotalSeconds)).ToString(CultureInfo.InvariantCulture)} saniye";
        }

        var hours = (int)total.TotalHours;
        var rest = (int)Math.Round(total.TotalMinutes - (hours * 60));
        return (hours, rest) switch
        {
            (0, _) => $"{rest} dakika",
            (_, 0) => $"{hours} saat",
            _ => $"{hours} saat {rest} dakika"
        };
    }

    /// <summary>Cümlenin ilk harfini büyütür (Türkçe kurallarıyla: "ışık" → "Işık").</summary>
    public static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Turkish) + text[1..];

    /// <summary>"çamaşırı", "toplantıyı" gibi belirtme hâlindeki nesne mi?</summary>
    public static bool IsAccusative(string phrase) =>
        phrase.Trim().Length > 3 && "ıiuü".Contains(phrase.Trim()[^1]);
}
