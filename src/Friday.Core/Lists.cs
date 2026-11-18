using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

/// <summary>
/// Adlandırılmış listeler: "market listesine süt ekle", "market listemde ne var", "market listesinden sütü çıkar".
/// D:\Friday\data\lists.json içinde durur.
/// </summary>
public sealed class ListStore(string file)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly object _gate = new();
    private Dictionary<string, List<string>>? _lists;

    public event EventHandler? Changed;

    public IReadOnlyList<string> Names
    {
        get
        {
            lock (_gate)
            {
                return [.. Lists.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key)];
            }
        }
    }

    public IReadOnlyList<string> Items(string list)
    {
        lock (_gate)
        {
            return Lists.TryGetValue(Key(list), out var items) ? [.. items] : [];
        }
    }

    /// <summary>Öğeleri ekler ("süt ve ekmek" → iki öğe); listede olanları tekrar eklemez. Eklenenler döner.</summary>
    public IReadOnlyList<string> Add(string list, string items)
    {
        var added = new List<string>();
        lock (_gate)
        {
            var key = Key(list);
            if (!Lists.TryGetValue(key, out var current))
            {
                Lists[key] = current = [];
            }

            foreach (var item in Split(items))
            {
                if (!current.Any(existing => Same(existing, item)))
                {
                    current.Add(item);
                    added.Add(item);
                }
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>"sütü" → "süt" öğesini bulup çıkarır (ekler gevşek eşleşir).</summary>
    public string? Remove(string list, string item)
    {
        lock (_gate)
        {
            if (!Lists.TryGetValue(Key(list), out var current))
            {
                return null;
            }

            var match = current.FirstOrDefault(existing => Same(existing, item) || Fold(item).StartsWith(Fold(existing), StringComparison.Ordinal));
            if (match is null)
            {
                return null;
            }

            current.Remove(match);
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
            return match;
        }
    }

    public int Clear(string list)
    {
        lock (_gate)
        {
            if (!Lists.Remove(Key(list), out var removed))
            {
                return 0;
            }

            Save();
            Changed?.Invoke(this, EventArgs.Empty);
            return removed.Count;
        }
    }

    public static IReadOnlyList<string> Split(string items) =>
    [
        .. items.Replace(" ve ", ",", StringComparison.Ordinal).Replace(" ile ", ",", StringComparison.Ordinal)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .Select(item => item.TrimEnd('.'))
    ];

    private Dictionary<string, List<string>> Lists => _lists ??= Read();

    private Dictionary<string, List<string>> Read()
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(file), Options) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(Lists, Options));
    }

    private static string Key(string list) => list.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));

    private static bool Same(string a, string b) => Fold(a) == Fold(b);

    private static string Fold(string text) => new(text.ToLower(CultureInfo.GetCultureInfo("tr-TR"))
        .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
        .Where(char.IsLetterOrDigit).ToArray());
}
