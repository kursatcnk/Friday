using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Metin kısaltmaları ("imzamı yaz") ve gün sonu günlük sorusu.</summary>
public static partial class QuickIntents
{
    /// <summary>
    /// "imza kısaltması: Saygılarımla, Kürşat" → ("imza", "Saygılarımla, Kürşat"). Metin büyük-küçük harfiyle korunur;
    /// bu yüzden küçültülmemiş cümleye bakılır.
    /// </summary>
    public static (string Name, string Text)? SnippetDefinition(string text)
    {
        var original = AddressAnyCase().Replace(text.Trim(), "").Trim();
        var match = SnippetDefine().Match(original);
        return match.Success && match.Groups["v"].Value.Trim().Length > 0
            ? (SnippetKey(match.Groups["k"].Value.Trim()), match.Groups["v"].Value.Trim())
            : null;
    }

    /// <summary>"IBAN" → "iban" (kısaltma, Türkçe ı'ya çevrilmez), "İmza" → "imza".</summary>
    private static string SnippetKey(string raw) => raw.Any(char.IsLower) ? raw.ToLower(Turkish) : raw.ToLowerInvariant();

    /// <summary>"Bunu imza olarak kaydet", "panodakini adres kısaltması yap" → kısaltma adı.</summary>
    private static QuickCommand? Snippets(string sentence)
    {
        if (SnippetFromClipboard().Match(sentence) is { Success: true } clip)
        {
            return Command("kisaltma", ("islem", "panodan"), ("ad", clip.Groups["k"].Value.Trim()));
        }

        if (SnippetList().IsMatch(sentence))
        {
            return Command("kisaltma", ("islem", "listele"));
        }

        if (SnippetDelete().Match(sentence) is { Success: true } delete)
        {
            return Command("kisaltma", ("islem", "sil"), ("ad", delete.Groups["k"].Value.Trim()));
        }

        if (ClipboardImageRead().IsMatch(sentence))
        {
            return Command("pano_gorseli", ("islem", "oku"));
        }

        if (ClipboardImageAsk().IsMatch(sentence))
        {
            return Command("pano_gorseli", ("islem", "acikla"), ("soru", sentence));
        }

        if (JournalPrompt().IsMatch(sentence))
        {
            return Command("gunluk_sorusu");
        }

        return null;
    }

    /// <summary>"Şimdi değil", "sonra", "boş ver": serbest cevap bekleyen soruyu geçmek.</summary>
    public static bool IsDecline(string text) => Decline().IsMatch(Normalize(text));

    [GeneratedRegex(@"^(?:şimdi değil|sonra|daha sonra|bugün değil|boş ver|boşver|geç|atla|yazma|istemiyorum|anlatmak istemiyorum)$")]
    private static partial Regex Decline();

    /// <summary>"İmzamı yaz", "IBAN'ımı yapıştır": öyle bir kısaltma varsa yazılır; yoksa soru dil modeline gider.</summary>
    private static QuickCommand? SnippetUse(string sentence) =>
        SnippetType().Match(sentence) is { Success: true } use
            ? new QuickCommand("kisaltma", new Dictionary<string, string> { ["islem"] = "kullan", ["ad"] = use.Groups["k"].Value.Trim() }, Tentative: true)
            : null;

    // "imza kısaltması: Saygılarımla, Kürşat", "kısaltma ekle adres: Atatürk Cad. No 5"
    [GeneratedRegex(@"^(?:(?<k>[\p{L}\d][\p{L}\d \-]{0,28}?) kısaltması(?: olarak)?(?: kaydet| ekle| olsun)?|kısaltma ekle[:,]?\s+(?<k>[\p{L}\d][\p{L}\d \-]{0,28}?))\s*[:=]\s*(?<v>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SnippetDefine();

    [GeneratedRegex(@"^(?:panodakini|kopyaladığımı|bunu|panodaki metni) (?<k>[\p{L}\d \-]{2,30}?) (?:kısaltması )?olarak (?:kaydet|sakla|ekle)$|^(?:panodakini|kopyaladığımı) (?<k>[\p{L}\d \-]{2,30}?) kısaltması yap$")]
    private static partial Regex SnippetFromClipboard();

    [GeneratedRegex(@"^(?:kısaltmalarım|metin kısaltmalarım|kayıtlı kısaltmalar)(?: neler| ne| var mı)?$|^kısaltmalarımı (?:göster|söyle|oku)$")]
    private static partial Regex SnippetList();

    [GeneratedRegex(@"^(?<k>[\p{L}\d \-]{2,30}?) kısaltmasını (?:sil|kaldır)$")]
    private static partial Regex SnippetDelete();

    // "imzamı yaz", "adresimi yapıştır", "iban'ımı yaz", "e-posta imzamı gir"
    [GeneratedRegex(@"^(?<k>[\p{L}\d][\p{L}\d \-]{1,30}?)(?:['’]?(?:ımı|imi|umu|ümü|mı|mi|mu|mü|yı|yi|yu|yü|ı|i|u|ü))? (?:yaz|yapıştır|gir)$")]
    private static partial Regex SnippetType();

    // "panodaki resmi oku", "kopyaladığım görseldeki yazıyı çıkar", "ekran alıntısındaki metni kopyala"
    [GeneratedRegex(@"^(?:panodaki|kopyaladığım|bu) (?:resm|görsel|ekran alıntısı|ekran görüntüsü|fotoğraf)\p{L}*(?:ndeki|deki|daki|teki|taki)? ?(?:yazıyı|metni|yazıları)? ?(?:oku|çıkar|kopyala|al|metne çevir|yazıya çevir)$")]
    private static partial Regex ClipboardImageRead();

    // "panodaki resimde ne var", "kopyaladığım görseli açıkla"
    [GeneratedRegex(@"^(?:panodaki|kopyaladığım) (?:resim|görsel|ekran alıntısı|ekran görüntüsü|fotoğraf)\p{L}* (?:ne var|ne yazıyor|ne anlatıyor|açıkla|anlat|ne diyor)$|^(?:panodaki|kopyaladığım) (?:resm|görsel|ekran alıntısın|ekran görüntüsün|fotoğraf)\p{L}* (?:açıkla|anlat|incele|yorumla)$")]
    private static partial Regex ClipboardImageAsk();

    [GeneratedRegex(@"^(?:günümü|bugünü) (?:günlüğe )?(?:yazalım|anlatayım|kaydedelim)$|^günlük sorusu(?: sor)?$|^günümü sor$")]
    private static partial Regex JournalPrompt();
}
