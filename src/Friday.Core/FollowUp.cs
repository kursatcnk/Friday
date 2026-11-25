using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Eksik söylenmiş devam komutları: "dolar kaç" → "peki euro?", "market listeme süt ekle" → "yumurta da",
/// "İstanbul'da hava nasıl" → "ya Ankara'da?" ya da "peki yarın?". Bunlar tek başına hiçbir kalıba uymadığı için dil
/// modeline gidiyordu: ölçümde "peki euro" 1,6 sn, "ya Ankara'da" 36,7 sn sürdü, "yumurta da" 18 sn'den uzun sürüp
/// yarıda kaldı. Burada son hızlı komutun cümlesinde yeni değer eski değerin yerine konur ("dolar kaç" → "euro kaç")
/// ya da başına eklenir ("yarın İstanbul'da hava nasıl"); yeni cümle aynı araca gidiyor ve son komuttan yalnızca bir
/// bilgisi farklıysa o komut çalışır. Söz devam olduğunu belli etmelidir ("peki", "ya", "bir de", "da/de", soru);
/// "tamam", "güzel" gibi sözler listeye eklenmez, şehir sanılmaz.
/// </summary>
public static partial class FollowUp
{
    /// <summary>Son hızlı komuttan sonra bu kadar süre içinde söylenen kısa söz devam sayılabilir.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(3);

    private const int MaxWords = 3;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Devamı anlamlı araçlar: bir değeri değiştirip aynısını yeniden sormak ya da eklemek.</summary>
    private static readonly HashSet<string> Tools = new(StringComparer.Ordinal)
    {
        "doviz", "hava_durumu", "liste", "yapilacak", "yapilacaklar", "saat_ve_tarih", "gun_sayaci", "ekran_suresi"
    };

    /// <summary>Bir değerin yerine değil cümlenin başına gelen zaman sözleri ("peki yarın", "ya hafta sonu").</summary>
    private static readonly HashSet<string> TimeWords = new(StringComparer.Ordinal)
    {
        "bugün", "yarın", "öbür", "haftaya", "hafta", "pazartesi", "salı", "çarşamba", "perşembe", "cuma", "cumartesi", "pazar",
        "sabah", "öğlen", "akşam", "gece", "dün"
    };

    /// <summary>Devam sözünü son komutun üzerine kurar; kurulamazsa null.</summary>
    public static (string Text, QuickCommand Command)? Resolve(string text, string lastText, QuickCommand last, Func<string, QuickCommand?> match)
    {
        // "347 çarpı 28 kaç" → "bunu ikiye böl": önceki hesabın sonucu üzerinden yeni hesap (bkz. SpokenMath.Continue).
        if (last.Tool == "hesap" && last.Arguments.TryGetValue("soru", out var question) && SpokenMath.Continue(text, question) is { } next)
        {
            return (next, new QuickCommand("hesap", new Dictionary<string, string> { ["soru"] = next }));
        }

        // Whisper "Peki, euro." diye virgül koyuyor, soru işaretini düşürüyor.
        var plain = string.Join(' ', text.Replace(',', ' ').Trim().TrimEnd('?', '.', '!').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!Tools.Contains(last.Tool) || Shape().Match(plain) is not { Success: true } shape)
        {
            return null;
        }

        var marked = shape.Groups["p"].Success || shape.Groups["d"].Success || shape.Groups["t"].Success || text.TrimEnd().EndsWith('?');
        var value = shape.Groups["v"].Value.Trim();
        var single = !value.Contains(' ', StringComparison.Ordinal);
        // "Ya Ankara'da?" çoğu kez yalnızca "Ankara'da" diye yazıya dökülür; tek başına bulunma ekli bir ad zaten devamdır.
        if (!marked && single && last.Tool is "hava_durumu" or "saat_ve_tarih" && Locative().IsMatch(value))
        {
            marked = true;
        }

        // "Kira da" bitişik "Kirada" diye yazılır: liste ve işlerde tek sözcüğün sonundaki "da/de" bağlaçtır.
        if (!marked && single && last.Tool is "liste" or "yapilacak" or "yapilacaklar" && value.Length > 4
            && Fold(value)[^2..] is "da" or "de" or "ta" or "te")
        {
            value = value[..^2];
            marked = true;
        }

        if (!marked || value.Length < 2 || value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxWords)
        {
            return null;
        }

        foreach (var (candidate, time) in Candidates(value, lastText, last))
        {
            // Zaman sözü günü ve odağı birlikte değiştirebilir; cümlede geçen değerler (şehir) korunmalıdır.
            if (match(candidate) is { Tentative: false } command && command.Tool == last.Tool
                && (time ? Differences(command, last) >= 1 && KeepsSlots(command, last, lastText) : Differences(command, last) == 1))
            {
                return (candidate, command);
            }
        }

        return null;
    }

