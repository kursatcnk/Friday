using System.Globalization;
using System.Text;

namespace Friday.Core;

/// <summary>Belgedeki bir parçanın metin dosyasındaki yeri (metin bellekte tutulmaz, gerekince diskten okunur).</summary>
public sealed record ChunkRef(long Offset, int Length);

/// <summary>Dizindeki bir belge: yolu, değişme zamanı, boyutu ve parçaları.</summary>
public sealed record IndexedDocument(string Path, DateTime Modified, long Size, IReadOnlyList<ChunkRef> Chunks);

/// <summary>Arama sonucu: hangi belgenin hangi parçası, ne kadar ilgili.</summary>
public sealed record DocumentHit(int Document, int Chunk, double Score);

/// <summary>
/// Belgeler için yerel anahtar sözcük dizini (BM25). Türkçe eklerle uğraşmamak için sözcüklerin ilk beş harfi kök
/// sayılır ("sözleşmesinde" → "sozle"); Türkçe bilgi erişiminde basit ve etkili bilinen bir yöntem. Bellekte yalnızca
/// sözcük → parça listeleri tutulur; parçaların metni diskte kalır.
/// </summary>
public sealed class DocumentIndex
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "ve", "ile", "bir", "bu", "su", "o", "da", "de", "mi", "mu", "ne", "icin", "gibi", "daha", "cok", "en", "ki", "ya",
        "veya", "ama", "ise", "olan", "olara", "neydi", "nedir", "nasil", "kac", "hangi", "nered", "zaman", "var", "yok",
        "bana", "benim", "bende", "belge", "belgel", "dosya", "dosyal", "icind", "hakki", "the", "a", "an", "of", "to",
        "in", "and", "or", "is", "are", "for", "on", "with"
    };

    private readonly Dictionary<string, List<(int Chunk, ushort Count)>> _postings = new(StringComparer.Ordinal);
    private readonly List<(int Document, int Index, int Length)> _chunks = [];
    private double _averageLength = 1;
    private long _totalLength;

    public int ChunkCount => _chunks.Count;

    /// <summary>Parçaları ekler; ortalama uzunluk toplu eklemenin sonunda bir kez hesaplanır.</summary>
    public void AddRange(IEnumerable<(int Document, int Index, string Text)> chunks)
    {
        foreach (var (document, index, text) in chunks)
        {
            var id = _chunks.Count;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var length = 0;
            foreach (var term in Terms(text))
            {
                counts[term] = counts.GetValueOrDefault(term) + 1;
                length++;
            }

            _chunks.Add((document, index, Math.Max(1, length)));
            _totalLength += Math.Max(1, length);
            foreach (var (term, count) in counts)
            {
                if (!_postings.TryGetValue(term, out var list))
                {
                    _postings[term] = list = [];
                }

                list.Add((id, (ushort)Math.Min(count, ushort.MaxValue)));
            }
        }

        _averageLength = _chunks.Count == 0 ? 1 : (double)_totalLength / _chunks.Count;
    }

    /// <summary>En ilgili parçalar; aynı belgeden en fazla iki parça (sonuçlar tek belgeye boğulmasın).</summary>
    public IReadOnlyList<DocumentHit> Search(string query, int take = 5)
    {
        var scores = new Dictionary<int, double>();
        var total = _chunks.Count;
        foreach (var term in Terms(query).Distinct())
        {
            if (!_postings.TryGetValue(term, out var list))
            {
                continue;
            }

            var idf = Math.Log(1 + ((total - list.Count + 0.5) / (list.Count + 0.5)));
            foreach (var (chunk, count) in list)
            {
                var length = _chunks[chunk].Length;
                var score = idf * (count * (K1 + 1)) / (count + (K1 * (1 - B + (B * length / _averageLength))));
                scores[chunk] = scores.GetValueOrDefault(chunk) + score;
            }
        }

        var perDocument = new Dictionary<int, int>();
        var hits = new List<DocumentHit>();
        foreach (var (chunk, score) in scores.OrderByDescending(pair => pair.Value))
        {
            var (document, index, _) = _chunks[chunk];
            if (perDocument.GetValueOrDefault(document) >= 2)
            {
                continue;
            }

            perDocument[document] = perDocument.GetValueOrDefault(document) + 1;
            hits.Add(new DocumentHit(document, index, score));
            if (hits.Count == take)
            {
                break;
            }
        }

        return hits;
    }

    /// <summary>Küçük harf, Türkçe harfler sadeleşir, ilk beş harf kök sayılır; sayılar olduğu gibi kalır.</summary>
    public static IEnumerable<string> Terms(string text)
    {
        var word = new StringBuilder();
        foreach (var raw in text)
        {
            var letter = Fold(raw);
            if (char.IsLetterOrDigit(letter))
            {
                word.Append(letter);
                continue;
            }

            if (Finish(word) is { } term)
            {
                yield return term;
            }
        }

        if (Finish(word) is { } last)
        {
            yield return last;
        }
    }

    /// <summary>Metni ~900 karakterlik, 150 karakter örtüşen parçalara böler; mümkünse cümle sonunda keser.</summary>
    public static IReadOnlyList<string> Split(string text, int size = 900, int overlap = 150)
    {
        var chunks = new List<string>();
        var clean = string.Join(' ', text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        var start = 0;
        while (start < clean.Length)
        {
            var end = Math.Min(clean.Length, start + size);
            if (end < clean.Length)
            {
                var stop = clean.LastIndexOfAny(['.', '!', '?', ';'], end - 1, Math.Min(250, end - start));
                if (stop > start + (size / 2))
                {
                    end = stop + 1;
                }
            }

            chunks.Add(clean[start..end].Trim());
            if (end >= clean.Length)
            {
                break;
            }

            start = Math.Max(start + 1, end - overlap);
            while (start < clean.Length && start > 0 && !char.IsWhiteSpace(clean[start - 1]))
            {
                start++;
            }
        }

        return chunks.Where(chunk => chunk.Length > 20).ToList();
    }

    private static string? Finish(StringBuilder word)
    {
        if (word.Length == 0)
        {
            return null;
        }

        var text = word.ToString();
        word.Clear();
        if (text.Length < 2 && !char.IsDigit(text[0]))
        {
            return null;
        }

        var term = text.All(char.IsDigit) || text.Length <= 5 ? text : text[..5];
        return StopWords.Contains(term) ? null : term;
    }

    private static char Fold(char letter) => char.ToLower(letter, CultureInfo.GetCultureInfo("tr-TR")) switch
    {
        'ı' => 'i',
        'ğ' => 'g',
        'ü' => 'u',
        'ş' => 's',
        'ö' => 'o',
        'ç' => 'c',
        'â' => 'a',
        'î' => 'i',
        'û' => 'u',
        var other => other
    };
}

