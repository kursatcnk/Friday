using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Seçili metin işlemleri, dikte, pencere düzeni ve haberler.</summary>
public static partial class QuickIntents
{
    /// <summary>"Şunu yaz: yarın görüşürüz" → yazılacak metin (büyük/küçük harf ve noktalama korunur); değilse null.</summary>
    public static string? TypeRequest(string text)
    {
        var original = AddressAnyCase().Replace(text.Trim(), "").Trim();
        var match = TypeRequestPattern().Match(original.ToLower(Turkish));
        return match.Success && match.Groups["x"].Length > 0
            ? original.Substring(match.Groups["x"].Index, match.Groups["x"].Length).Trim()
            : null;
    }

    /// <summary>Dikteyi bitiren sözler: "dikte bitti", "dikteyi bitir", "tamam bitti".</summary>
    public static bool IsDictationEnd(string text) => DictationEnd().IsMatch(Normalize(text));

    /// <summary>Dikte sırasında tuşa karşılık gelen sözler: "yeni satır" → Enter, "geri al" → Ctrl+Z, "sil" → son kelime.</summary>
    public static string? DictationKey(string text) => Normalize(text) switch
    {
        "yeni satır" or "alt satır" or "satır başı" or "enter" => "enter",
        "geri al" => "geri_al",
        "son kelimeyi sil" or "kelimeyi sil" => "kelime_sil",
        _ => null
    };

    private static QuickCommand? TypeText(string text) => TypeRequest(text) is { } words ? Command("yaz", ("metin", words)) : null;

    private static QuickCommand? Selection(string sentence)
    {
        var match = SelectionRequest().Match(sentence);
        if (!match.Success)
        {
            return null;
        }

        var operation = match.Groups["op"].Value switch
        {
            "özetle" => "ozetle",
            "çevir" => "cevir",
            "düzelt" => "duzelt",
            "resmileştir" => "resmilestir",
            "maddele" => "maddele",
            "açıkla" => "acikla",
            _ => "kisalt"
        };
        var language = match.Groups["lang"].Success ? TurkishGrammar.Capitalize(match.Groups["lang"].Value) : "";
        return Command("secili", ("islem", operation), ("dil", language));
    }

    private static QuickCommand? Dictation(string sentence) => DictationStart().IsMatch(sentence) ? Command("dikte") : null;

    private static QuickCommand? WindowLayout(string sentence)
    {
        if (SnapRequest().Match(sentence) is { Success: true } snap)
        {
            return Command("pencere", ("islem", snap.Groups["d"].Value == "sola" ? "sol" : "sag"));
        }

        var action = sentence switch
        {
            _ when MaximizeRequest().IsMatch(sentence) => "buyut",
            _ when MinimizeRequest().IsMatch(sentence) => "kucult",
            _ when SplitRequest().IsMatch(sentence) => "bol",
            _ => ""
        };
        return action.Length > 0 ? Command("pencere", ("islem", action)) : null;
    }

    private static QuickCommand? News(string sentence)
    {
        var match = NewsRequest().Match(sentence);
        return match.Success ? Command("haber", ("kategori", match.Groups["c"].Success ? match.Groups["c"].Value : "gundem")) : null;
    }

    [GeneratedRegex(@"^(?:şunu|bunu|şöyle|şunları) yaz[:,]?\s+(?<x>.+)$|^yaz:\s*(?<x>.+)$", RegexOptions.Singleline)]
    private static partial Regex TypeRequestPattern();

    // "seçili metni özetle", "seçili yazıyı ingilizceye çevir", "seçili paragrafı resmileştir"
    [GeneratedRegex(@"^seçili (?:metni|yazıyı|paragrafı|kısmı|cümleyi|e-postayı|maili) (?:(?<lang>\p{L}+?)(?:ye|ya|e|a) )?(?<op>özetle|çevir|düzelt|resmileştir|maddele|açıkla|kısalt)$")]
    private static partial Regex SelectionRequest();

    [GeneratedRegex(@"^(?:dikte|yazdırma|yazı) modu(?:nu)? aç$|^dikte (?:et|aç|modu)$|^(?:ben )?söyleyeyim sen yaz$|^ben söyleyeceğim sen yaz$")]
    private static partial Regex DictationStart();

    [GeneratedRegex(@"^(?:dikte|dikteyi|yazmayı|yazdırmayı|dikte modunu) (?:bitir|kapat|durdur)$|^(?:dikte|yazma) bitti$|^tamam bitti$|^bitti$")]
    private static partial Regex DictationEnd();

    [GeneratedRegex(@"^(?:bu )?pencere(?:yi)? (?<d>sola|sağa) (?:yasla|al|kaydır|yapıştır|daya)$")]
    private static partial Regex SnapRequest();

    [GeneratedRegex(@"^(?:bu )?pencere(?:yi)? (?:tam ekran yap|büyüt|ekranı kaplasın|tam ekran)$|^tam ekran yap$")]
    private static partial Regex MaximizeRequest();

    [GeneratedRegex(@"^(?:bu )?pencere(?:yi)? (?:küçült|simge durumuna küçült|gizle|aşağı al)$")]
    private static partial Regex MinimizeRequest();

    [GeneratedRegex(@"^ekranı (?:ikiye böl|bölüştür|ikiye ayır)$|^(?:son )?iki pencereyi yan yana (?:koy|getir|diz|al)$")]
    private static partial Regex SplitRequest();

    [GeneratedRegex(@"^(?:bugünün |bugünkü |dünkü |son )?(?:haberleri|haberler|gündem|son dakika)(?: (?:ne|neler|oku|söyle|nedir))?$|^gündemde ne var$|^(?:bugün )?neler oluyor$|^(?<c>spor|ekonomi|teknoloji|dünya) haberleri(?: (?:ne|neler|oku|söyle))?$")]
    private static partial Regex NewsRequest();
}
