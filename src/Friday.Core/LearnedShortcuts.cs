using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

/// <summary>
/// Friday kullandıkça hızlanır: hızlı yolun bir kalıbına uymayan bir söz dil modeline gider; model aynı söz için iki
/// kez aynı tek aracı aynı ayarlarla çağırırsa ("sesi biraz kıs" → medya, ses -10), söz öğrenilmiş bir kısayola dönüşür
/// ve bir dahaki sefere dil modeli beklenmeden (1–3 sn yerine ~0,1 sn) çalışır. Yalnızca bağlamdan bağımsız, zararsız
/// araçlar ve en az iki sözcüklük sözler öğrenilir; "yanlış anladın" kısayolu siler. D:\Friday\data\shortcuts.json.
/// </summary>
public sealed class LearnedShortcuts(string file)
{
    public const int Capacity = 300;
    public const int Confirmations = 2;

    /// <summary>
    /// Öğrenilebilen araçlar: dil modelindeki adı → hızlı yoldaki adı ve izin verilen argümanlar. Argümanı hızlı yolda
    /// aynı anlama gelmeyen araçlar (ör. hava durumunun "gün"ü, saatin "tarih" odağı) listede yoktur.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Quick, string[] Keys)> Learnable =
        new Dictionary<string, (string Quick, string[] Keys)>(StringComparer.Ordinal)
        {
            ["medya"] = ("medya", ["komut", "deger"]),
            ["uygulama_ac"] = ("uygulama_ac", ["ad"]),
            ["uygulama_sesi"] = ("uygulama_sesi", ["islem", "ad", "deger"]),
            ["video_ac"] = ("video_ac", ["arama"]),
            ["doviz"] = ("doviz", ["birim"]),
            ["ritim"] = ("ritim", ["islem", "dakika"]),
            ["rahatsiz_etme"] = ("rahatsiz_etme", ["islem", "dakika"]),
            ["pomodoro"] = ("pomodoro", ["islem", "adet"]),
            ["odak_hedefi"] = ("odak_hedefi", ["dakika"]),
            ["ortam"] = ("ortam", ["islem", "tur", "dakika"]),
            ["pencere"] = ("pencere", ["islem"]),
            ["ekran_suresi"] = ("ekran_suresi", ["gun", "uygulama"]),
            ["takvim"] = ("takvim", ["gun"]),
            ["haberler"] = ("haber", ["kategori"]),
            ["her_yerde_ara"] = ("her_yerde_ara", ["sorgu"]),
            ["o_gun"] = ("o_gun", ["gun"]),
            ["gun_plani"] = ("gun_plani", []),
            ["ne_yapayim"] = ("ne_yapayim", []),
            ["haftalik_ozet"] = ("haftalik_ozet", []),
            ["ozel_gunler"] = ("ozel_gunler", []),
            ["kacirdiklarim"] = ("kacirdiklarim", []),
            ["durum_raporu"] = ("durum_raporu", [])
        };

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private Dictionary<string, Shortcut>? _shortcuts;

    /// <summary>Birebir sözün ötesinde öğrenilen kalıplar ("{metin} {zaman} bildir"; bkz. <see cref="PhraseTemplates"/>).</summary>
    public PhraseTemplates Templates { get; } = new(Path.Combine(Path.GetDirectoryName(file) ?? ".", "templates.json"));

    /// <summary>Öğrenilmiş kısayollar: söz → hızlı yol aracı.</summary>
    public IReadOnlyList<(string Phrase, string Tool)> Learned
    {
        get
        {
            lock (_gate)
            {
                return [.. Load().Where(pair => pair.Value.Seen >= Confirmations).Select(pair => (pair.Key, pair.Value.Tool))];
            }
        }
    }

