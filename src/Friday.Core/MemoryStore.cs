using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <param name="Source">Bilgi başka bir yapay zekâdan aktarıldıysa onun adı (ör. "ChatGPT"); Friday'in kendisi için null.</param>
public sealed record MemoryItem(string Id, string Text, DateTimeOffset Created, string? Source = null);

/// <summary>
/// Uzun süreli hafıza: "Unutma, arabanın muayenesi Kasım'da" gibi bilgiler D:\Friday\data\memory.json'a yazılır ve
/// her soruda dil modeline verilir. Dosya yalnızca bilgisayarda durur.
/// </summary>
public sealed partial class MemoryStore
{
    /// <summary>Hafıza soruya göre arandığı için büyük olabilir (eskiden en yeni 80'i görülürdü).</summary>
    private const int MaxItems = 2000;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _file;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private List<MemoryItem> _items;

    public MemoryStore(string file, TimeProvider? clock = null)
    {
        _file = file;
        _clock = clock ?? TimeProvider.System;
        _items = File.Exists(file)
            ? JsonSerializer.Deserialize<List<MemoryItem>>(File.ReadAllText(file), Options) ?? []
            : [];
    }

    public event EventHandler? Changed;

    public IReadOnlyList<MemoryItem> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public MemoryItem Remember(string text)
    {
        var clean = text.Trim();
        if (clean.Length == 0)
        {
            throw new ArgumentException("Hatırlanacak bilgi boş.", nameof(text));
        }

        MemoryItem item;
        lock (_gate)
        {
            var existing = _items.FirstOrDefault(entry => Normalize(entry.Text) == Normalize(clean));
            if (existing is not null)
            {
                return existing;
            }

            // Aynı bilginin biraz farklı söylenişi (ya da düzeltilmiş hali) yeni kayıt açmaz; eskisinin yerine geçer.
            var index = _items.FindIndex(entry => SameFact(entry.Text, clean));
            if (index >= 0)
            {
                item = _items[index] with { Text = clean, Created = _clock.GetUtcNow() };
                _items[index] = item;
            }
            else
            {
                item = new MemoryItem(Guid.NewGuid().ToString("N")[..8], clean, _clock.GetUtcNow());
                _items.Add(item);
                if (_items.Count > MaxItems)
                {
                    _items.RemoveAt(0);
                }
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    /// <summary>Başka bir yapay zekâdan gelen bilgileri ekler; zaten bilinenler atlanır.</summary>
    /// <returns>Eklenen bilgi sayısı.</returns>
    public int Import(IEnumerable<string> facts, string source)
    {
        var added = 0;
        lock (_gate)
        {
            var known = _items.Select(item => MemoryImport.Key(item.Text)).ToHashSet(StringComparer.Ordinal);
            foreach (var fact in facts.Select(fact => fact.Trim()).Where(fact => fact.Length > 0))
            {
                if (known.Add(MemoryImport.Key(fact)))
                {
                    _items.Add(new MemoryItem(Guid.NewGuid().ToString("N")[..8], fact, _clock.GetUtcNow(), source));
                    added++;
                }
            }

            while (_items.Count > MaxItems)
            {
                _items.RemoveAt(0);
            }

            if (added > 0)
            {
                Save();
            }
        }

        if (added > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return added;
    }

    /// <summary>Okunur Markdown: yedek almak ya da başka bir uygulamaya taşımak için.</summary>
    public string ToMarkdown(string assistantName)
    {
        var items = Items;
        var text = new System.Text.StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# {assistantName}'in hafızası");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{_clock.GetLocalNow().ToString("d MMMM yyyy HH:mm", Turkish)} · {items.Count} bilgi");
        text.AppendLine();
        foreach (var item in items)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {item.Text}");
        }

        return text.ToString();
    }

    /// <summary>
    /// Başka bir yapay zekâya yapıştırılacak hazır mesaj: bilgileri tanıtır ve kaydetmesini ister. ChatGPT, Claude ve
    /// Gemini bu biçimdeki bir mesajı hafızalarına alabiliyor.
    /// </summary>
    public string ToPortablePrompt()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("Aşağıdakiler benim hakkımda bilmeni istediğim şeyler. Lütfen hepsini hafızana kaydet ve bundan sonraki konuşmalarımızda kullan:");
        text.AppendLine();
        foreach (var item in Items)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {item.Text}");
        }

        return text.ToString();
    }

    /// <summary>Metninde aranan ifade geçen bilgileri siler; silinenleri döndürür.</summary>
    public IReadOnlyList<MemoryItem> Forget(string query)
    {
        var needle = Normalize(query);
        if (needle.Length == 0)
        {
            return [];
        }

        List<MemoryItem> removed;
        lock (_gate)
        {
            removed = _items.Where(item => item.Id == query.Trim() || Normalize(item.Text).Contains(needle, StringComparison.Ordinal)).ToList();
            if (removed.Count == 0)
            {
                return [];
            }

            _items = _items.Except(removed).ToList();
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            if (_items.RemoveAll(item => item.Id == id) == 0)
            {
                return;
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_items, Options));
    }

    private static string Normalize(string text) => text.Trim().TrimEnd('.').ToLower(Turkish);

    /// <summary>
    /// İki bilgi aynı şeyi mi söylüyor? Sözcüklerin büyük kısmı ortaksa evet: "muayene 20 Kasım'da" ile "muayene 25
    /// Kasım'da" aynı bilginin düzeltilmesidir; "kızının doğum günü 3 Mart" ile "oğlunun doğum günü 5 Nisan" değil.
    /// </summary>
    public static bool SameFact(string first, string second)
    {
        var a = Words(first);
        var b = Words(second);
        if (a.Count < 3 || b.Count < 3)
        {
            return false;
        }

        var common = a.Count(b.Contains);
        var union = a.Count + b.Count - common;
        return common / (double)union >= 0.6;
    }

    private static HashSet<string> Words(string text) =>
        [.. WordSplit().Split(text.ToLower(Turkish)).Where(word => word.Length > 1 || (word.Length == 1 && char.IsDigit(word[0])))];

    [GeneratedRegex(@"[^\p{L}\d]+")]
    private static partial Regex WordSplit();
}
