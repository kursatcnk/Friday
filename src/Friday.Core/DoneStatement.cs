using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// "Yaptım" cümlesinin açık bir işi karşılaması: "annemi aradım" → "annemi ara", "raporu gönderdim" → "raporu gönder",
/// "mail attım" → "mail at", "sütü aldım" → "süt al". Eskiden yalnızca "tamamladım/bitirdim/hallettim" iş kapatıyordu;
/// gündelik söyleyişlerin çoğu dil modeline gidiyor ya da alışkanlık sanılıp boşa düşüyordu. İşin son sözcüğü fiildir;
/// cümlenin son sözcüğü o fiilin geçmiş zamanı olmalı (ünsüz ikizleşmesiyle: at → attım, git → gittim, et → ettim) ve
/// işin nesneleri cümlede kökleriyle geçmelidir. Nesnesi olmayan iş ("koş") yalnızca fiille eşleşmez.
/// </summary>
public static partial class DoneStatement
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "bugün", "az", "önce", "demin", "şimdi", "sonunda", "nihayet", "bile", "de", "da", "artık", "çoktan", "zaten", "ben"
    };

    /// <summary>Cümle birinci tekil geçmiş zamanla bitiyor mu ("…dım/dim/tım…")?</summary>
    /// <summary>
    /// "Dün akşam ne yemiştim" (soru, -mıştım) ve "bugün çok yoruldum" (kendiliğinden olan, -ıl/-ul) bir işi yaptığını
    /// söylemez; eskiden yapılacaklarda eşleşme aranıp boşa bir adım atılıyordu.
    /// </summary>
    public static bool IsDoneStatement(string sentence) =>
        Words(sentence) is [.., var last] words && PastFirstPerson().IsMatch(last) && !NegativePast().IsMatch(last)
        && !PastPerfect().IsMatch(last) && !Undergone().IsMatch(last) && !words.Any(QuestionWords.Contains);

    private static readonly HashSet<string> QuestionWords = new(StringComparer.Ordinal)
    {
        "ne", "neyi", "neden", "niye", "niçin", "nasıl", "kim", "kimi", "kime", "nerede", "nereye", "nereden", "hangi", "kaç", "mı", "mi", "mu", "mü"
    };

    [GeneratedRegex(@"m[ıiuü]ş(?:t|d)[ıiuü]m$")]
    private static partial Regex PastPerfect();

    /// <summary>"Yoruldum", "sıkıldım", "üzüldüm", "kırıldım": başına gelen, yapılan bir iş değil.</summary>
    [GeneratedRegex(@"^(?:yoruldum|sıkıldım|üzüldüm|kırıldım|bunaldım|daraldım|şaşırdım|korktum|acıktım|susadım|üşüdüm|hastalandım|uyudum|uyandım|bayıldım|sinirlendim|rahatladım)$")]
    private static partial Regex Undergone();

    /// <summary>Cümle bu işi yapıldı diye mi söylüyor?</summary>
    public static bool Fulfills(string sentence, string task)
    {
        var said = Words(sentence).Where(word => !Fillers.Contains(word)).ToList();
        var todo = Words(task);
        if (said.Count < 2 || todo.Count < 2 || NegativePast().IsMatch(said[^1]) || PastFirstPerson().Match(said[^1]) is not { Success: true } past)
        {
            return false;
        }

        var stem = said[^1][..past.Index];
        var verb = todo[^1];
        // "at" → "att", "git" → "gitt", "et" → "ett": ikizleşen ünsüz; "gönder" → "gönder".
        if (stem != verb && !(stem.Length == verb.Length + 1 && stem.StartsWith(verb, StringComparison.Ordinal) && stem[^1] == verb[^1]))
        {
            return false;
        }

        var objects = todo.Take(todo.Count - 1).Where(word => !Fillers.Contains(word)).ToList();
        var others = said.Take(said.Count - 1).ToList();
        return objects.Count > 0 && objects.All(word => others.Any(other => MemoryRecall.Same(Fold(word), Fold(other))));
    }

    /// <summary>
    /// "Sütü aldım", "süt ve ekmeği aldım", "marketten yumurta aldım": alınan şeyler (listeden düşmek için); "aldım" ile
    /// bitmiyorsa boş. "Marketten" gibi nereden alındığını söyleyen sözcükler atılır.
    /// </summary>
    public static IReadOnlyList<string> Bought(string sentence)
    {
        var words = Words(sentence).Where(word => !Fillers.Contains(word)).ToList();
        if (words.Count < 2 || words[^1] is not ("aldım" or "aldık"))
        {
            return [];
        }

        var things = words.Take(words.Count - 1).Where(word => word != "satın" && !Ablative().IsMatch(word)).ToList();
        return [.. string.Join(' ', things).Split([" ve ", " ile ", " bir de "], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim()).Where(item => item.Length > 1)];
    }

    [GeneratedRegex(@"['’]?(?:dan|den|tan|ten)$")]
    private static partial Regex Ablative();

    private static List<string> Words(string text) =>
        [.. Split().Split(text.ToLower(Turkish).Trim().TrimEnd('.', '!', '?')).Where(word => word.Length > 0)];

    private static string Fold(string word) =>
        word.Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');

    [GeneratedRegex(@"(?:dım|dim|dum|düm|tım|tim|tum|tüm)$")]
    private static partial Regex PastFirstPerson();

    /// <summary>Olumsuz geçmiş: "aramadım", "öyle demedim" bir işi yapmadığını söyler.</summary>
    [GeneratedRegex(@"m[ae]d[ıi]m$")]
    private static partial Regex NegativePast();

    [GeneratedRegex(@"[\s,;:]+")]
    private static partial Regex Split();
}
