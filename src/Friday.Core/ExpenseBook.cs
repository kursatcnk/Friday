using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

/// <summary>Bir harcama: ne zaman, ne kadar (TL), hangi kategoride, ne için.</summary>
public sealed record Expense(string Id, DateTime When, decimal Amount, string Category, string Note);

/// <summary>Bir dönemin harcama özeti: toplam ve kategoriler (büyükten küçüğe).</summary>
public sealed record ExpenseSummary(decimal Total, int Count, IReadOnlyList<(string Category, decimal Amount)> Categories);

/// <summary>
/// Harcama kaydı: "kahveye 80 lira harcadım", "bu ay ne kadar harcadım", "aylık bütçem 15 bin lira". Kategori
/// söylenenden tahmin edilir; aylık bütçe aşılmaya yaklaşınca uyarılır. D:\Friday\data\expenses.json'da durur.
/// </summary>
public sealed class ExpenseBook
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Kategori → söylenişlerdeki kökler (sadeleşmiş: ı→i, ş→s…).</summary>
    private static readonly (string Category, string[] Stems)[] Categories =
    [
        ("Yeme-içme", ["kahve", "cay", "yemek", "yemeg", "restoran", "lokanta", "kafe", "cafe", "pizza", "burger", "doner", "kebap", "kebab", "tatli", "pasta", "simit", "ogle", "aksam yemeg", "kahvalti", "starbucks", "getir yemek", "yemeksepeti", "dondurma", "icecek", "bira"]),
        ("Market", ["market", "migros", "bim", "a101", "sok", "carrefour", "manav", "kasap", "firin", "ekmek", "sut", "bakkal", "alisveris"]),
        ("Ulaşım", ["taksi", "benzin", "akaryakit", "mazot", "otobus", "metro", "dolmus", "istanbulkart", "kart", "ucak", "bilet", "otopark", "kopru", "otoyol", "uber", "bitaksi", "araba", "servis", "yakit"]),
        ("Faturalar", ["fatura", "elektrik", "dogalgaz", "dogal gaz", "su faturas", "internet", "telefon", "aidat", "abonelik", "gsm", "turkcell", "vodafone", "turk telekom"]),
        ("Kira", ["kira"]),
        ("Eğlence", ["sinema", "netflix", "spotify", "oyun", "steam", "konser", "tiyatro", "youtube", "disney", "playstation", "xbox", "mac bilet"]),
        ("Sağlık", ["ilac", "eczane", "doktor", "hastane", "dis", "muayene", "vitamin", "spor salonu", "gym"]),
        ("Giyim", ["giyim", "kiyafet", "ayakkabi", "tisort", "pantolon", "mont", "ceket", "elbise", "gomlek", "zara", "lcw", "defacto"]),
        ("Eğitim", ["kitap", "kurs", "egitim", "okul", "ders", "udemy", "kirtasiye", "defter", "kalem"]),
        ("Ev", ["ev", "mobilya", "ikea", "temizlik", "deterjan", "tamir", "tadilat", "elektronik", "beyaz esya"]),
        ("Hediye", ["hediye", "cicek", "dogum gunu"])
    ];

    private readonly string _file;
    private readonly object _gate = new();
    private readonly ExpenseData _data;

    public ExpenseBook(string file)
    {
        _file = file;
        try
        {
            _data = File.Exists(file) ? JsonSerializer.Deserialize<ExpenseData>(File.ReadAllText(file), Options) ?? new ExpenseData() : new ExpenseData();
        }
        catch (JsonException)
        {
            _data = new ExpenseData();
        }
    }

    public event EventHandler? Changed;

    /// <summary>Aylık bütçe (TL); 0 ise bütçe yok.</summary>
    public decimal MonthlyBudget
    {
        get
        {
            lock (_gate)
            {
                return _data.MonthlyBudget;
            }
        }
        set
        {
            lock (_gate)
            {
                _data.MonthlyBudget = Math.Max(0, value);
                Save();
            }
        }
    }

    public IReadOnlyList<Expense> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _data.Items];
            }
        }
    }

    public Expense Add(decimal amount, string note, DateTime when, string? category = null)
    {
        var clean = CleanNote(note);
        var expense = new Expense(Guid.NewGuid().ToString("N")[..10], when, Math.Round(amount, 2), category ?? Guess(clean), clean);
        lock (_gate)
        {
            _data.Items.Add(expense);
            Save();
        }

        return expense;
    }

    /// <summary>Son eklenen harcamayı siler; yoksa null.</summary>
    public Expense? RemoveLast()
    {
        lock (_gate)
        {
            if (_data.Items.Count == 0)
            {
                return null;
            }

            var last = _data.Items.MaxBy(item => item.When)!;
            _data.Items.Remove(last);
            Save();
            return last;
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            if (_data.Items.RemoveAll(item => item.Id == id) == 0)
            {
                return false;
            }

            Save();
            return true;
        }
    }

    public IReadOnlyList<Expense> Between(DateTime from, DateTime to)
    {
        lock (_gate)
        {
            return [.. _data.Items.Where(item => item.When >= from && item.When < to).OrderBy(item => item.When)];
        }
    }

    public ExpenseSummary Summary(DateTime from, DateTime to, string? category = null)
    {
        var items = Between(from, to).Where(item => category is null || item.Category == category).ToList();
        var groups = items.GroupBy(item => item.Category)
            .Select(group => (group.Key, group.Sum(item => item.Amount)))
            .OrderByDescending(group => group.Item2)
            .ToList();
        return new ExpenseSummary(items.Sum(item => item.Amount), items.Count, groups);
    }

    /// <summary>"bugun", "dun", "hafta", "ay", "gecen_ay" → [başlangıç, bitiş) ve okunuşu.</summary>
    public static (DateTime From, DateTime To, string Label) Period(string period, DateTime now)
    {
        var today = now.Date;
        var month = new DateTime(today.Year, today.Month, 1);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return period switch
        {
            "bugun" => (today, today.AddDays(1), "Bugün"),
            "dun" => (today.AddDays(-1), today, "Dün"),
            "hafta" => (monday, today.AddDays(1), "Bu hafta"),
            "gecen_ay" => (month.AddMonths(-1), month, "Geçen ay"),
            _ => (month, month.AddMonths(1), "Bu ay")
        };
    }

    /// <summary>Söylenen kategori adı ya da harcama sözcüğünden kategori ("kahveye" → Yeme-içme); bulunamazsa "Diğer".</summary>
    public static string Guess(string text)
    {
        var key = Simple(text);
        if (key.Length == 0)
        {
            return "Diğer";
        }

        foreach (var (category, stems) in Categories)
        {
            if (Simple(category) == key || stems.Any(stem => key.Contains(stem, StringComparison.Ordinal)))
            {
                return category;
            }
        }

        return "Diğer";
    }

    /// <summary>Söylenen sözcük bilinen bir kategoriye düşüyorsa onun adı; yoksa null ("kahveye" → Yeme-içme).</summary>
    public static string? KnownCategory(string text) => Guess(text) is var category && category != "Diğer" ? category : null;

    /// <summary>"kahveye" → "kahve", "markete" → "market", "telefon faturasına" → "telefon faturası", "Migros'a" → "Migros".</summary>
    public static string CleanNote(string note)
    {
        var text = note.Trim().TrimEnd('.', '!', ',');
        if (text.Length == 0)
        {
            return "";
        }

        var apostrophe = text.LastIndexOfAny(['\'', '’']);
        if (apostrophe > 0 && text.Length - apostrophe <= 4)
        {
            return text[..apostrophe];
        }

        static bool Vowel(char letter) => "aeıioöuü".Contains(letter, StringComparison.Ordinal);
        var lower = text.ToLower(Turkish);
        if (lower.Length > 5 && (lower.EndsWith("sına", StringComparison.Ordinal) || lower.EndsWith("sine", StringComparison.Ordinal)
            || lower.EndsWith("suna", StringComparison.Ordinal) || lower.EndsWith("süne", StringComparison.Ordinal)))
        {
            return text[..^2];
        }

        if (lower.Length > 4 && (lower.EndsWith("ya", StringComparison.Ordinal) || lower.EndsWith("ye", StringComparison.Ordinal)) && Vowel(lower[^3]))
        {
            return text[..^2];
        }

        // Yalın hali de a/e ile biten sözcükler var ("kahve", "araba", "kira"); ek yalnızca kalan bilinen bir köke
        // denk geliyorsa atılır: "markete" → "market", "yemeğe" → "yemek", ama "kahve" ve "araba" olduğu gibi kalır.
        static string? Known(string candidate) => KnownCategory(candidate) is null ? null : candidate;
        if (lower.Length > 4 && (lower.EndsWith("ğa", StringComparison.Ordinal) || lower.EndsWith("ğe", StringComparison.Ordinal)))
        {
            return Known(text[..^2] + "k") ?? text;
        }

        if (lower.Length > 4 && (lower.EndsWith("ba", StringComparison.Ordinal) || lower.EndsWith("be", StringComparison.Ordinal))
            && Known(text[..^2] + "p") is { } softened)
        {
            return softened;
        }

        if (lower.Length > 4 && (lower.EndsWith("ca", StringComparison.Ordinal) || lower.EndsWith("ce", StringComparison.Ordinal)) && Vowel(lower[^3])
            && Known(text[..^2] + "ç") is { } hard)
        {
            return hard;
        }

        return lower.Length > 3 && (lower[^1] == 'a' || lower[^1] == 'e') && !Vowel(lower[^2]) && Known(text[..^1]) is { } stem ? stem : text;
    }

    /// <summary>"1.250,50 TL" biçiminde tutar.</summary>
    public static string Money(decimal amount) =>
        amount.ToString(amount == Math.Round(amount) ? "N0" : "N2", Turkish) + " TL";

    /// <summary>Konuşmada okunacak tutar: "1.250 lira", "80,5 lira".</summary>
    public static string Spoken(decimal amount) =>
        amount.ToString(amount == Math.Round(amount) ? "N0" : "0.##", Turkish) + " lira";

    /// <summary>Excel'in açabileceği CSV (noktalı virgülle ayrılmış, UTF-8 BOM'lu).</summary>
    public int ExportCsv(string path)
    {
        var items = Items.OrderBy(item => item.When).ToList();
        var builder = new StringBuilder("Tarih;Saat;Tutar (TL);Kategori;Açıklama\n");
        foreach (var item in items)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{item.When:yyyy-MM-dd};{item.When:HH:mm};{item.Amount.ToString("0.00", Turkish)};{item.Category};{item.Note.Replace(';', ',')}\n");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return items.Count;
    }

    private static string Simple(string text) =>
        text.ToLower(Turkish).Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c').Trim();

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_data, Options));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ExpenseData
    {
        public decimal MonthlyBudget { get; set; }

        public List<Expense> Items { get; set; } = [];
    }
}
