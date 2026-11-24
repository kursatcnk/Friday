using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Friday kendi hızlı yolunu genişletir. Öğrenilmiş kısayollar yalnızca birebir aynı sözü hatırlıyordu; burada dil
/// modelinin tek bir araçla çözdüğü bir isteğin iskeleti öğrenilir. Aracın aldığı değerler cümlede aynen geçiyorsa
/// ("ilgaz bey'i aramayı yarın 10'da bana bildir" → hatirlatici, metin "ilgaz bey'i aramayı", zaman "yarın 10'da")
/// o değerler yuvaya dönüşür: "{metin} {zaman} bana bildir". Aynı iskelet iki farklı değerle görülünce kalıp
/// etkinleşir ve ona uyan her yeni cümle (değerleri ne olursa olsun) dil modeli beklenmeden çalışır.
/// Güvenlik: yalnızca değerleri cümleden aynen çıkan, zararsız araçlar; iskelette en az iki sabit sözcük; yuva
/// değerleri denetlenir (zaman çözülebilmeli, kur bilinen bir kur olmalı, değer en fazla sekiz sözcük). "Yanlış
/// anladın" o cümleye uyan kalıbı siler. D:\Friday\data\templates.json.
/// </summary>
public sealed partial class PhraseTemplates(string file)
{
    public const int Capacity = 200;

    /// <summary>Etkinleşmek için gereken farklı değer sayısı.</summary>
    public const int Confirmations = 2;

    private const int MaxSlotWords = 8;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Dil modelindeki ad → hızlı yoldaki ad, yuva olabilen argümanlar, sabit kalması gereken argümanlar.</summary>
    public static readonly IReadOnlyDictionary<string, (string Quick, string[] Slots, string[] Fixed, bool Tentative)> Learnable =
        new Dictionary<string, (string, string[], string[], bool)>(StringComparer.Ordinal)
        {
            ["hatirlatici"] = ("hatirlatici", ["zaman", "metin"], ["islem"], false),
            ["not"] = ("not", ["metin"], ["islem"], false),
            ["yapilacaklar"] = ("yapilacak", ["metin"], ["islem"], false),
            ["liste"] = ("liste", ["oge", "ad"], ["islem"], false),
            ["video_ac"] = ("video_ac", ["arama"], [], false),
            ["her_yerde_ara"] = ("her_yerde_ara", ["sorgu"], [], false),
            // Uygulama bulunamazsa hızlı yol null döner ve istek dil modeline gider.
            ["uygulama_ac"] = ("uygulama_ac", ["ad"], [], true),
            ["doviz"] = ("doviz", ["birim"], [], false)
        };

