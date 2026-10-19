using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

public sealed record MemoryItem(string Id, string Text, DateTimeOffset Created);

/// <summary>
/// Uzun süreli hafıza: "Unutma, arabanın muayenesi Kasım'da" gibi bilgiler D:\Friday\data\memory.json'a yazılır ve
/// her soruda dil modeline verilir. Dosya yalnızca bilgisayarda durur.
/// </summary>
public sealed class MemoryStore
{
    private const int MaxItems = 200;
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

            item = new MemoryItem(Guid.NewGuid().ToString("N")[..8], clean, _clock.GetUtcNow());
            _items.Add(item);
            if (_items.Count > MaxItems)
            {
                _items.RemoveAt(0);
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return item;
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
}
