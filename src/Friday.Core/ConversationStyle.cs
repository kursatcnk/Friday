using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Sorunun türüne göre küçük kararlar: düşünme adımı gerekir mi?</summary>
public static partial class ConversationStyle
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Araç gerektirmeyen konuşma: selamlaşma, teşekkür, hal hatır, espri ve fıkra, Friday'in kendisi ya da kullanıcı
    /// hakkında bildikleri. Model bunlarda doğrudan cevap verir; düşünme adımı 1–6 sn boşa gidiyordu. Bir istek ya da
    /// bilgi sorusu içeren her şey (ör. "merhaba, hava nasıl") sohbet sayılmaz.
    /// </summary>
    public static bool IsSmallTalk(string text)
    {
        var sentence = Spaces().Replace(Punctuation().Replace(text.ToLower(Turkish), " "), " ").Trim();
        if (sentence.Length is 0 or > 90)
        {
            return false;
        }

        return Greeting().IsMatch(sentence)
               || ((Playful().IsMatch(sentence) || AboutUser().IsMatch(sentence)) && !NeedsTool().IsMatch(sentence));
    }

    /// <summary>
    /// Yapay zekâ kalıbı kapanış cümlesi mi? ("Başka bir şeye ihtiyacın var mı?", "Size nasıl yardımcı olabilirim?",
    /// "Yardımcı olmak için buradayım.") Talimata rağmen model ara sıra ekliyor; okunmadan ayıklanır.
    /// </summary>
    public static bool IsFiller(string sentence) => Filler().IsMatch(sentence.Trim().ToLower(Turkish));

    /// <summary>Metinden kalıp cümleleri çıkarır; hepsi kalıpsa metni olduğu gibi bırakır.</summary>
    public static string RemoveFiller(string text)
    {
        var sentences = SentenceEnd().Split(text.Trim()).Where(sentence => sentence.Length > 0).ToList();
        var kept = sentences.Where(sentence => !IsFiller(sentence)).ToList();
        return kept.Count == 0 || kept.Count == sentences.Count ? text.Trim() : string.Join(" ", kept);
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"^(?:peki,? |o zaman,? |şimdi,? |bugün )?(?:(?:sana|size) (?:başka )?(?:nasıl|ne konuda|hangi konuda) yardımcı olabilirim|başka (?:bir )?(?:şey(?:e|ler)?|konu(?:da|ya)?|sorun)(?: için)? (?:ihtiyacın|ihtiyacınız|yardımcı|var mı|sormak)|(?:başka )?bir şeye ihtiyacın(?:ız)? (?:var mı|olursa)|(?:sana |size )?yardımcı olmak için (?:her zaman )?buradayım|(?:her zaman )?emrindeyim|başka bir isteğin var mı)[^.!?]*[.!?]?$")]
    private static partial Regex Filler();

    [GeneratedRegex(@"[.,!?;:'’""]+")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(?:(?:hey |selam |merhaba )?(?:friday|jarvis) ?)?(?:merhaba|selam|selamlar|hey|günaydın|iyi akşamlar|iyi geceler|iyi günler|tünaydın|naber|ne haber|nasılsın|iyi misin|nasıl gidiyor|keyfin nasıl|teşekkürler|teşekkür ederim|çok teşekkürler|sağ ol|sağol|sağ olasın|eyvallah|eline sağlık|harikasın|süpersin|aferin|çok iyi|güzel|tamam|anladım|görüşürüz|hoşça kal|kendine iyi bak|kimsin|sen kimsin|adın ne|ne yapabilirsin|neler yapabilirsin|seni seviyorum|canım sıkıldı|sıkıldım|yoruldum|mutluyum|üzgünüm)(?: (?:friday|jarvis|dostum|kanka|ya|bakalım|o zaman))*(?: (?:sen nasılsın|nasılsın|naber))?$")]
    private static partial Regex Greeting();

    [GeneratedRegex(@"\b(?:espri|fıkra|şaka|bilmece|tekerleme|kafiye|şiir|hikaye|masal|beni güldür|motive et|moral ver)")]
    private static partial Regex Playful();

    [GeneratedRegex(@"\b(?:benim hakkımda|hakkımda ne|beni ne kadar tanıyorsun|beni tanıyor musun|neleri hatırlıyorsun|ne hatırlıyorsun|bana kendini tanıt|kendini tanıt)")]
    private static partial Regex AboutUser();

    /// <summary>Espri isteğinde bile bir iş varsa (ör. "espri yap ve not defterini aç") düşünme açık kalır.</summary>
    [GeneratedRegex(@"\b(?:aç|kapat|ara|bul|oku|yaz|kaydet|hatırla|unut|hava|saat|ekran|pano|dosya|internet|haber|kur|çevir|özetle)\b")]
    private static partial Regex NeedsTool();
}
