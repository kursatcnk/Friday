using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>İki sürüm arasındaki farklar: eklenen, çıkan ve değişen cümleler.</summary>
public sealed record TextChanges(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<(string Before, string After)> Changed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    /// <summary>Dil modeline verilecek düz liste (sınırlı uzunlukta).</summary>
    public string Describe(int budget = 5000)
    {
        var lines = new List<string>();
        lines.AddRange(Changed.Select(change => $"DEĞİŞTİ: \"{change.Before}\" → \"{change.After}\""));
        lines.AddRange(Added.Select(sentence => $"EKLENDİ: \"{sentence}\""));
        lines.AddRange(Removed.Select(sentence => $"ÇIKARILDI: \"{sentence}\""));
        var result = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (result.Length + line.Length > budget)
            {
                result.Append("… (").Append(lines.Count).Append(" farkın bir kısmı)");
                break;
            }

            result.AppendLine(line);
        }

        return result.ToString().Trim();
    }
}

/// <summary>
/// Bir belgenin iki sürümünü cümle cümle karşılaştırır. Fark dil modeline bırakılmaz (uzun iki metni karşılaştırmakta
/// güvenilmez); kod hangi cümlelerin eklendiğini, çıktığını ve değiştiğini bulur, model yalnızca bunları anlatır.
/// </summary>
public static partial class TextDiff
{
    private const int MaxItems = 40;

    public static TextChanges Compare(string before, string after)
    {
        var old = Sentences(before);
        var fresh = Sentences(after);
        var oldKeys = old.Select(Key).ToHashSet(StringComparer.Ordinal);
        var freshKeys = fresh.Select(Key).ToHashSet(StringComparer.Ordinal);
        var removed = old.Where(sentence => !freshKeys.Contains(Key(sentence))).Distinct().ToList();
        var added = fresh.Where(sentence => !oldKeys.Contains(Key(sentence))).Distinct().ToList();

        // Çıkan bir cümleyle eklenen bir cümle büyük ölçüde aynıysa bu bir değişikliktir ("2.000" → "2.300").
        var changed = new List<(string, string)>();
        foreach (var gone in removed.ToList())
        {
            var words = Words(gone);
            var best = added.Select(sentence => (sentence, score: Overlap(words, Words(sentence))))
                .Where(item => item.score >= 0.5).OrderByDescending(item => item.score).FirstOrDefault();
            if (best.sentence is { } match)
            {
                changed.Add((gone, match));
                removed.Remove(gone);
                added.Remove(match);
            }
        }

        return new TextChanges([.. added.Take(MaxItems)], [.. removed.Take(MaxItems)], [.. changed.Take(MaxItems)]);
    }

    /// <summary>Metnin cümleleri (çok kısa parçalar ve sayfa numaraları atılır).</summary>
    public static IReadOnlyList<string> Sentences(string text) =>
        [.. SentenceEnd().Split(Space().Replace(text, " ").Replace(" \n", "\n", StringComparison.Ordinal))
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length >= 12 && sentence.Count(char.IsLetter) >= 6)];

    private static string Key(string sentence) => NonWord().Replace(sentence.ToLowerInvariant(), "");

    private static HashSet<string> Words(string sentence) =>
        [.. NonWord().Split(sentence.ToLowerInvariant()).Where(word => word.Length > 0)];

    private static double Overlap(HashSet<string> first, HashSet<string> second) =>
        first.Count == 0 || second.Count == 0 ? 0 : first.Intersect(second).Count() / (double)Math.Max(first.Count, second.Count);

    // Cümle sonu: nokta/ünlem/soru + boşluk (sayılardaki "2.300" bölünmez) ya da satır sonu.
    [GeneratedRegex(@"(?<=[.!?…])\s+(?=\S)|\n+")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"[ \t\r\f\v]+")]
    private static partial Regex Space();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();
}
