namespace Friday.Core;

/// <summary>
/// Hafızanın dil modeline verilişi. Eskiden en yeni 80 bilgi her soruda talimata yazılıyordu: başka bir yapay zekâdan
/// yüzlerce bilgi aktarılınca eskiler hiç görülmüyor, ilgisizler her soruda ~2.000 belirteç yer kaplıyordu. Artık
/// talimatta yalnızca yaklaşan tarihli bilgiler ve en yeni birkaç bilgi durur (sabit önek, önbellekte kalır); soruyla
/// ilgili olanlar bütün hafızada Türkçe köklerle (BM25) aranır ve yalnızca o soruya eklenir.
/// </summary>
public static class MemoryRecall
{
    /// <summary>Talimatta her zaman duran en yeni bilgi sayısı (tarihli olanlar ayrıca).</summary>
    public const int Pinned = 20;

    /// <summary>Soruya eklenen ilgili bilgi sayısı.</summary>
    public const int Recalled = 6;

    /// <summary>Talimatta duran bilgiler: yaklaşan tarihliler ve en yeni <see cref="Pinned"/> bilgi.</summary>
    public static IReadOnlySet<MemoryItem> PinnedItems(IReadOnlyList<MemoryItem> memories, DateOnly today)
    {
        var dated = memories.Where(memory => MemoryDates.Countdown(memory.Text, today) is not null);
        return memories.Reverse().Take(Pinned).Concat(dated).ToHashSet();
    }

    /// <summary>Soruyla ilgili (talimatta olmayan) bilgiler, en ilgiliden başlayarak.</summary>
    public static IReadOnlyList<MemoryItem> Relevant(IReadOnlyList<MemoryItem> memories, string question, IReadOnlySet<MemoryItem> exclude, int take = Recalled)
    {
        var candidates = memories.Where(memory => !exclude.Contains(memory)).ToList();
        if (candidates.Count == 0 || question.Trim().Length == 0)
        {
            return [];
        }

        var asked = Expand(Words(question));
        if (asked.Count == 0)
        {
            return [];
        }

        var words = candidates.Select(memory => Words(memory.Text)).ToList();
        // Her soru sözcüğünün nadirliği: birçok bilgide geçen ("proje", "çalıştı") az, az geçen ("kızım", "araba") çok sayılır.
        var weights = asked.ToDictionary(word => word, word =>
        {
            var count = words.Count(memory => memory.Any(other => Same(word, other)));
            return count == 0 ? 0 : Math.Log(1 + (candidates.Count / (double)count));
        });
        var scored = candidates
            .Select((memory, position) => (Memory: memory, Score: asked.Where(word => words[position].Any(other => Same(word, other))).Sum(word => weights[word])))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ToList();
        if (scored.Count == 0)
        {
            return [];
        }

        // Zayıf eşleşmeler (yalnızca yaygın bir sözcük tutan) gürültüdür: en iyinin yarısının altı ve çok yaygın sözcükler atılır.
        var best = scored[0].Score;
        return [.. scored.Where(item => item.Score >= Math.Max(1.0, best / 2)).Take(take).Select(item => item.Memory)];
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "kullanici", "kullanicinin", "bir", "bu", "su", "ve", "ile", "icin", "gibi", "daha", "cok", "ama", "veya", "neydi", "nedir",
        "nasil", "nerede", "hangi", "kac", "kim", "ne", "mi", "mu", "misin", "musun", "bana", "benim", "beni", "bende", "sen", "seni",
        "neden", "niye", "nicin", "nedeniyle", "var", "yok", "olan", "oldu", "olur", "eder", "ediyor", "yapar", "yapti", "hakkinda", "onerir", "soyle", "anlat", "lutfen"
    };

    /// <summary>
    /// Aynı şeyin farklı söylenişleri (sadeleşmiş kökler): soru "nerede çalışıyorum", bilgi "İşyerim Maslak'ta" olabilir.
    /// Ölçümde 20 farklı söyleyişten 3'ü yalnızca bu yüzden bulunamıyordu.
    /// </summary>
    private static readonly (string Key, string[] Related)[] Concepts =
    [
        ("bilgisayar", ["laptop", "dizustu", "notebook", "masaustu"]),
        ("laptop", ["bilgisayar", "dizustu"]),
        ("calis", ["isyeri", "isyerim", "ofis", "sirket", "patron"]),
        ("isyeri", ["calis", "ofis", "sirket"]),
        ("ofis", ["isyeri", "calis"]),
        ("bos", ["hobi", "hobim"]),
        ("hobi", ["bos", "zevk"]),
        ("araba", ["arac", "otomobil"]),
        ("arac", ["araba", "otomobil"]),
        ("doktor", ["hekim"]),
        ("hekim", ["doktor"]),
        ("cocuk", ["kizim", "oglum"]),
        ("esim", ["karim", "kocam"]),
        ("karim", ["esim"]),
        ("kocam", ["esim"]),
        ("telefon", ["cep", "numara"]),
        ("spor", ["salon", "fitness", "gym", "kosu"]),
        ("oturu", ["yasiyor", "yasar", "adres"]),
        ("yasiyor", ["oturuyor", "adres"]),
        ("kira", ["ev", "sahibi"]),
        ("maas", ["ucret", "gelir"])
    ];

    /// <summary>Soru sözcüklerine kavram eşlerini ekler.</summary>
    internal static List<string> Expand(List<string> words)
    {
        var expanded = new List<string>(words);
        foreach (var word in words)
        {
            foreach (var (key, related) in Concepts)
            {
                if (Same(word, key) || (word.Length > key.Length && word.StartsWith(key, StringComparison.Ordinal)))
                {
                    expanded.AddRange(related.Where(item => !expanded.Contains(item)));
                }
            }
        }

        return expanded;
    }

    /// <summary>Küçük harf, Türkçe harfler sadeleşmiş, 3 harften uzun ve dolgu olmayan sözcükler.</summary>
    internal static List<string> Words(string text) =>
        [.. text.ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
            .Split([' ', ',', '.', '\'', '’', '?', '!', ':', ';', '(', ')', '"', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 3 && !StopWords.Contains(word))
            .Distinct()];

    /// <summary>Türkçe ekler sona gelir: "kızımın"/"kızı", "sütlü"/"süt", "arabamın"/"arabası" ortak kökle eşleşir.</summary>
    internal static bool Same(string first, string second)
    {
        if (first == second)
        {
            return true;
        }

        var shorter = Math.Min(first.Length, second.Length);
        var common = 0;
        while (common < shorter && first[common] == second[common])
        {
            common++;
        }

        return common >= 3 && common >= shorter - 2;
    }

    /// <summary>Soruya eklenecek satırlar; ilgili bilgi yoksa boş.</summary>
    public static string Context(IReadOnlyList<MemoryItem> recalled) =>
        recalled.Count == 0
            ? ""
            : "\n[Hafızadan, bu soruyla ilgili olabilir: " + string.Join(" | ", recalled.Select(memory => memory.Text.TrimEnd('.'))) + "]";
}
