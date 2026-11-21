using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Geçmiş sohbetlerden bir soru-cevap ve içinde geçtiği sohbetin başlığı.</summary>
public sealed record PastTurn(DateTime Time, string User, string Assistant, string Chat);

/// <summary>
/// Konuşma hafızası: "geçen hafta sana bir kitap sormuştum, hangisini önermiştin?" gibi geçmişe dönen sorularda eski
/// sohbetlerdeki ilgili soru-cevap bulunur ve soruya eklenir; model araç beklemeden kendi söylediğini hatırlar. Eski
/// arama sözcüğün aynen geçmesini bekliyordu ("kitap" arayınca "kitabı" bulunmuyordu, iki sözcük yan yana olmalıydı);
/// burada Türkçe köklerle ve sözcüklerin nadirliğine göre sıralanır, "dün", "geçen hafta", "salı günü" gibi zaman
/// sözleri aramayı o günlere daraltır. Geçmişe dönmeyen sorulara bir şey eklenmez: eski bir konuşma yeni bir soruda
/// gürültüdür.
/// </summary>
public static partial class ConversationRecall
{
    /// <summary>Soruya eklenen en fazla soru-cevap sayısı.</summary>
    public const int Take = 3;

    private const int ExcerptLength = 260;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly string[] Weekdays = ["pazar", "pazartesi", "sali", "carsamba", "persembe", "cuma", "cumartesi"];

