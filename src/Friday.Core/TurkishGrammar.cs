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

    /// <summary>
    /// Birinci tekil iyelik eki alabilen sık adlar (sadeleşmemiş, ünsüz yumuşamasıyla): "annemi", "ilacımı", "oğlumu".
    /// Genel bir kural "kalemi"ni (kalem+i) "kaleni" yapardı; yalnızca bu adlarda ek kesin iyeliktir.
    /// </summary>
    private static readonly string[] Possessed =
    [
        "anne", "baba", "eş", "kız", "oğl", "abla", "abi", "ağabey", "kardeş", "patron", "müdür", "doktor", "dişçi", "hoca",
        "öğretmen", "ilac", "ilaç", "hap", "araba", "telefon", "bilgisayar", "anahtar", "cüzdan", "gözlüğ", "çanta", "ev",
        "sevgili", "arkadaş", "komşu", "teyze", "dayı", "amca", "hala", "nine", "dede", "kedi", "köpeğ", "köpek",
        "randevu", "toplantı", "ders", "ödev", "sınav", "maaş", "kira", "fatura"
    ];

    /// <summary>
    /// Kullanıcının kendi ağzından söylediği bir metni Friday'in ağzına çevirir: "dişçiyi aramamı" → "dişçiyi aramanı",
    /// "akşam ilacımı" → "akşam ilacını", "annemi ara" → "anneni ara". Hatırlatma kurulurken ve çalınca "Dişçiyi aramamı
    /// unutma" yerine "Dişçiyi aramanı unutma" denir. Kayıtlı metin değişmez.
    /// </summary>
    public static string ToYou(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var words = text.Split(' ');
        for (var index = 0; index < words.Length; index++)
        {
            // "gitmem gerektiğini", "aramam lazım olduğunu" → "gitmen gerektiğini", "araman lazım olduğunu".
            var next = index + 1 < words.Length ? words[index + 1].ToLower(Turkish) : "";
            var lower = words[index].ToLower(Turkish);
            if (lower.Length > 4 && (lower.EndsWith("mam", StringComparison.Ordinal) || lower.EndsWith("mem", StringComparison.Ordinal))
                && (next.StartsWith("gerek", StringComparison.Ordinal) || next.StartsWith("lazım", StringComparison.Ordinal)))
            {
                words[index] = words[index][..^1] + "n";
                continue;
            }

            words[index] = YouWord(words[index]);
        }

        return string.Join(' ', words);
    }

    private static string YouWord(string word)
    {
        var lower = word.ToLower(Turkish);
        // Eylem adı + iyelik + belirtme: "aramamı", "gitmemi", "ödememi".
        if (lower.Length > 5 && lower != "tamamı" && (lower.EndsWith("mamı", StringComparison.Ordinal) || lower.EndsWith("memi", StringComparison.Ordinal)))
        {
            return word[..^2] + "n" + word[^1];
        }

        foreach (var stem in Possessed)
        {
            if (!lower.StartsWith(stem, StringComparison.Ordinal))
            {
                continue;
            }

            var at = stem.Length;
            if (lower.Length > at + 3 && (lower[at..].StartsWith("lar", StringComparison.Ordinal) || lower[at..].StartsWith("ler", StringComparison.Ordinal)))
            {
                at += 3;
            }

            var vowelEnd = BackVowels.Contains(lower[at - 1]) || FrontVowels.Contains(lower[at - 1]);
            if (vowelEnd && at < lower.Length && lower[at] == 'm')
            {
                return word[..at] + "n" + word[(at + 1)..];
            }

            if (!vowelEnd && at + 1 < lower.Length && "ıiuü".Contains(lower[at]) && lower[at + 1] == 'm')
            {
                return word[..(at + 1)] + "n" + word[(at + 2)..];
            }
        }

        return word;
    }
}
