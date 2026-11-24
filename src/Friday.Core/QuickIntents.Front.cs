using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Önündeki belgeye dair kısa istekler (bkz. <see cref="FrontDocument"/>): "bu ne", "bunu açıkla", "bunu Türkçeye çevir",
/// "bu sayfayı özetle", "devamını çevir". Belgeye dair serbest sorular ("bu sözleşmede depozito ne kadar") dil modeline
/// gider; model aynı aracı soruyla çağırır.
/// </summary>
public static partial class QuickIntents
{
    private static QuickCommand? Front(string sentence)
    {
        if (FrontTranslate().IsMatch(sentence))
        {
            return Command("onumdeki", ("islem", "cevir"));
        }

        if (FrontContinue().IsMatch(sentence))
        {
            return Command("onumdeki", ("islem", "devam"));
        }

        if (FrontSummary().IsMatch(sentence))
        {
            return Command("onumdeki", ("islem", "ozetle"));
        }

        return FrontExplain().IsMatch(sentence) ? Command("onumdeki", ("islem", "acikla")) : null;
    }

    // "bunu türkçeye çevir", "bu sayfayı çevir", "bu pdf'i türkçeye çevirir misin", "türkçeye çevir"
    [GeneratedRegex(@"^(?:(?:bunu|şunu|bu (?:sayfayı|belgeyi|pdf'?i|dosyayı|metni|yazıyı|makaleyi|maili|e-postayı))(?: bana)?(?: türkçe'?ye)?|türkçe'?ye) çevir(?:ir misin|ebilir misin|sene)?$")]
    private static partial Regex FrontTranslate();

    [GeneratedRegex(@"^(?:devamını|sonrasını|sonraki kısmı|kalanını) (?:çevir|oku|açıkla)$")]
    private static partial Regex FrontContinue();

    [GeneratedRegex(@"^(?:bunu|bu (?:sayfayı|belgeyi|pdf'?i|dosyayı|metni|yazıyı|makaleyi|maili|e-postayı)) özetle(?:r misin)?$")]
    private static partial Regex FrontSummary();

    // "bu ne", "bu ne demek", "bunu açıkla", "burada ne yazıyor", "bu sayfa ne diyor", "bu pdf ne anlatıyor"
    [GeneratedRegex(@"^(?:bu ne|bu nedir|bu ne demek|bu ne anlama geliyor|bunu açıkla|bunu bana açıkla|bunu anlat|burada ne yazıyor|burada ne diyor|ekranda ne yazıyor|bu (?:sayfa|belge|pdf|dosya|metin|yazı|makale|mail|e-posta) ne (?:diyor|anlatıyor|yazıyor)|bu (?:sayfa|belge|pdf|dosya|metin|yazı|makale) ne hakkında)$")]
    private static partial Regex FrontExplain();
}