    /// <summary>Konuyu taşımayan sözcükler (sadeleşmiş): zaman sözleri, hitap, "sohbet" ve "adı".</summary>
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "gecen", "gecenki", "hafta", "haftaki", "haftalarda", "gun", "gunu", "gunku", "gunler", "sefer", "seferki", "once", "onceki",
        "dun", "dunku", "bugun", "bugunku", "sabah", "aksam", "aksamki", "sabahki", "ayki", "hani", "sana", "bize", "beraber",
        "birlikte", "adi", "ismi", "acaba", "tekrar", "yine", "simdi", "galiba", "sanki", "evvelsi", "geceki"
    };

    /// <summary>Geçmişe dönme fiilleri ve sözleri, sadeleşmiş kökler: "konuşmuştuk", "önermiştin", "hatırlıyor musun".</summary>
    private static readonly string[] CueStems =
    [
        "konus", "soyle", "sormus", "sordu", "bahset", "bahsed", "anlatmis", "anlatti", "onermis", "onerdi", "hatirl", "demistin",
        "demistim", "demistik", "dedin", "dedim", "gecenl", "sohbet", "unuttun", "aklinda", "tavsiye"
    ];

    /// <summary>Soru, geçmiş bir konuşmaya mı dönüyor ("konuşmuştuk", "demiştin", "sana sormuştum", "geçen sefer").</summary>
    public static bool RefersToPast(string question) => PastCue().IsMatch(question.ToLower(Turkish));

    /// <summary>Sorudaki zaman sözünün kapsadığı aralık ("dün", "geçen hafta", "salı günü"); yoksa null.</summary>
    public static (DateTime From, DateTime To)? Window(string question, DateTime now)
    {
        var text = Simplify(question.ToLower(Turkish));
        var today = now.Date;
        if (Has(text, "evvelsi gun") || Has(text, "onceki gun") || Has(text, "dunden once"))
        {
            return (today.AddDays(-2), today.AddDays(-1));
        }

        if (Has(text, "dun") || Has(text, "dunku") || Has(text, "dun aksam") || Has(text, "dun sabah"))
        {
            return (today.AddDays(-1), today);
        }

        if (Has(text, "bugun") || Has(text, "bu sabah") || Has(text, "bu ogle") || Has(text, "demin") || Has(text, "az once"))
        {
            return (today, now.AddMinutes(1));
        }

        for (var day = 0; day < Weekdays.Length; day++)
        {
            if (Has(text, Weekdays[day]) || Has(text, Weekdays[day] + "ya") || Has(text, Weekdays[day] + "ye")
                || Has(text, Weekdays[day] + " gunu"))
            {
                // En yakın geçmiş o gün (bugün o günse geçen haftaki).
                var back = ((int)today.DayOfWeek - day + 7) % 7;
                var date = today.AddDays(-(back == 0 ? 7 : back));
                return (date, date.AddDays(1));
            }
        }

        if (Has(text, "bu hafta"))
        {
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            return (monday, now.AddMinutes(1));
        }

        if (Has(text, "gecen hafta") || Has(text, "gecenlerde") || Has(text, "gecen gun") || Has(text, "birkac gun once"))
        {
            return (today.AddDays(-14), now.AddMinutes(1));
        }

        if (Has(text, "gecen ay") || Has(text, "birkac hafta once"))
        {
            return (today.AddDays(-62), now.AddMinutes(1));
        }

        return null;
    }

    /// <summary>
    /// Soruyla en ilgili geçmiş soru-cevaplar, en ilgiliden başlayarak. Zaman sözü varsa o aralıktakiler öne geçer ama
    /// dışarıdakiler elenmez: kullanıcı günü yanlış hatırlayabilir ("dün" dediği 4 gün önceydi); bulunanın gerçek
    /// tarihi cevaba yazılır.
    /// </summary>
    public static IReadOnlyList<PastTurn> Find(string question, IReadOnlyList<PastTurn> turns, DateTime now, int take = Take)
    {
        var asked = Topic(question);
        if (asked.Count == 0 || turns.Count == 0)
        {
            return [];
        }

        var window = Window(question, now);
        return Rank(asked, turns, take, window is var (from, to) ? turn => turn.Time >= from && turn.Time < to : null);
    }

    /// <summary>"Dün ne konuşmuştuk" gibi konusuz sorular için o aralıktaki sohbetlerin başlıkları; zaman sözü yoksa boş.</summary>
    public static IReadOnlyList<(DateTime When, string Chat)> Overview(string question, IReadOnlyList<PastTurn> turns, DateTime now)
    {
        if (Topic(question).Count > 0 || Window(question, now) is not var (from, to))
        {
            return [];
        }

        return [.. turns.Where(turn => turn.Time >= from && turn.Time < to)
            .GroupBy(turn => turn.Chat)
            .Select(group => (When: group.Min(turn => turn.Time), Chat: group.Key))
            .OrderBy(item => item.When)
            .Take(8)];
    }

    /// <summary>Bir arama sözünün (aracın "ara" değeri) geçtiği soru-cevaplar: kökle, sözcük sırası gözetmeden.</summary>
    public static IReadOnlyList<PastTurn> Search(string query, IReadOnlyList<PastTurn> turns, int take = 8) =>
        Topic(query) is { Count: > 0 } asked ? Rank(asked, turns, take) : [];

    /// <summary>Soruya eklenecek satır; bulunan yoksa boş.</summary>
    public static string Context(string question, IReadOnlyList<PastTurn> turns, DateTime now)
    {
        if (!RefersToPast(question))
        {
            return "";
        }

        var found = Find(question, turns, now);
        if (found.Count > 0)
        {
            var asked = Topic(question);
            // Kullanıcı günü yanlış hatırlıyorsa ("dün" dediği 4 gün önceydi) model bunu nazikçe düzeltsin.
            var misremembered = Window(question, now) is var (from, to) && (found[0].Time < from || found[0].Time >= to)
                ? $" Not: bu konuşma kullanıcının söylediği zamanda değil, {Day(found[0].Time, now).ToLower(Turkish)} oldu; cevabında bunu kısaca düzelt."
                : "";
            return "\n[Önceki sohbetlerinden, bu soruyla ilgili: " + string.Join(" | ", found.Select(turn =>
                $"{Day(turn.Time, now)} kullanıcı sordu: \"{Shorten(turn.User, 140)}\", sen cevapladın: \"{Excerpt(turn.Assistant, asked)}\"")) + "." + misremembered + "]";
        }

        var overview = Overview(question, turns, now);
        return overview.Count == 0
            ? ""
            : "\n[O zaman konuştuğunuz konular: " + string.Join(" | ", overview.Select(item => $"{item.When.ToString("d MMMM HH:mm", Turkish)} · {item.Chat}")) + "]";
    }

    /// <summary>Sorunun konusu: geçmişe dönme fiilleri ve zaman sözleri çıkınca kalan sözcükler.</summary>
    public static IReadOnlyList<string> Topic(string question)
    {
        var words = MemoryRecall.Words(question)
            .Where(word => !Filler.Contains(word) && !Weekdays.Any(day => word.StartsWith(day, StringComparison.Ordinal) && word.Length <= day.Length + 3)
                           && !CueStems.Any(stem => word.StartsWith(stem, StringComparison.Ordinal)) && !IsPastVerb(word))
            .ToList();
        return words;
    }

    /// <summary>
    /// Nadirliğe göre puanlama. Kavram eşleri ("laptop" sorusunda "bilgisayar") yarım, yalnızca cevapta geçen sözcük
    /// 0,7, devam sorusunun önceki sorusunda geçen 0,5 sayılır; zaman sözünün aralığındaki turlar 1,5 kat öne geçer.
    /// </summary>
    private static List<PastTurn> Rank(IReadOnlyList<string> topic, IReadOnlyList<PastTurn> turns, int take, Func<PastTurn, bool>? inWindow = null)
    {
        if (turns.Count == 0)
        {
            return [];
        }

        var asked = MemoryRecall.Expand([.. topic]);

        var asks = turns.Select(turn => MemoryRecall.Words(turn.User)).ToList();
        var answers = turns.Select(turn => MemoryRecall.Words(turn.Assistant)).ToList();
        // "Yanına ne yapsam?" gibi devam sorularının konusu bir önceki soruda kalır: aynı sohbette yarım saat içinde
        // sorulmuş önceki sorunun sözcükleri yarı ağırlıkla bu tura da sayılır.
        var lead = new List<string>[turns.Count];
        var last = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < turns.Count; index++)
        {
            lead[index] = last.TryGetValue(turns[index].Chat, out var previous) && turns[index].Time - turns[previous].Time is { TotalMinutes: >= 0 and <= 30 }
                ? asks[previous]
                : [];
            last[turns[index].Chat] = index;
        }

        // Nadir sözcük ağır basar: her sohbette geçen "proje" az, bir kez geçen "Dune" çok sayılır.
        var weights = asked.ToDictionary(word => word, word =>
        {
            var count = Enumerable.Range(0, turns.Count).Count(index => asks[index].Concat(answers[index]).Any(other => Matches(word, other)));
            return count == 0 ? 0 : Math.Log(1 + ((turns.Count + 1) / (double)count)) * (topic.Contains(word) ? 1 : 0.5);
        });
        // Kullanıcının kendi sorusunda geçen sözcük konuyu taşır; yalnızca cevapta geçen biraz daha az sayılır.
        var scored = Enumerable.Range(0, turns.Count)
            .Select(index => (Turn: turns[index], Score: (inWindow?.Invoke(turns[index]) == true ? 1.5 : 1) * asked.Sum(word =>
                asks[index].Any(other => Matches(word, other)) ? weights[word]
                : answers[index].Any(other => Matches(word, other)) ? weights[word] * 0.7
                : lead[index].Any(other => Matches(word, other)) ? weights[word] * 0.5 : 0)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Turn.Time)
            .ToList();
        if (scored.Count == 0)
        {
            return [];
        }

        var best = scored[0].Score;
        return [.. scored.Where(item => item.Score >= Math.Max(0.6, best * 0.6)).Take(take).Select(item => item.Turn)];
    }

    /// <summary>Uzun bir cevabın soruyla ilgili cümleleri: ilk cümle ve konunun geçtiği cümleler, sırasıyla.</summary>
    public static string Excerpt(string answer, IReadOnlyList<string> asked)
    {
        var flat = string.Join(' ', answer.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= ExcerptLength)
        {
            return flat;
        }

        var sentences = Sentence().Split(flat).Where(sentence => sentence.Length > 0).ToList();
        var keep = sentences.Select((sentence, index) => index == 0 || MemoryRecall.Words(sentence).Any(word => asked.Any(other => Matches(word, other)))).ToList();
        var text = "";
        for (var index = 0; index < sentences.Count; index++)
        {
            if (!keep[index])
            {
                continue;
            }

            var next = text.Length == 0 ? sentences[index] : text + " " + sentences[index];
            if (next.Length > ExcerptLength)
            {
                break;
            }

            text = next;
        }

        return text.Length > 0 ? text + (text.Length < flat.Length ? " …" : "") : Shorten(flat, ExcerptLength);
    }

    /// <summary>
    /// Kökle eşleşme; ama üç harflik bir sözcük uzun bir sözcüğün başı diye sayılmaz ("dizüstü" ile "diz", "kitapçı" ile
    /// "kit" aynı şey değil). "bel"/"beli" gibi kısa çekimler yine eşleşir.
    /// </summary>
    private static bool Matches(string asked, string other) =>
        MemoryRecall.Same(asked, other) && (Math.Min(asked.Length, other.Length) > 3 || Math.Max(asked.Length, other.Length) <= 5);

    /// <summary>"Vermiştin", "hesaplamıştık" gibi geçmiş zaman fiilleri konu değildir ("verir misin"le eşleşmesin).</summary>
    private static bool IsPastVerb(string word) =>
        word.Length > 7 && (word.EndsWith("mistin", StringComparison.Ordinal) || word.EndsWith("mistik", StringComparison.Ordinal)
                            || word.EndsWith("mustun", StringComparison.Ordinal) || word.EndsWith("mustuk", StringComparison.Ordinal));

    private static string Day(DateTime when, DateTime now)
    {
        var days = (now.Date - when.Date).Days;
        return days switch
        {
            0 => $"Bugün {when:HH:mm}'de",
            1 => "Dün",
            < 7 => $"{days} gün önce ({when.ToString("d MMMM dddd", Turkish)})",
            _ => $"{when.ToString("d MMMM", Turkish)}'de ({days} gün önce)"
        };
    }

    private static string Shorten(string text, int length)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > length ? flat[..length].TrimEnd() + "…" : flat;
    }

    private static bool Has(string text, string phrase) =>
        text == phrase || text.StartsWith(phrase + " ", StringComparison.Ordinal) || text.EndsWith(" " + phrase, StringComparison.Ordinal)
        || text.Contains(" " + phrase + " ", StringComparison.Ordinal);

    private static string Simplify(string text) =>
        Punctuation().Replace(text.Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c'), " ").Trim();

    [GeneratedRegex(@"[^\p{L}\d]+")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex Sentence();

    [GeneratedRegex(@"\b(?:konuş(?:muştuk|tuk|muştun|muştum|tuğumuz|muştuğumuz)|(?:ne )?de(?:miştin|miştim|miştik|din|diğin)|söyle(?:miştin|miştim|din|diğin|mişsin)|sor(?:muştum|duğum|muştun|dum)|bahset(?:miştin|miştik|miştim|tin|tik|tiğin|tiğim)|bahsed(?:ip|en)|anlat(?:mıştın|mıştım|tın|tığın)|öner(?:miştin|din|diğin)|tavsiye et(?:miştin|tin|tiğin)|geçen (?:sefer|konuşma|sohbet)|önceki (?:sohbet|konuşma)|daha önce (?:konuş|sor|söyle|bahset)|hatırlıyor mu(?:sun)?|unuttun mu|aklında mı|\p{L}+m[ıiuü]şt[ıiuü][kn])")]
    private static partial Regex PastCue();
}
