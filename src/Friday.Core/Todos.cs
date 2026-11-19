using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

/// <summary>Yapılacaklar listesinde bir iş; tamamlanınca zamanı yazılır. Son tarihi ve önemi isteğe bağlıdır.</summary>
public sealed record TodoItem(string Id, string Text, DateTime Created, DateTime? Done = null, DateOnly? Due = null, bool Important = false)
{
    public bool IsDone => Done is not null;
}

/// <summary>
/// Yapılacaklar listesi (D:\Friday\data\todos.json): "yapılacaklara ekle: faturayı öde", "faturayı ödedim", "yapılacaklarım
/// neler". Tamamlananlar bir gün daha görünür, sonra listeden düşer.
/// </summary>
public sealed class TodoStore
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _file;
    private readonly object _gate = new();
    private List<TodoItem> _items;

    public TodoStore(string file)
    {
        _file = file;
        try
        {
            _items = File.Exists(file) ? JsonSerializer.Deserialize<List<TodoItem>>(File.ReadAllText(file), Options) ?? [] : [];
        }
        catch (JsonException)
        {
            _items = [];
        }
    }

    public event EventHandler? Changed;

    /// <summary>Açık işler (eskiden yeniye) ve son bir günde tamamlananlar.</summary>
    /// <summary>Bu tarihten sonra tamamlanan işlerin sayısı.</summary>
    public int DoneSince(DateTime since)
    {
        lock (_gate)
        {
            return _items.Count(item => item.Done >= since);
        }
    }

    public IReadOnlyList<TodoItem> Visible(DateTime now)
    {
        lock (_gate)
        {
            return [.. Ordered(_items.Where(item => !item.IsDone))
                .Concat(_items.Where(item => item.Done is { } done && now - done < TimeSpan.FromDays(1)).OrderByDescending(item => item.Done))];
        }
    }

    public IReadOnlyList<TodoItem> Open
    {
        get
        {
            lock (_gate)
            {
                return [.. Ordered(_items.Where(item => !item.IsDone))];
            }
        }
    }

    /// <param name="due">Son tarih; var olan işe söylenirse güncellenir.</param>
    public TodoItem Add(string text, DateTime now, DateOnly? due = null, bool important = false)
    {
        var clean = TurkishGrammar.Capitalize(text.Trim().TrimEnd('.'));
        TodoItem item;
        lock (_gate)
        {
            var existing = _items.FirstOrDefault(existing => !existing.IsDone && string.Equals(Key(existing.Text), Key(clean), StringComparison.Ordinal));
            if (existing is null)
            {
                item = new TodoItem(Guid.NewGuid().ToString("N")[..8], clean, now, Due: due, Important: important);
                _items.Add(item);
                Save();
            }
            else if ((due is not null && existing.Due != due) || (important && !existing.Important))
            {
                item = existing with { Due = due ?? existing.Due, Important = existing.Important || important };
                _items[_items.IndexOf(existing)] = item;
                Save();
            }
            else
            {
                item = existing;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    /// <summary>Açık işlerden metni en iyi eşleşeni bulur ("faturayı ödedim" → "Faturayı öde").</summary>
    public TodoItem? Find(string query)
    {
        var words = Words(query);
        if (words.Count == 0)
        {
            return null;
        }

        lock (_gate)
        {
            return _items.Where(item => !item.IsDone)
                .Select(item => (Item: item, Score: Words(item.Text).Count(word => words.Any(other => Similar(word, other)))))
                .Where(entry => entry.Score > 0 && entry.Score * 2 >= Math.Min(words.Count, Words(entry.Item.Text).Count))
                .OrderByDescending(entry => entry.Score)
                .Select(entry => entry.Item)
                .FirstOrDefault();
        }
    }

    public bool Complete(string id, DateTime now) => Update(id, item => item with { Done = now });

    public bool Reopen(string id) => Update(id, item => item with { Done = null });

    public bool Remove(string id)
    {
        lock (_gate)
        {
            if (_items.RemoveAll(item => item.Id == id) == 0)
            {
                return false;
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Tamamlananları siler; silinen sayıyı döndürür.</summary>
    public int ClearDone()
    {
        int removed;
        lock (_gate)
        {
            removed = _items.RemoveAll(item => item.IsDone);
            if (removed > 0)
            {
                Save();
            }
        }

        if (removed > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    private bool Update(string id, Func<TodoItem, TodoItem> change)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }

            _items[index] = change(_items[index]);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Önce son tarihi yakın (gecikmiş en önde), sonra önemli, sonra eklenme sırası.</summary>
    private static IEnumerable<TodoItem> Ordered(IEnumerable<TodoItem> items) =>
        items.OrderBy(item => item.Due ?? DateOnly.MaxValue).ThenByDescending(item => item.Important).ThenBy(item => item.Created);

    /// <summary>Ekleri farklı olsa da aynı kelime sayılır: "faturayı", "fatura", "faturaları" → ilk 5 harf.</summary>
    private static bool Similar(string first, string second) =>
        first == second || (first.Length >= 4 && second.Length >= 4 && first[..Math.Min(5, first.Length)] == second[..Math.Min(5, second.Length)]);

    /// <summary>Türkçe büyük/küçük harf farkı gözetmeyen karşılaştırma anahtarı (I/ı, İ/i).</summary>
    private static string Key(string text) => text.ToLower(Turkish);

    private static List<string> Words(string text) =>
        [.. text.ToLower(Turkish).Split([' ', ',', '.', '\'', '’', ':', ';', '!', '?'], StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length > 2)];

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_items, Options));
    }
}