    /// <summary>
    /// Dil modelinin bir söz için yaptığı tek araç çağrısı. Aynı söz aynı çağrıyla ikinci kez görülünce kısayol
    /// öğrenilir; true döner.
    /// </summary>
    public bool Observe(string text, string tool, IReadOnlyDictionary<string, string> arguments)
    {
        // Kalıp da gözlenir: değerleri cümlede aynen geçen istekler yeni değerlere genellenebilir.
        var generalized = Templates.Observe(text, tool, arguments);
        return ObserveExact(text, tool, arguments) || generalized;
    }

    private bool ObserveExact(string text, string tool, IReadOnlyDictionary<string, string> arguments)
    {
        var key = Key(text);
        if (key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2 || !Learnable.TryGetValue(tool, out var rule)
            || arguments.Keys.Any(name => !rule.Keys.Contains(name, StringComparer.Ordinal)))
        {
            return false;
        }

        var clean = arguments.Where(pair => pair.Value.Trim().Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.Ordinal);
        lock (_gate)
        {
            var shortcuts = Load();
            var same = shortcuts.TryGetValue(key, out var known) && known.Tool == rule.Quick && Equal(known.Arguments, clean);
            var seen = same ? known!.Seen + 1 : 1;
            shortcuts[key] = new Shortcut { Tool = rule.Quick, Arguments = clean, Seen = seen, At = DateTime.Now };
            while (shortcuts.Count > Capacity)
            {
                // Yer açılırken önce hiç doğrulanmamış, en eski gözlemler gider.
                var oldest = shortcuts.OrderBy(pair => pair.Value.Seen >= Confirmations).ThenBy(pair => pair.Value.At).First().Key;
                shortcuts.Remove(oldest);
            }

            Save(shortcuts);
            return seen == Confirmations;
        }
    }

    /// <summary>Söz öğrenilmiş bir kısayolsa ya da öğrenilmiş bir kalıba uyuyorsa hızlı yol komutu; değilse null.</summary>
    public QuickCommand? Find(string text)
    {
        var key = Key(text);
        lock (_gate)
        {
            if (Load().TryGetValue(key, out var shortcut) && shortcut.Seen >= Confirmations)
            {
                return new QuickCommand(shortcut.Tool, new Dictionary<string, string>(shortcut.Arguments, StringComparer.Ordinal));
            }
        }

        return Templates.Find(text);
    }

    public bool Forget(string text)
    {
        var template = Templates.Forget(text) > 0;
        lock (_gate)
        {
            var shortcuts = Load();
            if (!shortcuts.Remove(Key(text)))
            {
                return template;
            }

            Save(shortcuts);
            return true;
        }
    }

    /// <summary>Hepsini unutur; öğrenilmiş kısayol sayısını döndürür.</summary>
    public int Clear()
    {
        lock (_gate)
        {
            var shortcuts = Load();
            var count = shortcuts.Values.Count(item => item.Seen >= Confirmations) + Templates.Clear();
            shortcuts.Clear();
            Save(shortcuts);
            return count;
        }
    }

    private static string Key(string text) => CorrectionBook.Key(text);

    private static bool Equal(Dictionary<string, string> first, Dictionary<string, string> second) =>
        first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase));

    private Dictionary<string, Shortcut> Load()
    {
        if (_shortcuts is not null)
        {
            return _shortcuts;
        }

        try
        {
            _shortcuts = File.Exists(file)
                ? JsonSerializer.Deserialize<Dictionary<string, Shortcut>>(File.ReadAllText(file), Options) ?? []
                : [];
        }
        catch (JsonException)
        {
            _shortcuts = [];
        }

        return _shortcuts;
    }

    private void Save(Dictionary<string, Shortcut> shortcuts)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(shortcuts, Options));
        }
        catch (IOException)
        {
            // Kaydedilemezse bu oturumda bellekte kalır.
        }
    }

    private sealed class Shortcut
    {
        public string Tool { get; set; } = "";

        public Dictionary<string, string> Arguments { get; set; } = [];

        public int Seen { get; set; }

        public DateTime At { get; set; }
    }
}
