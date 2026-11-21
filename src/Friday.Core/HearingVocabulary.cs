using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Friday'in kulağı: Whisper, çözümlemeye başlamadan önce verilen ipucu metnindeki sözcüklere yatkınlaşır. Genel bir
/// model "Ekinsu", "Validebağ", "Ilgaz Bey" gibi adları bilemez; kullanıcının dünyasındaki özel adlar (hafızadaki
/// kişiler ve yerler, işlerde ve notlarda geçen adlar, listeler, sık kullanılan uygulamalar, şehir) buradan toplanıp
/// ipucuna yazılır. Ayar gerekmez; kullanıcı Friday'e bir şey anlattıkça kulağı da ona alışır.
/// </summary>
public static partial class HearingVocabulary
{
    /// <summary>İpucu Whisper'ın 224 belirteçlik sınırının epey altında kalsın (her ad 2–4 belirteç).</summary>
    public const int MaxTerms = 36;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Büyük harfle yazılsa da Whisper'ın zaten bildiği, ipucunda yer kaplamaması gereken sözcükler.</summary>
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
        "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar", "Türkçe", "İngilizce", "Türkiye",
        "Bey", "Hanım", "Hoca", "Bay", "Bayan", "Dr", "Prof", "Friday", "Kullanıcı", "Kullanıcının", "Not", "Hatırlatma"
    };

    /// <summary>"Merhaba Friday. Ekinsu, Ilgaz Bey, Validebağ, Visual Studio Code."</summary>
    public static string Prompt(string assistantName, IEnumerable<string> terms)
    {
        var name = string.IsNullOrWhiteSpace(assistantName) ? "Friday" : assistantName.Trim();
        var list = terms
            .Select(term => term.Trim().TrimEnd('.', ','))
            .Where(term => term.Length > 1 && !string.Equals(term, name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToList();
        return list.Count == 0 ? $"Merhaba {name}." : $"Merhaba {name}. {string.Join(", ", list)}.";
    }

    /// <summary>
    /// Kaynaklardan ipucu terimleri: sabit terimler (kullanıcının adı, şehir, liste ve uygulama adları) önce, sonra
    /// metinlerde en çok geçen özel adlar.
    /// </summary>
    public static IReadOnlyList<string> Collect(IEnumerable<string> fixedTerms, IEnumerable<string> texts)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var first = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var name in texts.SelectMany(ProperNames))
        {
            counts[name] = counts.GetValueOrDefault(name) + 1;
            first.TryAdd(name, order++);
        }

        var named = counts
            .OrderByDescending(pair => pair.Value)
            .ThenByDescending(pair => first[pair.Key])
            .Select(pair => pair.Key);
        return [.. fixedTerms.Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term.Trim()).Concat(named)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTerms)];
    }

    /// <summary>
    /// Metindeki özel adlar: cümle ortasında büyük harfle başlayan sözcükler, ekleri kesilmiş ("Elif'in" → "Elif");
    /// arkasından gelen unvanla birlikte ("Ilgaz Bey", "Tülin Hanım"). Cümle başındaki sözcük yalnızca ek kesmesi ya da
    /// unvan taşıyorsa alınır ("Ekinsu'nun dersi…"); "Yarın", "Annem" gibi cümle başları ad sayılmaz.
    /// </summary>
    public static IEnumerable<string> ProperNames(string text)
    {
        foreach (var sentence in SentenceBreak().Split(text))
        {
            var words = Word().Matches(sentence).Select(match => match.Value).ToList();
            var stems = words.Select(word => word.IndexOfAny(['\'', '’']) is var at and > 0 ? word[..at] : word).ToList();
            for (var index = 0; index < words.Count; index++)
            {
                var stem = stems[index];
                var suffixed = stem.Length < words[index].Length;
                var titled = index + 1 < words.Count && Titles.Contains(stems[index + 1]) && Capitalized(stems[index + 1]);
                if (!Capitalized(stem) || Titles.Contains(stem) || Common.Contains(stem) || stem.Length < 3
                    || (index == 0 && !suffixed && !titled))
                {
                    continue;
                }

                if (titled && !suffixed)
                {
                    yield return $"{stem} {stems[index + 1]}";
                    index++;
                }
                else
                {
                    yield return stem;
                }
            }
        }
    }

    /// <summary>
    /// Yanlış duyulan sözün düzeltmesi: kullanıcı söylediğini hemen ardından yazarak tekrarladıysa (sözcüklerin en az
    /// yarısı aynı), yazıda olup duyulanda olmayan ve duyulan bir sözcüğe çok benzeyen özel adlar ("Kinsu" yerine
    /// "Ekinsu") kulağın öğrenmesi gereken adlardır.
    /// </summary>
    public static IReadOnlyList<string> Corrections(string heard, string typed)
    {
        var heardWords = Word().Matches(heard).Select(match => Fold(Stem(match.Value))).Where(word => word.Length > 0).ToList();
        var typedWords = Word().Matches(typed).Select(match => Fold(Stem(match.Value))).Where(word => word.Length > 0).ToList();
        if (heardWords.Count == 0 || typedWords.Count == 0 || typedWords.Count > heardWords.Count * 2 || heardWords.Count > typedWords.Count * 2)
        {
            return [];
        }

        var shared = typedWords.Count(word => heardWords.Contains(word));
        if (shared * 2 < typedWords.Count || shared == typedWords.Count)
        {
            return [];
        }

        var learned = new List<string>();
        foreach (var name in ProperNames(typed).Where(name => !name.Contains(' ', StringComparison.Ordinal)))
        {
            var folded = Fold(name);
            if (heardWords.Contains(folded))
            {
                continue;
            }

            var closest = heardWords.Where(word => !typedWords.Contains(word)).Select(word => Distance(word, folded)).DefaultIfEmpty(int.MaxValue).Min();
            if (closest <= Math.Max(2, folded.Length / 3))
            {
                learned.Add(name);
            }
        }

        return [.. learned.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static string Stem(string word) => word.IndexOfAny(['\'', '’']) is var at and > 0 ? word[..at] : word;

    private static int Distance(string first, string second)
    {
        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        for (var i = 1; i <= first.Length; i++)
        {
            var current = new int[second.Length + 1];
            current[0] = i;
            for (var j = 1; j <= second.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (first[i - 1] == second[j - 1] ? 0 : 1));
            }

            previous = current;
        }

        return previous[second.Length];
    }

    /// <summary>
    /// Sessizlikte Whisper bazen ipucunu "duyar". İpucundaki adlardan en az üçünü, ipucundaki sırayla ve başka hiçbir
    /// şey söylemeden döken bir yazı kullanıcının sözü sayılmaz.
    /// </summary>
    public static bool IsEcho(string transcript, string prompt)
    {
        var heard = Fold(transcript);
        if (heard.Length == 0)
        {
            return false;
        }

        var words = heard.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 3 && $" {Fold(prompt)} ".Contains($" {heard} ", StringComparison.Ordinal);
    }

    private static readonly HashSet<string> Titles = new(StringComparer.OrdinalIgnoreCase) { "Bey", "Hanım", "Hoca", "Abla", "Abi", "Teyze", "Amca" };

    private static bool Capitalized(string word) => word.Length > 1 && char.IsUpper(word[0]) && word.Skip(1).Any(char.IsLower);

    private static string Fold(string text) =>
        string.Join(' ', Word().Matches(text.ToLower(Turkish)).Select(match => match.Value.Trim('\'', '’', '.', '-')).Where(word => word.Length > 0));

    [GeneratedRegex(@"[.!?\n;:]+\s*")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’.-]*")]
    private static partial Regex Word();
}
