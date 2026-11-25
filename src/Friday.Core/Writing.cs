using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Yazma köprüsünün bir taslağı: cevap verilen yazı, kullanıcının söylediği ve yazılan metin.</summary>
public sealed record WritingDraft(string Context, string Intent, string Language, string Text, string Turkish, DateTime At);

/// <summary>Dil modelinin taslak çıktısı.</summary>
public sealed record WritingOutput(string Language, string Text, string Turkish);

/// <summary>
/// Yazma köprüsü: kullanıcı Türkçe ne demek istediğini söyler, Friday önündeki e-postaya ya da mesaja bakarak gönderilecek
/// metni (çoğunlukla İngilizce) yazar ve panoya koyar. "Daha kibar yap", "kısalt", "şunu da ekle" son taslağı değiştirir.
/// Bu sınıf istemi kurar ve modelin cevabını ayrıştırır; kullanıcının düzeltmelerinden üslubunu da öğrenir
/// (<see cref="WritingStyle"/>).
/// </summary>
public static partial class Writing
{
    /// <summary>Bir taslak bu kadar süre "düzeltilebilir" sayılır.</summary>
    public static readonly TimeSpan DraftLife = TimeSpan.FromMinutes(20);

    /// <summary>Taslak isteminin talimatı.</summary>
    public static string Instruction(string language, string userName, string signature, IReadOnlyList<string> style) =>
        "You are Friday, writing a message on behalf of the user" + (userName.Length > 0 ? $" ({userName})" : "") + ". " +
        "The user told you in Turkish what they want to say. Write the message they will send.\n" +
        "Rules:\n" +
        $"- Write it in {(language.Length > 0 ? language : "the same language as the message being replied to (if there is none, English)")}.\n" +
        "- Say what the user said, naturally and fluently; do not add facts, promises, dates or details the user did not give.\n" +
        "- Keep names, numbers, dates and amounts exactly.\n" +
        "- When replying, address the points of the message the user is answering, greet the sender by name if the message shows it, " +
        "and match its formality.\n" +
        (signature.Length > 0 ? $"- For an e-mail, end with this signature exactly: {signature}\n" : userName.Length > 0 ? $"- For an e-mail, sign with the name {userName}.\n" : "") +
        string.Concat(style.Select(hint => "- " + hint + "\n")) +
        "- No subject line, no markdown, no comments.\n" +
        "Answer in exactly this format:\n" +
        "LANG: <the language you wrote in, named in Turkish, e.g. İngilizce>\n" +
        "TEXT:\n<the message>\n" +
        "TR: <one or two Turkish sentences saying what the message says>";

    /// <summary>Taslak isteminin kullanıcı kısmı.</summary>
    public static string Request(string context, string intent)
    {
        var request = new StringBuilder();
        if (context.Trim().Length > 0)
        {
            request.Append("The message I am replying to:\n<<<\n").Append(context.Trim()).Append("\n>>>\n\n");
        }

        return request.Append("What I want to say (in my words): ").Append(intent.Trim()).ToString();
    }

    /// <summary>Son taslağı değiştirme isteği.</summary>
    public static string Revision(WritingDraft draft, string change) =>
        (draft.Context.Length > 0 ? "The message I am replying to:\n<<<\n" + draft.Context + "\n>>>\n\n" : "") +
        "Your current draft:\n<<<\n" + draft.Text + "\n>>>\n\n" +
        "Change the draft as I ask, keep everything else the same: " + change.Trim();

    /// <summary>"LANG: … TEXT: … TR: …" çıktısını ayrıştırır; biçim tutmazsa metnin tamamı taslak sayılır.</summary>
    public static WritingOutput Parse(string output, string fallbackLanguage)
    {
        var text = output.Trim().Trim('`').Trim();
        var language = LanguageLine().Match(text) is { Success: true } lang ? lang.Groups["l"].Value.Trim() : fallbackLanguage;
        var body = text;
        if (TextStart().Match(text) is { Success: true } start)
        {
            body = text[(start.Index + start.Length)..];
        }
        else if (LanguageLine().Match(text) is { Success: true } only)
        {
            body = text[(only.Index + only.Length)..];
        }

        var turkish = "";
        if (TurkishLine().Match(body) is { Success: true } tr)
        {
            turkish = tr.Groups["t"].Value.Trim();
            body = body[..tr.Index];
        }

        return new WritingOutput(LanguageName(language.Length > 0 ? language : "İngilizce"), body.Trim(), turkish);
    }

    /// <summary>Model dili kendi adıyla yazabiliyor ("Deutsch", "English"): Türkçe adı.</summary>
    public static string LanguageName(string language)
    {
        var said = language.Trim().Trim('.', '*', '"').ToLowerInvariant();
        return said switch
        {
            "english" or "ingilizce" or "i̇ngilizce" or "en" => "İngilizce",
            "deutsch" or "german" or "almanca" or "de" => "Almanca",
            "français" or "francais" or "french" or "fransızca" or "fr" => "Fransızca",
            "español" or "espanol" or "spanish" or "ispanyolca" or "i̇spanyolca" or "es" => "İspanyolca",
            "italiano" or "italian" or "italyanca" or "i̇talyanca" or "it" => "İtalyanca",
            "русский" or "russian" or "rusça" or "ru" => "Rusça",
            "العربية" or "arabic" or "arapça" or "ar" => "Arapça",
            "türkçe" or "turkish" or "tr" => "Türkçe",
            _ => language.Trim()
        };
    }