/// <summary>Birden çok adımlı istekleri tanır: "PDF'i bul, özetle ve notlarıma ekle", "önce sesi kıs sonra müziği aç".</summary>
public static partial class AgentPlan
{
    /// <summary>Düşünme kapalı turlarda soruya eklenen yönerge (bkz. OllamaChatClient.MaxTokens).</summary>
    public const string NoThinking = "\n(Düşünmeden, hemen cevap ver.)";

    private static readonly string[] Verbs =
    [
        "aç", "bul", "yaz", "kaydet", "ekle", "özetle", "oku", "hatırlat", "gönder", "kapat", "ara", "çevir", "listele", "sil",
        "kur", "başlat", "hazırla", "karşılaştır", "topla", "kopyala", "yapıştır", "çalıştır", "göster", "söyle", "anlat",
        "araştır", "planla", "düzenle", "taşı", "not al", "hesapla"
    ];

    /// <summary>Yalnızca bilgi getiren fiiller; tek başlarına çok adımlı bir iş sayılmaz.</summary>
    private static readonly HashSet<string> Reading = new(StringComparer.Ordinal) { "oku", "özetle", "anlat", "göster", "söyle", "bul", "ara", "listele", "çevir" };

    private static readonly string[] Connectors = [" sonra ", "ardından", "sonrasında", " önce ", "en son", "daha sonra"];

    /// <summary>Modele verilen ek talimat (yalnızca çok adımlı isteklerde).</summary>
    public const string Instruction =
        "Bu istek birden çok adımlı bir görev. Önce tek kısa cümleyle planı söyle (ör. \"Üç adımda yapıyorum: bulup özetleyip notlarına ekliyorum.\"), " +
        "sonra araçları sırayla çağır ve her aracın sonucunu sonraki adımda kullan. Bir adım olmazsa nedenini söyle, kalanları yapmaya devam et. " +
        "Silme, kapatma, gönderme gibi geri alınamaz bir işten önce kullanıcıya sor. Bitince ne yaptığını tek cümleyle özetle.";

