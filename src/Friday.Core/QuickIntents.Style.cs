using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>"Daha kısa konuş", "cevaplarını daha ayrıntılı ver", "normal konuş" (bkz. <see cref="ReplyStyle"/>).</summary>
public static partial class QuickIntents
{
    private static QuickCommand? Style(string sentence) =>
        BriefPlease().IsMatch(sentence) ? Command("uslup", ("islem", "kisa"))
        : DetailPlease().IsMatch(sentence) ? Command("uslup", ("islem", "ayrintili"))
        : NormalPlease().IsMatch(sentence) ? Command("uslup", ("islem", "normal"))
        : StyleQuestion().IsMatch(sentence) ? Command("uslup", ("islem", "durum"))
        : null;

    // "Daha detaylı anlat" tek seferlik bir istektir (son cevabı aç); kalıcı tercih "konuş", "cevap ver", "bundan sonra" ile söylenir.
    [GeneratedRegex(@"^(?:bundan sonra |artık )?(?:daha kısa konuş|kısa konuş|cevaplarını kısa tut|(?:daha )?kısa cevap(?:lar)? ver|çok uzun konuşuyorsun|lafı uzatma|bu kadar uzun anlatma|az konuş)$")]
    private static partial Regex BriefPlease();

    [GeneratedRegex(@"^(?:bundan sonra |artık )?(?:cevaplarını (?:daha )?(?:detaylı|ayrıntılı) ver|(?:daha )?(?:detaylı|ayrıntılı) cevaplar ver|(?:daha )?(?:detaylı|ayrıntılı) konuş|uzun uzun anlatabilirsin|cevaplarını kısa kesme)$")]
    private static partial Regex DetailPlease();

    [GeneratedRegex(@"^(?:normal konuş|eskisi gibi konuş|cevap uzunluğunu sıfırla|konuşma uzunluğunu sıfırla)$")]
    private static partial Regex NormalPlease();

    [GeneratedRegex(@"^(?:neden (?:bu kadar )?kısa konuşuyorsun|cevap uzunluğun ne|nasıl konuşuyorsun|cevaplarının uzunluğunu nasıl ayarlıyorsun)$")]
    private static partial Regex StyleQuestion();
}
