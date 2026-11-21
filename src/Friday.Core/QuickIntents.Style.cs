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
        : WhereWasI().IsMatch(sentence) ? Command("kaldigim_yer")
        : WhySaid().IsMatch(sentence) ? Command("neden")
        : WhatLearned().IsMatch(sentence) ? Command("ogrendiklerin")
        : ShortcutsReset().IsMatch(sentence) ? Command("kisayol_ogrenimi", ("islem", "sifirla"))
        : ShortcutsList().IsMatch(sentence) ? Command("kisayol_ogrenimi", ("islem", "listele"))
        : null;

    [GeneratedRegex(@"^(?:bunu |şunu |onu )?(?:neden|niye|niçin) (?:söyledin|hatırlattın|sordun|dedin|haber verdin)$|^(?:neden|niye) bunu (?:söyledin|hatırlattın|sordun)$")]
    private static partial Regex WhySaid();

    [GeneratedRegex(@"^(?:bu hafta |son zamanlarda |bugüne kadar |benden |benim hakkımda )?(?:neler|ne) öğrendin$|^neler öğrendiğini (?:söyle|anlat)$")]
    private static partial Regex WhatLearned();

    [GeneratedRegex(@"^(?:öğrendiğin )?kısayolları (?:unut|sıfırla|temizle)$")]
    private static partial Regex ShortcutsReset();

    [GeneratedRegex(@"^(?:öğrendiğin kısayollar(?:ı)? (?:neler|ne|say|söyle)|hangi kısayolları öğrendin|neleri düşünmeden yapıyorsun)$")]
    private static partial Regex ShortcutsList();

    /// <summary>"Dün ne yaptım", "geçen salı nasıl geçti": gün anlaşılırsa o günün hikâyesi.</summary>
    private static QuickCommand? PastDayQuestion(string sentence) =>
        PastDay().Match(sentence) is { Success: true } match && DayStory.Parse(match.Groups["d"].Value, DateOnly.FromDateTime(DateTime.Now)) is not null
            ? Command("o_gun", ("gun", match.Groups["d"].Value))
            : null;

    [GeneratedRegex(@"^(?<d>dün|önceki gün|evvelsi gün|dünden önceki gün|\d{1,3} gün önce|(?:geçen (?:hafta )?)?(?:pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar)|\d{1,2} (?:ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık))(?: günü)? (?:ne(?:ler)? yaptım|ne(?:ler)? yapmışım|nasıl geçti|ne(?:yle)? uğraştım|ne yapıyordum|neler oldu|ne yaptığımı (?:söyle|anlat))$")]
    private static partial Regex PastDay();

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