    private static readonly string[] FactWords =
    [
        " kaç", "ne kadar", "neydi", "nedir", "hangi", "ne zaman", "kim ", "kim?", "kimdi", "kimdir", "nerede", "kaçta", "tarih", "belge", "dosya",
        "sözleşme", "rapor", "fatura", "takvim", "hatırlat", "toplantı", "fiyat", "kur", "nüfus", "uzunluğu", "yüksekliği", "yüzde",
        "skor", "maç", "sonuç", "puan", "dolar", "euro"
    ];

    /// <summary>
    /// Akıllı modda düşünme adımı gerekir mi? Ölçüm (21.11.2026, Friday'in gerçek talimatı ve araçlarıyla): kesin bilgi
    /// soruları ilk kez sorulduğunda model düşünmeden de 10/10 doğru aracı seçiyor ve ilk tepki ~1,0 sn yerine ~3,4 sn
    /// sürüyordu. Ama sohbette o konunun sayılı eski bir cevabı varken düşünmeden 4 sorunun 2'sinde aracı atlayıp sayı
    /// uydurdu ("toplam 3 hatırlatman var"); düşünmeyle 4/4. Bu yüzden düşünme yalnızca kesin bilgi sorusu ve geçmişte
    /// rakam içeren bir cevap birlikteyse açılır.
    /// </summary>
    /// <remarks>
    /// Ölçümdeki dört geçmişli sorunun hepsi bir değer soruyordu ("şimdi dolar kaç?"). "Yarın akşam hatırlatırsın değil
    /// mi" gibi bir istek "hatırlat" yüzünden kesin bilgi sayılıyor, geçmişte bir telefon numarası geçince düşünme açılıp
    /// ilk cümleyi 12 sn'ye çıkarıyordu; artık sorunun bir değer sorması da gerekir.
    /// </remarks>
    public static bool NeedsThinking(string question, IEnumerable<string> earlierAnswers) =>
        IsFactual(question) && AsksForValue(question) && earlierAnswers.Any(answer => answer.Any(char.IsDigit));

    private static readonly string[] ValueWords =
    [
        " kaç", " ne kadar", " neydi", " nedir", " hangi", " ne zaman", " kim ", " kim?", " kimdi", " nerede", " var mı", " oldu mu", " durum"
    ];