    /// <summary>Düzeltme isteğinin türü (üslup öğrenmek için): kisa, uzun, resmi, samimi, kibar ya da boş.</summary>
    public static string Kind(string change)
    {
        var said = change.ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        return said switch
        {
            _ when Shorter().IsMatch(said) => "kisa",
            _ when Longer().IsMatch(said) => "uzun",
            _ when Formal().IsMatch(said) => "resmi",
            _ when Casual().IsMatch(said) => "samimi",
            _ when Polite().IsMatch(said) => "kibar",
            _ => ""
        };
    }

    [GeneratedRegex(@"^\s*LANG\s*:\s*(?<l>[^\n]*)\n?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LanguageLine();

    [GeneratedRegex(@"^\s*TEXT\s*:\s*\n?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TextStart();

    [GeneratedRegex(@"^\s*TR\s*:\s*(?<t>[\s\S]*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TurkishLine();

    [GeneratedRegex(@"kısa|kısalt|özet|uzatma|az yaz")]
    private static partial Regex Shorter();

    [GeneratedRegex(@"uzun|uzat|detay|ayrıntı")]
    private static partial Regex Longer();

    [GeneratedRegex(@"resmi|profesyonel|ciddi")]
    private static partial Regex Formal();

    [GeneratedRegex(@"samimi|sıcak|rahat|günlük dil|arkadaşça")]
    private static partial Regex Casual();

    [GeneratedRegex(@"kibar|nazik|yumuşak|incelikli")]
    private static partial Regex Polite();
}

/// <summary>
/// Kullanıcının yazı üslubu: taslaklarda sık istediği düzeltmeler ("kısalt" üç kez denmişse bundan sonra ilk taslak zaten
/// kısa yazılır). Karşıt istekler birbirini götürür (kısa/uzun, resmi/samimi). data\writing-style.json.
/// </summary>
public sealed class WritingStyle
{
    private const int Threshold = 2;

    private static readonly Dictionary<string, string> Opposite = new()
    {
        ["kisa"] = "uzun", ["uzun"] = "kisa", ["resmi"] = "samimi", ["samimi"] = "resmi"
    };

    private static readonly Dictionary<string, string> Hints = new()
    {
        ["kisa"] = "The user prefers short, to-the-point messages; keep it brief.",
        ["uzun"] = "The user prefers fuller messages with a bit more detail.",
        ["resmi"] = "The user prefers a formal, professional tone.",
        ["samimi"] = "The user prefers a warm, casual tone.",
        ["kibar"] = "The user wants messages to sound especially polite and considerate."
    };

    private readonly string _file;
    private readonly Dictionary<string, int> _counts;

    public WritingStyle(string file)
    {
        _file = file;
        try
        {
            _counts = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(file)) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _counts = [];
        }
    }

    /// <summary>İlk taslağa eklenecek üslup notları.</summary>
    public IReadOnlyList<string> Notes
    {
        get
        {
            lock (_counts)
            {
                return [.. _counts.Where(pair => pair.Value >= Threshold && Hints.ContainsKey(pair.Key)).Select(pair => Hints[pair.Key])];
            }
        }
    }

    /// <summary>Öğrenilmiş tercihler, Türkçe ("kısa", "resmi").</summary>
    public IReadOnlyList<string> Learned
    {
        get
        {
            lock (_counts)
            {
                return [.. _counts.Where(pair => pair.Value >= Threshold).Select(pair => pair.Key switch
                {
                    "kisa" => "kısa", "uzun" => "ayrıntılı", "resmi" => "resmi", "samimi" => "samimi", _ => "kibar"
                })];
            }
        }
    }

    /// <summary>Öğrenilmiş tek bir tercihi ("kısa", "resmi") unutur; öğrenilmişse true.</summary>
    public bool Unlearn(string learned)
    {
        var key = learned switch { "kısa" => "kisa", "ayrıntılı" => "uzun", "resmi" => "resmi", "samimi" => "samimi", "kibar" => "kibar", _ => learned };
        lock (_counts)
        {
            if (!_counts.Remove(key))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file) ?? ".");
                File.WriteAllText(_file, JsonSerializer.Serialize(_counts));
            }
            catch (IOException)
            {
                // Bir dahaki öğrenmede yeniden yazılır.
            }

            return true;
        }
    }

    /// <summary>Bir düzeltme isteği öğrenilir; üslup notu yeni oluştuysa true.</summary>
    public bool Learn(string kind)
    {
        if (kind.Length == 0)
        {
            return false;
        }

        bool reached;
        lock (_counts)
        {
            _counts[kind] = _counts.GetValueOrDefault(kind) + 1;
            if (Opposite.TryGetValue(kind, out var opposite) && _counts.GetValueOrDefault(opposite) > 0)
            {
                _counts[opposite]--;
            }

            reached = _counts[kind] == Threshold;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file) ?? ".");
                File.WriteAllText(_file, JsonSerializer.Serialize(_counts));
            }
            catch (IOException)
            {
                // Bir dahaki düzeltmede yeniden yazılır.
            }
        }

        return reached;
    }

    public void Reset()
    {
        lock (_counts)
        {
            _counts.Clear();
            try
            {
                File.Delete(_file);
            }
            catch (IOException)
            {
                // Silinemezse boş sayaçlar bir dahaki öğrenmede üzerine yazılır.
            }
        }
    }
}
