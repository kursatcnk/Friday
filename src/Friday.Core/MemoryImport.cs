using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>İçe aktarılabilecek bir bilgi; önizlemede seçilip seçilmemesine kullanıcı karar verir.</summary>
public sealed record MemoryCandidate(string Text, bool AlreadyKnown);

/// <summary>
/// Başka bir yapay zekânın hafızasını Friday'e taşır. ChatGPT'nin "Hafızayı yönet" listesi, Claude'un ya da Gemini'nin
/// "kaydedilen bilgiler"i, düz metin, Markdown madde listesi ya da JSON olabilir; her biçimden tek tek bilgiler çıkarılır.
/// Başlıklar, boş satırlar ve "İşte seninle ilgili bildiklerim" gibi giriş cümleleri atlanır.
/// </summary>
public static partial class MemoryImport
{
    private const int MaxLength = 400;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly string[] TextFields = ["text", "content", "memory", "value", "fact", "Text"];

    /// <summary>Metindeki bilgileri sırayla çıkarır; aynı bilgi iki kez gelmez.</summary>
    public static IReadOnlyList<string> Parse(string text)
    {
        var trimmed = text.Trim().TrimStart('﻿');
        var items = trimmed.StartsWith('[') || trimmed.StartsWith('{') ? FromJson(trimmed) : FromText(trimmed);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return items
            .Select(Clean)
            .Where(item => item.Length >= 8 && item.Length <= MaxLength && !Boilerplate().IsMatch(item))
            .Where(item => seen.Add(Key(item)))
            .ToList();
    }

    /// <summary>Önizleme: Friday'in zaten bildikleri işaretlenir ve varsayılan olarak seçilmez.</summary>
    public static IReadOnlyList<MemoryCandidate> Preview(string text, IEnumerable<MemoryItem> known)
    {
        var existing = known.Select(item => Key(item.Text)).ToHashSet(StringComparer.Ordinal);
        return Parse(text).Select(item => new MemoryCandidate(item, existing.Contains(Key(item)))).ToList();
    }

    /// <summary>Kaynağı metinden ya da dosya adından tahmin eder (önizlemede ve hafıza listesinde gösterilir).</summary>
    public static string GuessSource(string text, string fileName = "")
    {
        var hint = (fileName + " " + text[..Math.Min(text.Length, 2000)]).ToLower(Turkish);
        return hint switch
        {
            _ when hint.Contains("chatgpt", StringComparison.Ordinal) || hint.Contains("openai", StringComparison.Ordinal) => "ChatGPT",
            _ when hint.Contains("claude", StringComparison.Ordinal) || hint.Contains("anthropic", StringComparison.Ordinal) => "Claude",
            _ when hint.Contains("gemini", StringComparison.Ordinal) || hint.Contains("bard", StringComparison.Ordinal) => "Gemini",
            _ when hint.Contains("copilot", StringComparison.Ordinal) => "Copilot",
            _ => "İçe aktarma"
        };
    }

    /// <summary>Karşılaştırma anahtarı: büyük-küçük harf, noktalama ve boşluk farkları önemsizdir.</summary>
    internal static string Key(string text) =>
        Spaces().Replace(NonWord().Replace(text.ToLower(Turkish), " "), " ").Trim();

    private static List<string> FromJson(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            return FromText(json);
        }

        using (document)
        {
            var found = new List<string>();
            Collect(document.RootElement, found);
            return found;
        }
    }

    /// <summary>Dizilerdeki metinleri ve nesnelerin "text/content/memory" alanlarını toplar; iç içe yapılara iner.</summary>
    private static void Collect(JsonElement element, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                found.Add(element.GetString() ?? "");
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, found);
                }

                break;
            case JsonValueKind.Object:
                foreach (var field in TextFields)
                {
                    if (element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        found.Add(value.GetString() ?? "");
                        return;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                    {
                        Collect(property.Value, found);
                    }
                }

                break;
        }
    }

    private static List<string> FromText(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var bulleted = lines.Count(line => Bullet().IsMatch(line)) >= 2;
        var items = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            if (bulleted)
            {
                if (Bullet().IsMatch(line))
                {
                    items.Add(Bullet().Replace(line, ""));
                }
                else if (items.Count > 0 && !line.EndsWith(':') && !EndsSentence(items[^1]))
                {
                    // Madde bir alt satıra taşmış: önceki maddeye eklenir.
                    items[^1] += " " + line;
                }

                continue;
            }

            // Madde işaretsiz metin: her satır, uzun satırlar cümle cümle.
            items.AddRange(line.Length > 160 ? SentenceBreak().Split(line) : [line]);
        }

        return items;
    }

    private static bool EndsSentence(string text) => text.TrimEnd().EndsWith('.') || text.TrimEnd().EndsWith('!') || text.TrimEnd().EndsWith('?');

    private static string Clean(string text)
    {
        var clean = Spaces().Replace(Markdown().Replace(text, ""), " ").Trim().Trim('"', '“', '”');
        return clean.Length == 0 ? clean : char.ToUpper(clean[0], Turkish) + clean[1..];
    }

    [GeneratedRegex(@"^\s*(?:[-*•·–]|\d{1,3}[.)])\s+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\*\*|__|`")]
    private static partial Regex Markdown();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Hafıza listelerinin başındaki ve sonundaki açıklama cümleleri.</summary>
    [GeneratedRegex(@"^(?:here(?:'s| is| are) (?:what|everything|a list)|işte (?:seninle|senin hakkında|hakkında)|bu (?:liste|bilgiler)|aşağıdaki|the following|memories?:?$|saved (?:memories|info)|kaydedilen bilgiler|hafıza:?$|let me know|başka bir şey)", RegexOptions.IgnoreCase)]
    private static partial Regex Boilerplate();
}