    /// <summary>Soru bir değer mi soruyor ("kaç", "ne kadar", "hangi", "var mı")? Bir iş isteği ("hatırlat") sormaz.</summary>
    public static bool AsksForValue(string question)
    {
        var text = $" {question.ToLower(CultureInfo.GetCultureInfo("tr-TR"))} ";
        return ValueWords.Any(word => text.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>
    /// Kesin bir bilgi isteyen soru mu (sayı, tarih, belge, takvim)? Akıllı modda düşünme adımı yalnızca bunlarda açılır:
    /// düşünmeden model bazen aracı atlayıp sohbet geçmişinden sayı uyduruyor. Fıkra, sohbet, öneri gibi serbest
    /// isteklerde düşünme 9 sn'ye kadar gecikme ekliyor ve gerekmiyor.
    /// </summary>
    public static bool IsFactual(string question)
    {
        var text = $" {question.ToLower(CultureInfo.GetCultureInfo("tr-TR"))} ";
        // Yalnızca soru işareti yetmez: "neden", "nasıl" gibi açıklama sorularında düşünme ~5 sn ekliyor, cevabı iyileştirmiyordu.
        return FactWords.Any(word => text.Contains(word, StringComparison.Ordinal));
    }

    private static readonly string[] DocumentWords =
    [
        "belge", "sözleşme", "rapor", "dosya", "pdf", "sunum", "fatura", "teklif", "makale", "ödev", "özgeçmiş", " cv", "tez",
        "doküman", "dokuman", "word", "excel", "tablo", "kontrat", "poliçe", "dekont"
    ];

    /// <summary>Soru kullanıcının belgelerinden birine mi değiniyor? (Öyleyse ilgili parçalar önceden bulunur.)</summary>
    public static bool MentionsDocuments(string question)
    {
        var text = $" {question.ToLower(CultureInfo.GetCultureInfo("tr-TR"))} ";
        return DocumentWords.Any(word => text.Contains(word, StringComparison.Ordinal));
    }

    private static readonly string[] Unable =
    [
        "aracım yok", "aracım bulunmuyor", "araca sahip değilim", "yapamıyorum", "yapamam", "erişimim yok", "erişemiyorum",
        "imkanım yok", "imkânım yok", "yeteneğim yok", "desteklemiyorum", "bunu yapacak bir", "erişim iznim yok", "kontrol edemiyorum",
        "göremiyorum", "açamıyorum", "kuramıyorum", "ayarlayamıyorum", "internete bağlı değilim", "yeteneğim bulunmuyor",
        "yetkim bulunmuyor", "yetkim yok", "okuyamıyorum", "tarayamıyorum", "erişip",
        // Model ara sıra İngilizceye kayıp "I am sorry, but I do not have a tool…" diyordu; bu da yetersizlik sayılır.
        "i do not have", "i don't have", "i cannot", "i can't", "i am unable", "i'm unable", "do not have access", "don't have access"
    ];

    /// <summary>Model cevabının ilk cümlesi bir araç eksikliğinden yakınıyor mu ("bunu yapacak bir aracım yok")?</summary>
    public static bool SoundsUnable(string sentence)
    {
        // İngilizce cümlede Türkçe küçük harf "I"yı "ı" yapar ("ı don't have"); iki biçim de denenir.
        var text = sentence.ToLower(CultureInfo.GetCultureInfo("tr-TR")) + " " + sentence.ToLowerInvariant().Replace('’', '\'');
        return Unable.Any(phrase => text.Contains(phrase, StringComparison.Ordinal));
    }

    /// <summary>Bir işi yaptığını ya da yapacağını söyleyen kalıplar ("not ettim", "listene ekledim", "yarın hatırlatırım").</summary>
    private static readonly string[] Claims =
    [
        "ekledim", "kaydettim", "not ettim", "not aldım", "notunu aldım", "notunu ettim", "notunu düştüm", "not düştüm", "notlarına ekledim", "notlarına yazdım", "kenara yazdım", "kenara not", "kurdum", "ayarladım", "oluşturdum", "hafızama", "aklıma yazdım",
        "hatırlatacağım", "hatırlatırım", "haber vereceğim", "haber veririm", "aklımda tut", "unutmayacağım", "uyandıracağım",
        "uyandırırım", "takip edeceğim", "dürteceğim", "seslenirim", "sesleneceğim", "kaydedeceğim", "ekleyeceğim", "not edeceğim",
        "kaydediyorum", "ekliyorum", "not ediyorum", "kuruyorum", "hatırlatıyorum", "yazdım bile", "listene attım", "listeye attım",
        "hatırlayacağım", "bilgilendireceğim", "bilgilendiririm", "uyaracağım", "uyarırım"
    ];

    /// <summary>Kullanıcının, Friday'in daha önce yaptığı bir işi sorduğu kalıplar ("listeye ne ekledin?").</summary>
    private static readonly string[] AskedDone =
    [
        "ekledin", "kaydettin", "kurdun", "ayarladın", "not ettin", "not aldın", "hatırlattın", "oluşturdun", "neleri değiştirdin"
    ];

    /// <summary>
    /// Model cümlesi bir işi yaptığını ya da yapacağını söylüyor mu? Araç çağrılmadan söylenirse yalandır: not alınmamış,
    /// hatırlatma kurulmamıştır. Böyle bir cümle okunmadan tutulur ve tur, işi araçla gerçekten yapması istenerek yinelenir.
    /// </summary>
    public static bool ClaimsAction(string sentence)
    {
        var text = sentence.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        return Claims.Any(phrase => text.Contains(phrase, StringComparison.Ordinal));
    }

    /// <summary>Kullanıcı yeni bir iş değil, Friday'in önceden yaptığı bir işi mi soruyor? (O zaman "ekledim" doğrudur.)</summary>
    public static bool AsksWhatWasDone(string question)
    {
        var text = question.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        return AskedDone.Any(phrase => text.Contains(phrase, StringComparison.Ordinal));
    }

    /// <summary>
    /// İddianın hangi işe denk geldiği: "hafızama kaydettim" hatirla, "not aldım" not, "hatırlatırım" hatirlatici. Ölçümde
    /// bütün araçlar verilerek yinelenen 11 sahte iddianın çoğunda model yanlış aracı seçti (hafıza yerine arama, not yerine
    /// pano); iddianın kendi aracı adıyla verilince doğru araç çağrılıyor.
    /// </summary>
    private static readonly (string[] Phrases, string Tool)[] ClaimTools =
    [
        (["listene", "listeye", "listesine"], "liste"),
        (["yapılacak", "işlerine", "görevlerine"], "yapilacaklar"),
        (["not ettim", "not aldım", "notunu", "not düştüm", "kenara yazdım", "kenara not", "not edeceğim", "not ediyorum", "notlarına", "yazdım bile"], "not"),
        (["hatırlatıcı", "hatırlatırım", "hatırlatacağım", "hatırlatıyorum", "haber vereceğim", "haber veririm", "uyandır", "dürt", "seslen",
          "uyar", "bilgilendir", "alarm"], "hatirlatici"),
        (["hafızama", "aklımda", "aklıma", "hatırlayacağım", "unutmayacağım", "kaydettim", "kaydedeceğim", "kaydediyorum"], "hatirla")
    ];

    /// <summary>Bir iddia cümlesinin denk geldiği araçlar, en belirginden başlayarak; tanınmazsa boş.</summary>
    public static IReadOnlyList<string> ClaimedTools(string text)
    {
        var lower = text.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        return [.. ClaimTools.Where(item => item.Phrases.Any(phrase => lower.Contains(phrase, StringComparison.Ordinal))).Select(item => item.Tool).Distinct()];
    }

    /// <summary>Araç çağrılmadan "yaptım" diyen bir cevabın yinelenmesinde soruya eklenen uyarı; iddianın aracı biliniyorsa adıyla.</summary>
    /// <summary>
    /// Soru adı geçen bir web sayfasının okunmasını istiyorsa o adres ("haberler.com.tr'deki haberi oku ve özetle",
    /// "github.com/x/y'de ne yazıyor"); yoksa null. Yalnızca "aç" denmişse okunmaz (tarayıcıda açmak istenmiştir).
    /// </summary>
    public static string? PageToRead(string question)
    {
        var text = question.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        return WebAddress().Match(text) is { Success: true } address && ReadVerb().IsMatch(text)
            ? address.Value.TrimEnd('.', ',', '\'', '’')
            : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?:https?://)?(?:www\.)?[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|net|org|io|dev|edu|gov|info|co|me|app|ai)(?:\.[a-z]{2})?(?:/[^\s'’]*)?|(?:https?://)?(?:www\.)?[a-z0-9-]+\.tr(?:/[^\s'’]*)?")]
    private static partial System.Text.RegularExpressions.Regex WebAddress();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(?:oku|özetle|ne yazıyor|ne diyor|ne yazmış|anlat|incele|neler var|haberleri|haberini|içeriğ)")]
    private static partial System.Text.RegularExpressions.Regex ReadVerb();

    /// <summary>Model "yapamam" dedi ama işe uyan araçları var: onları adıyla hatırlatan not.</summary>
    public static string UnableNudge(IReadOnlyList<string> tools) =>
        $"\n[Bu işi yapabilirsin: {string.Join(", ", tools)} araçların var. \"Yapamam\" deme; uygun aracı çağırıp sonucunu kullanarak cevap ver.]";

    public static string ClaimNudge(IReadOnlyList<string> tools) =>
        "\n[Az önce bu isteğe hiçbir araç çağırmadan işi yapmış gibi cevap verdin; iş yapılmadı. "
        + (tools.Count > 0 ? $"Şimdi {tools[0]} aracını çağırarak işi gerçekten yap." : "Şimdi uygun aracı çağırarak işi gerçekten yap.")
        + " Uygun bir araç yoksa yapamadığını açıkça söyle.]";

    public static bool IsMultiStep(string question)
    {
        var text = $" {question.ToLower(CultureInfo.GetCultureInfo("tr-TR"))} ";
        var verbs = 0;
        var acting = 0;
        foreach (var word in text.Split([' ', ',', '.', ';', '!', '?'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Verbs.FirstOrDefault(verb => word == verb || (word.StartsWith(verb, StringComparison.Ordinal) && word.Length <= verb.Length + 3
                    && (word.EndsWith("ip", StringComparison.Ordinal) || word.EndsWith("ıp", StringComparison.Ordinal)
                        || word.EndsWith("up", StringComparison.Ordinal) || word.EndsWith("üp", StringComparison.Ordinal)))) is { } found)
            {
                verbs++;
                acting += Reading.Contains(found) ? 0 : 1;
            }
        }

        // "Oku ve özetle", "bul ve göster" tek bir iştir: yalnızca okuma ve anlatma fiilleri ajan modu açmaz. Ajan modunda
        // model sayfayı okuduktan sonra yolunu kaybedip "ne yapayım" aracını çağırıyordu.
        return acting > 0 && (verbs >= 2 || Connectors.Any(connector => text.Contains(connector, StringComparison.Ordinal)));
    }
}
