using System.Text;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Model aracı çağırmak yerine çağrıyı düz metin olarak yazabiliyor: <c>eposta_taslagi(alici="Ahmet Bey", konu="İzin")</c>
/// ya da Gemma'nın eğitimdeki biçimiyle <c>```tool_code print(default_api.not(metin="…"))```</c>. Ölçümde uzun bir e-posta
/// isteğinde bu metin olduğu gibi okunuyordu. Böyle bir metin tanınır, okunmaz ve gerçek bir araç çağrısına çevrilir.
/// </summary>
public static partial class TextToolCall
{
    /// <summary>Metin (şimdiye kadar gelen kısmıyla) bilinen bir aracın çağrısı gibi mi başlıyor?</summary>
    public static bool Starts(string text, IReadOnlySet<string> names) =>
        Head().Match(Unwrap(text)) is { Success: true } head && Resolve(head.Groups["n"].Value, names) is not null;

    /// <summary>
    /// Şimdiye kadar gelen metin henüz karar verilemeyecek kadar kısa ve bir araç adına benziyor mu ("eposta_tas",
    /// "```tool")? Bu durumda ekrana yazılmaz; birkaç belirteç sonra ya çağrı olduğu anlaşılır ya da normal cevap.
    /// </summary>
    public static bool MaybeStarts(string text) => text.Length < 60 && Partial().IsMatch(Unwrap(text));

    /// <summary>Metnin tamamı tek bir araç çağrısıysa o çağrı; değilse null.</summary>
    public static ToolCall? Parse(string text, IReadOnlySet<string> names)
    {
        var body = Unwrap(text);
        var head = Head().Match(body);
        if (!head.Success || Resolve(head.Groups["n"].Value, names) is not { } name)
        {
            return null;
        }

        var close = body.LastIndexOf(')');
        if (close < head.Length)
        {
            return null;
        }

        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match pair in Argument().Matches(body[head.Length..close]))
        {
            var value = pair.Groups["q"].Success ? Unescape(pair.Groups["q"].Value) : pair.Groups["v"].Value.Trim();
            arguments[pair.Groups["k"].Value] = value;
        }

        return new ToolCall(name, arguments);
    }

    /// <summary>
    /// Yazılan adın gerçek araç adı: tam eşleşme, yoksa tire ve alt çizgi farkı yok sayılarak ("e-posta_taslagi",
    /// "e_posta_taslagi" → "eposta_taslagi"). Ölçümde model uzun e-posta isteğinde aracı bu adlarla metin olarak yazdı ve
    /// çağrı okunup geçildi. Birden çok araca uyuyorsa hiçbiri seçilmez.
    /// </summary>
    private static string? Resolve(string written, IReadOnlySet<string> names)
    {
        if (names.Contains(written))
        {
            return written;
        }

        static string Plain(string name) => name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        var plain = Plain(written);
        var matches = names.Where(name => Plain(name) == plain).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Kod çiti, "tool_code", "print(" ve "default_api." sarmalarını atar.</summary>
    private static string Unwrap(string text)
    {
        var body = text.TrimStart().TrimStart('`').TrimStart();
        foreach (var prefix in new[] { "tool_code", "python", "json" })
        {
            if (body.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                body = body[prefix.Length..].TrimStart();
            }
        }

        if (body.StartsWith("print(", StringComparison.Ordinal))
        {
            body = body["print(".Length..];
        }

        return body.StartsWith("default_api.", StringComparison.Ordinal) ? body["default_api.".Length..] : body;
    }

    private static string Unescape(string value)
    {
        var text = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length)
            {
                index++;
                text.Append(value[index] switch { 'n' => '\n', 't' => '\t', var other => other });
            }
            else
            {
                text.Append(value[index]);
            }
        }

        return text.ToString();
    }

    [GeneratedRegex(@"^(?<n>[a-z][a-z_-]{1,40})\(")]
    private static partial Regex Head();

    [GeneratedRegex(@"^(?:`*|[a-z_-]*)$")]
    private static partial Regex Partial();

    [GeneratedRegex(@"(?<k>[a-z_]+)\s*[=:]\s*(?:""(?<q>(?:[^""\\]|\\.)*)""|'(?<q>(?:[^'\\]|\\.)*)'|(?<v>[^,)]+))")]
    private static partial Regex Argument();
}