    /// <summary>
    /// Yeni cümle adayları: son komutun değerleri cümlede geçtiği yerde, en sondakinden başlayarak yenisiyle değiştirilir
    /// (Türkçede asıl içerik sona yakındır: "market listeme süt ekle"de "süt"); bulunma eki yeni ada ünlü uyumuyla
    /// eklenir ("ya İzmir" → "İzmir'de"). Zaman sözleri ise önce cümlenin başına konur ("yarın İstanbul'da hava nasıl").
    /// </summary>
    private static IEnumerable<(string Text, bool Time)> Candidates(string value, string lastText, QuickCommand last)
    {
        var words = Words(lastText);
        var prepended = value + " " + string.Join(' ', words);
        if (TimeWords.Contains(Fold(value).Split(' ')[0]))
        {
            yield return (prepended, true);
            yield break;
        }

        var folded = words.Select(Fold).ToArray();
        var slots = last.Arguments.Values
            .Where(old => old.Trim().Length >= 2)
            .Select(old => Find(folded, Fold(old.Trim()).Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(slot => slot.At >= 0)
            .OrderByDescending(slot => slot.At)
            .DistinctBy(slot => slot.At);
        foreach (var (at, length) in slots)
        {
            var word = words[at + length - 1];
            var apostrophe = word.IndexOfAny(Apostrophes);
            var suffix = apostrophe > 0 ? word[(apostrophe + 1)..].ToLower(Turkish) : "";
            // Whisper cümle başını büyük yazar ("Yumurta da"); eski değer küçük harfliyse ("süt") yenisi de küçük olur.
            if (char.IsLower(words[at][0]) && char.IsUpper(value[0]) && value.IndexOfAny(Apostrophes) < 0)
            {
                value = char.ToLower(value[0], Turkish) + value[1..];
            }

            yield return (Replace(words, at, length, value), false);
            if (suffix is "da" or "de" or "ta" or "te" && value.IndexOfAny(Apostrophes) < 0)
            {
                yield return (Replace(words, at, length, TurkishGrammar.Locative(value)), false);
            }
        }

        yield return (prepended, false);
    }

    private static string[] Words(string text) => text.Trim().TrimEnd('?', '.', '!').Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Son cümlede geçen değerler (ör. şehir) yeni komutta da aynı mı?</summary>
    private static bool KeepsSlots(QuickCommand command, QuickCommand last, string lastText)
    {
        var folded = Words(lastText).Select(Fold).ToArray();
        return last.Arguments
            .Where(pair => pair.Value.Trim().Length >= 2 && Find(folded, Fold(pair.Value.Trim()).Split(' ', StringSplitOptions.RemoveEmptyEntries)).At >= 0)
            .All(pair => Fold(command.Arguments.GetValueOrDefault(pair.Key) ?? "") == Fold(pair.Value));
    }

    private static readonly char[] Apostrophes = ['\'', '’'];

    /// <summary>Son cümlede bir değerin (birkaç sözcük olabilir) en sondaki yeri; son sözcük ek almış olabilir.</summary>
    private static (int At, int Length) Find(string[] folded, string[] parts)
    {
        for (var at = folded.Length - parts.Length; at >= 0; at--)
        {
            var found = true;
            for (var index = 0; index < parts.Length && found; index++)
            {
                found = index == parts.Length - 1
                    ? folded[at + index].StartsWith(parts[index], StringComparison.Ordinal)
                    : folded[at + index] == parts[index];
            }

            if (found)
            {
                return (at, parts.Length);
            }
        }

        return (-1, 0);
    }

    private static string Replace(string[] words, int at, int length, string value) =>
        string.Join(' ', words.Take(at).Append(value).Concat(words.Skip(at + length)));

    /// <summary>İki komutun kaç bilgisi farklı (eksik bilgi de farklı sayılır).</summary>
    private static int Differences(QuickCommand first, QuickCommand second) =>
        first.Arguments.Keys.Union(second.Arguments.Keys, StringComparer.Ordinal)
            .Count(key => !string.Equals(Fold(first.Arguments.GetValueOrDefault(key) ?? ""), Fold(second.Arguments.GetValueOrDefault(key) ?? ""), StringComparison.Ordinal));

    private static string Fold(string text) => text.ToLower(Turkish);

    /// <summary>"peki euro", "ya Ankara'da", "yumurta da", "bir de ekmek", "peynir de ekle", "peki yarın".</summary>
    [GeneratedRegex(@"^(?<p>(?:peki|ya|bir de|ayrıca|o zaman|hani)\s+)?(?<v>.+?)(?<d>\s+(?:da|de|ta|te))?(?<t>\s+(?:ekle|koy|at|yaz|nasıl|kaç|ne kadar|ne durumda|için|olsun))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Shape();

    /// <summary>Kesme işaretiyle bulunma eki almış bir ad: "Ankara'da", "İzmir'de", "Kars'ta".</summary>
    [GeneratedRegex(@"^\p{Lu}\p{L}+['’](?:da|de|ta|te)$")]
    private static partial Regex Locative();
}
