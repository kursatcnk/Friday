using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Hızlı yolun yanlış anladığı sözler. Kullanıcı "yanlış anladın" deyince son söz buraya yazılır; bir dahaki sefere
/// bir kalıba uysa da komut sanılmaz, dil modeline gider. D:\Friday\data\corrections.json içinde durur.
/// </summary>
public sealed partial class CorrectionBook(string file)
{
    public const int Capacity = 300;

    /// <summary>Düzeltmeyle öğrenilmeyen araçlar: "geri al", "tekrar et" gibi sözler hep komut kalmalı.</summary>
    public static readonly IReadOnlySet<string> Unlearnable = new HashSet<string>(StringComparer.Ordinal)
    {
        "geri_al", "tekrar", "sohbet", "kapanis", "onay", "serbest_cevap", "kontrol_cevap", "dikte", "duzeltme"
    };

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private List<string>? _phrases;

    public IReadOnlyList<string> Phrases
    {
        get
        {
            lock (_gate)
            {
                return [.. Load()];
            }
        }
    }

    /// <summary>"Yanlış anladın", "hayır, X demek istedim" gibi bir düzeltmeyse kullanıcının asıl söylediği (yoksa ""); düzeltme değilse null.</summary>
    public static string? Meant(string text)
    {
        var lower = text.Trim().ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        var match = Wrong().Match(lower);
        if (!match.Success)
        {
            match = Instead().Match(lower);
            if (!match.Success)
            {
                return null;
            }
        }

        var rest = Ending().Replace(match.Groups["rest"].Value, "");
        return rest.Trim().Trim(',', '.', '!', ':', ';', '?').Trim();
    }

    public bool Contains(string text)
    {
        var key = Key(text);
        if (key.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            return Load().Contains(key, StringComparer.Ordinal);
        }
    }

    /// <summary>Sözü öğrenir; zaten biliniyorsa ya da boşsa false.</summary>
    public bool Add(string text)
    {
        var key = Key(text);
        if (key.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            var phrases = Load();
            if (phrases.Contains(key, StringComparer.Ordinal))
            {
                return false;
            }

            phrases.Add(key);
            if (phrases.Count > Capacity)
            {
                phrases.RemoveAt(0);
            }

            Save(phrases);
            return true;
        }
    }

    /// <summary>Bütün düzeltmeleri unutur; kaç tane olduğunu döndürür.</summary>
    public int Clear()
    {
        lock (_gate)
        {
            var phrases = Load();
            var count = phrases.Count;
            phrases.Clear();
            Save(phrases);
            return count;
        }
    }

    /// <summary>Karşılaştırma anahtarı: hızlı yolun gördüğü biçim (hitap, nezaket sözü, noktalama atılmış).</summary>
    public static string Key(string text) => QuickIntents.Normalize(text);

    private List<string> Load()
    {
        if (_phrases is not null)
        {
            return _phrases;
        }

        try
        {
            _phrases = File.Exists(file) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file), Options) ?? [] : [];
        }
        catch (JsonException)
        {
            _phrases = [];
        }

        return _phrases;
    }

    private void Save(List<string> phrases)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(phrases, Options));
    }

    [GeneratedRegex(@"^(?:(?:hayır|yok|olmadı|friday|ya)[,.!]?\s+)*(?:beni\s+)?(?:yanlış\s+anladın|yanlış\s+anlaşıldı|yanlış\s+duydun|yanlış\s+yaptın|öyle\s+demedim|ben\s+öyle\s+demedim|(?:onu|bunu|ben\s+onu|ben\s+bunu)\s+(?:demedim|kastetmedim|istemedim|söylemedim))(?:\s*[,.!:;]\s*|\s+|$)(?<rest>.*)$")]
    private static partial Regex Wrong();

    [GeneratedRegex(@"^(?:hayır|yok)[,.!]?\s+(?<rest>.+?)\s+(?:demek\s+istedim|demek\s+istiyorum|dedim|diyorum|demiştim)[.!]?$")]
    private static partial Regex Instead();

    [GeneratedRegex(@"\s+(?:demek\s+istedim|demek\s+istiyorum|dedim|diyorum|demiştim)[.!]?$")]
    private static partial Regex Ending();
}