    /// <summary>Kur aracının bildiği birimler (altın yok: o soru dil modeliyle internete gider).</summary>
    private static readonly HashSet<string> Currencies = new(StringComparer.Ordinal)
    {
        "dolar", "euro", "avro", "sterlin", "pound", "frank", "yen", "ruble", "yuan", "riyal", "dirhem", "manat"
    };

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "bana", "beni", "bir", "de", "da", "için", "lütfen", "şunu", "bunu", "ve", "ile", "mı", "mi", "mu", "mü", "misin", "mısın"
    };

    private static readonly HashSet<string> TimeWords = new(StringComparer.Ordinal)
    {
        "saat", "sabah", "öğle", "öğlen", "öğleden", "akşam", "gece", "bugün", "yarın", "öbür", "pazartesi", "salı", "çarşamba",
        "perşembe", "cuma", "cumartesi", "pazar", "günü"
    };

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private readonly Dictionary<string, Regex> _patterns = new(StringComparer.Ordinal);
    private Dictionary<string, Template>? _templates;

    /// <summary>Etkin kalıplar ve araçları ("{metin} {zaman} bana bildir" → hatirlatici).</summary>
    public IReadOnlyList<(string Pattern, string Tool)> Learned
    {
        get
        {
            lock (_gate)
            {
                return [.. Load().Where(pair => pair.Value.Values.Count >= Confirmations).Select(pair => (pair.Key, pair.Value.Tool))];
            }
        }
    }

    /// <summary>İskeleti çıkarır; çıkarılamıyorsa null (test ve açıklama için de kullanılır).</summary>
    public static string? Skeleton(string sentence, string tool, IReadOnlyDictionary<string, string> arguments, out Dictionary<string, string> slots, out Dictionary<string, string> fixedValues)
    {
        slots = new Dictionary<string, string>(StringComparer.Ordinal);
        fixedValues = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Learnable.TryGetValue(tool, out var rule))
        {
            return null;
        }

        var text = Clean(sentence);
        var spans = new List<(int Start, int Length, string Key)>();
        foreach (var (key, raw) in arguments)
        {
            var value = Spaces().Replace(raw.ToLower(Turkish).Trim().TrimEnd('.', '!', ','), " ");
            if (value.Length == 0)
            {
                continue;
            }

            if (rule.Fixed.Contains(key, StringComparer.Ordinal))
            {
                fixedValues[key] = value;
                continue;
            }

            if (!rule.Slots.Contains(key, StringComparer.Ordinal))
            {
                return null;
            }

            // Değer cümlede bulunmalı: aynen, ya da model biraz yeniden yazdıysa ("aramayı" → "ara", "10'da" → "10:00")
            // zamanda aynı anı veren, metinde sözcükleri kökleriyle tutan parça. Yuvaya cümlenin kendi parçası yazılır.
            var at = WholeWord(text, value);
            var length = value.Length;
            if (at < 0 && Align(text, key, value) is var (start, size))
            {
                at = start;
                length = size;
            }

            if (at < 0 || spans.Any(span => at < span.Start + span.Length && span.Start < at + length))
            {
                return null;
            }

            spans.Add((at, length, key));
            slots[key] = text.Substring(at, length);
        }

        if (spans.Count == 0)
        {
            return null;
        }

        var skeleton = text;
        foreach (var span in spans.OrderByDescending(span => span.Start))
        {
            skeleton = skeleton[..span.Start] + "{" + span.Key + "}" + skeleton[(span.Start + span.Length)..];
        }

        // Zaman ya da kur gibi denetlenen bir yuva varsa tek sabit sözcük yeter ("{metin} {zaman} bildir").
        var checkedSlot = slots.ContainsKey("zaman") || tool == "doviz";
        var literal = SlotToken().Replace(skeleton, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return literal.Length >= (checkedSlot ? 1 : 2) && literal.Any(word => !Fillers.Contains(word))
            && !SlotToken().Replace(skeleton, "").Contains('{', StringComparison.Ordinal)
            ? skeleton
            : null;
    }

    /// <summary>Dil modelinin bir söz için yaptığı tek araç çağrısı; kalıp bu gözlemle etkinleştiyse true.</summary>
    public bool Observe(string sentence, string tool, IReadOnlyDictionary<string, string> arguments)
    {
        if (Skeleton(sentence, tool, arguments, out var slots, out var fixedValues) is not { } skeleton)
        {
            return false;
        }

        var rule = Learnable[tool];
        // Görülen değerler yalnızca saymak için gerekir; dosyada metnin kendisi değil kısa özeti durur.
        var signature = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join("|", slots.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value)))))[..12];
        lock (_gate)
        {
            var templates = Load();
            // Aynı biçimdeki söz ("… kısmına bunu yaz") farklı bir işe gittiyse kalıp güvenilmez: ikisi de unutulur.
            var shape = Shape(skeleton);
            var clashes = templates.Where(pair => Shape(pair.Key) == shape && (pair.Value.Tool != rule.Quick || !SameFixed(pair.Value.Fixed, fixedValues)))
                .Select(pair => pair.Key).ToList();
            if (clashes.Count > 0)
            {
                clashes.ForEach(clash => templates.Remove(clash));
                Save(templates);
                return false;
            }

            templates.TryGetValue(skeleton, out var known);

            known ??= new Template { Tool = rule.Quick, Fixed = fixedValues, Tentative = rule.Tentative };
            var before = known.Values.Count;
            if (!known.Values.Contains(signature, StringComparer.Ordinal))
            {
                known.Values.Add(signature);
            }

            known.At = DateTime.Now;
            templates[skeleton] = known;
            while (templates.Count > Capacity)
            {
                templates.Remove(templates.OrderBy(pair => pair.Value.Values.Count >= Confirmations).ThenBy(pair => pair.Value.At).First().Key);
            }

            Save(templates);
            return before < Confirmations && known.Values.Count >= Confirmations;
        }
    }

    /// <summary>Söz etkin bir kalıba uyuyorsa hızlı yol komutu; değilse null.</summary>
    public QuickCommand? Find(string sentence)
    {
        var text = Clean(sentence);
        lock (_gate)
        {
            // Daha çok sabit sözcüğü olan (daha belirgin) kalıp önce denenir.
            foreach (var (skeleton, template) in Load().Where(pair => pair.Value.Values.Count >= Confirmations)
                         .OrderByDescending(pair => SlotToken().Replace(pair.Key, "").Length))
            {
                if (Pattern(skeleton).Match(text) is not { Success: true } match)
                {
                    continue;
                }

                var arguments = new Dictionary<string, string>(template.Fixed, StringComparer.Ordinal);
                foreach (var name in SlotToken().Matches(skeleton).Select(slot => slot.Groups["k"].Value))
                {
                    arguments[name] = match.Groups[name].Value.Trim();
                }

                SplitTime(skeleton, arguments);
                var valid = arguments.Where(pair => !template.Fixed.ContainsKey(pair.Key)).All(pair => Valid(pair.Key, pair.Value, template.Tool));

                if (valid)
                {
                    // Değerler kullanıcının yazdığı biçimle döner ("Selin'in numarası", "selin'in numarası" değil).
                    foreach (var name in arguments.Keys.Where(key => !template.Fixed.ContainsKey(key)).ToList())
                    {
                        arguments[name] = Original(sentence, arguments[name]);
                    }

                    return new QuickCommand(template.Tool, arguments, template.Tentative);
                }
            }
        }

        return null;
    }

    /// <summary>Bu söze uyan kalıpları unutur ("yanlış anladın"); unutulan sayısı.</summary>
    public int Forget(string sentence)
    {
        var text = Clean(sentence);
        lock (_gate)
        {
            var templates = Load();
            var wrong = templates.Keys.Where(skeleton => Pattern(skeleton).IsMatch(text)).ToList();
            foreach (var skeleton in wrong)
            {
                templates.Remove(skeleton);
            }

            if (wrong.Count > 0)
            {
                Save(templates);
            }

            return wrong.Count;
        }
    }

    public int Clear()
    {
        lock (_gate)
        {
            var templates = Load();
            var count = templates.Values.Count(item => item.Values.Count >= Confirmations);
            templates.Clear();
            Save(templates);
            return count;
        }
    }

    /// <summary>
    /// Bitişik iki yuvadan biri zamansa ("{metin} {zaman}") sınır yeniden bulunur: tembel eşleşme "faturayı ödemeyi cuma
    /// 9'da"yı "faturayı" + "ödemeyi cuma 9'da" diye bölüyordu. Bütünle aynı anı veren en kısa zaman parçası seçilir.
    /// </summary>
    private static void SplitTime(string skeleton, Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("zaman", out var when))
        {
            return;
        }

        var now = DateTime.Now;
        foreach (Match pair in AdjacentTime().Matches(skeleton))
        {
            var other = pair.Groups["a"].Success ? pair.Groups["a"].Value : pair.Groups["b"].Value;
            var timeLast = pair.Groups["a"].Success;
            var words = (timeLast ? arguments[other] + " " + when : when + " " + arguments[other]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (TurkishTime.Parse(string.Join(' ', words), now) is not { } target)
            {
                return;
            }

            for (var size = 1; size < words.Length; size++)
            {
                var time = timeLast ? words[^size..] : words[..size];
                var rest = timeLast ? words[..^size] : words[size..];
                if (TurkishTime.Parse(string.Join(' ', time), now) == target)
                {
                    // "ilacımı içmeyi saat 22'de": zamanın yanındaki "saat", "akşam", "yarın" zamana katılır.
                    while (rest.Length > 0 && TimeWords.Contains(timeLast ? rest[^1] : rest[0]))
                    {
                        time = timeLast ? [rest[^1], .. time] : [.. time, rest[0]];
                        rest = timeLast ? rest[..^1] : rest[1..];
                    }

                    arguments["zaman"] = string.Join(' ', time);
                    arguments[other] = string.Join(' ', rest);
                    return;
                }
            }
        }
    }

    /// <summary>Küçük harfli değerin özgün cümledeki yazımı; bulunamazsa olduğu gibi.</summary>
    private static string Original(string sentence, string value)
    {
        var flat = Spaces().Replace(sentence, " ");
        var at = flat.ToLower(Turkish).IndexOf(value, StringComparison.Ordinal);
        return at >= 0 && at + value.Length <= flat.Length ? flat.Substring(at, value.Length) : value;
    }

    /// <summary>Yuva adları atılmış biçim: "{metin} kısmına yaz" ile "{oge} kısmına yaz" aynı biçimdir.</summary>
    private static string Shape(string skeleton) => SlotToken().Replace(skeleton, "{}");

    /// <summary>Hızlı yolla aynı biçim: hitap, nezaket ve baştaki bağlaç atılmış, küçük harfli cümle.</summary>
    private static string Clean(string sentence) => Spaces().Replace(QuickIntents.Normalize(sentence), " ").Trim();

    private static bool Valid(string name, string value, string tool) =>
        value.Length > 0
        && value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxSlotWords
        && (name != "zaman" || TurkishTime.Parse(value, DateTime.Now) is { } due && due > DateTime.Now)
        && (tool != "doviz" || Currencies.Contains(value));

    /// <summary>
    /// Değerin cümledeki karşılığı (başlangıç, uzunluk). Zaman: aynı anı veren en kısa sözcük dizisi. Diğerleri: aynı
    /// sayıda sözcük, her biri kökle eşleşen ("ilgaz bey'i ara" ↔ "ilgaz bey'i aramayı"). Bulunamazsa null.
    /// </summary>
    private static (int Start, int Length)? Align(string text, string key, string value)
    {
        var words = WordSpans().Matches(text).Select(match => (match.Index, match.Value)).ToList();
        if (key == "zaman")
        {
            var now = DateTime.Now;
            if (TurkishTime.Parse(value, now) is not { } target)
            {
                return null;
            }

            for (var size = 1; size <= Math.Min(5, words.Count); size++)
            {
                for (var first = 0; first + size <= words.Count; first++)
                {
                    var start = words[first].Index;
                    var end = words[first + size - 1].Index + words[first + size - 1].Value.Length;
                    if (TurkishTime.Parse(text[start..end], now) == target)
                    {
                        // "akşam 7'de", "saat 22'de", "yarın sabah 9'da": komşu zaman sözcükleri de parçaya katılır.
                        var left = first;
                        var right = first + size - 1;
                        while (left > 0 && TimeWords.Contains(words[left - 1].Value))
                        {
                            left--;
                        }

                        while (right + 1 < words.Count && TimeWords.Contains(words[right + 1].Value))
                        {
                            right++;
                        }

                        start = words[left].Index;
                        end = words[right].Index + words[right].Value.Length;
                        return (start, end - start);
                    }
                }
            }

            return null;
        }

        var wanted = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var first = 0; first + wanted.Length <= words.Count; first++)
        {
            var all = true;
            for (var index = 0; index < wanted.Length && all; index++)
            {
                all = Similar(words[first + index].Value, wanted[index]);
            }

            if (all)
            {
                var last = words[first + wanted.Length - 1];
                return (words[first].Index, last.Index + last.Value.Length - words[first].Index);
            }
        }

        return null;
    }

    private static bool Similar(string first, string second)
    {
        var a = MemoryRecall.Words(first);
        var b = MemoryRecall.Words(second);
        return string.Equals(first, second, StringComparison.Ordinal)
            || (a.Count == 1 && b.Count == 1 && MemoryRecall.Same(a[0], b[0]))
            || (a.Count == 0 && b.Count == 0 && first.Trim('\'', '’') == second.Trim('\'', '’'));
    }

    private static int WholeWord(string text, string value)
    {
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
        {
            var before = at == 0 || text[at - 1] == ' ';
            var end = at + value.Length;
            var after = end == text.Length || text[end] is ' ' or '\'' or '’' or ',' or '.';
            if (before && after)
            {
                return at;
            }
        }

        return -1;
    }

    private Regex Pattern(string skeleton)
    {
        if (!_patterns.TryGetValue(skeleton, out var pattern))
        {
            // Regex.Split adlandırılmış grubu da sonuca kattığı için parçalar elle ayrılır.
            var expression = "^";
            var position = 0;
            foreach (Match slot in SlotToken().Matches(skeleton))
            {
                expression += Regex.Escape(skeleton[position..slot.Index]) + $"(?<{slot.Groups["k"].Value}>.+?)";
                position = slot.Index + slot.Length;
            }

            expression += Regex.Escape(skeleton[position..]);

            pattern = new Regex(expression + "$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            _patterns[skeleton] = pattern;
        }

        return pattern;
    }

    private static bool SameFixed(Dictionary<string, string> first, Dictionary<string, string> second) =>
        first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private Dictionary<string, Template> Load()
    {
        if (_templates is not null)
        {
            return _templates;
        }

        try
        {
            _templates = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, Template>>(File.ReadAllText(file), Options) ?? [] : [];
        }
        catch (JsonException)
        {
            _templates = [];
        }

        return _templates;
    }

    private void Save(Dictionary<string, Template> templates)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(templates, Options));
        }
        catch (IOException)
        {
            // Kaydedilemezse bu oturumda bellekte kalır.
        }
    }

    [GeneratedRegex(@"\{(?<k>[a-z]+)\}")]
    private static partial Regex SlotToken();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\{(?<a>[a-y]+)\} \{zaman\}|\{zaman\} \{(?<b>[a-y]+)\}")]
    private static partial Regex AdjacentTime();

    [GeneratedRegex(@"[^\s:,]+")]
    private static partial Regex WordSpans();

    private sealed class Template
    {
        public string Tool { get; set; } = "";

        public Dictionary<string, string> Fixed { get; set; } = [];

        public bool Tentative { get; set; }

        /// <summary>Görülen farklı yuva değerleri (genelleşmenin kanıtı).</summary>
        public List<string> Values { get; set; } = [];

        public DateTime At { get; set; }
    }
}
