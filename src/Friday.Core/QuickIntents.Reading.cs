using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Okuduklarım (<see cref="ReadingLog"/>) ve yazma köprüsü (<see cref="Writing"/>) için kapılar. Asıl iş araçlarda:
/// buradaki kalıplar yalnızca isteği modeli beklemeden doğru araca götürür.
/// </summary>
public static partial class QuickIntents
{
    private static QuickCommand? Reading(string sentence)
    {
        if (ReadingForget().Match(sentence) is { Success: true } forget)
        {
            return Command("okuduklarim", ("islem", "unut"), ("ad", forget.Groups["all"].Success ? "hepsi" : "bu"));
        }

        if (ReadingList().Match(sentence) is { Success: true } list)
        {
            return Command("okuduklarim", ("islem", "liste"), ("zaman", list.Groups["t"].Value));
        }

        if (ReadingDiff().IsMatch(sentence))
        {
            return Command("okuduklarim", ("islem", "fark"));
        }

        // "Okuduğum" her zaman belgedir; "açtığım", "baktığım" ise ancak bir belgeden söz ediliyorsa ("az önce açtığım
        // pencereyi kapat" okuma değildir).
        if (!ReadVerb().IsMatch(sentence) && !(SeenVerb().IsMatch(sentence) && DocumentNoun().IsMatch(sentence)))
        {
            return null;
        }

        // "Geçen hafta okuduğum raporu aç" / "dün baktığım pdf'i bul" → yeniden aç; geri kalanı belgeye dair bir soru.
        return ReadingOpen().Match(sentence) is { Success: true } open
            ? Command("okuduklarim", ("islem", "ac"), ("ad", open.Groups["x"].Value))
            : Command("okuduklarim", ("islem", "sor"), ("soru", sentence));
    }

    /// <summary>
    /// "Buna cevap yaz: perşembe olur ama üçten sonra", "şunu İngilizce yaz: toplantıyı yarına alabilir miyiz",
    /// "daha kibar yap", "kısalt", "şunu da ekle: …". Söylenen metin birkaç cümle olabildiği için özgün metne bakılır.
    /// </summary>
    private static QuickCommand? WritingRequest(string text)
    {
        var original = AddressAnyCase().Replace(text.Trim(), "").Trim();
        // Kalıplar küçük harfle aranır (Türkçe "İ" büyük-küçük harf duyarsız aramada tutmaz); söylenen metin özgün hâliyle
        // alınır. Türkçe küçültme harf sayısını değiştirmez.
        var lower = original.ToLower(Turkish);
        string Said(Group group) => original.Substring(group.Index, group.Length).Trim();
        if (WriteReply().Match(lower) is { Success: true } reply && Said(reply.Groups["x"]).Length > 1)
        {
            return Command("yazi", ("islem", "cevap"), ("icerik", Said(reply.Groups["x"])), ("dil", Language(reply.Groups["l"].Value)));
        }

        if (WriteIn().Match(lower) is { Success: true } write && Said(write.Groups["x"]).Length > 1)
        {
            return Command("yazi", ("islem", "yaz"), ("icerik", Said(write.Groups["x"])), ("dil", Language(write.Groups["l"].Value)));
        }

        var sentence = lower.TrimEnd('.', '!', '?', ' ');
        return WriteRevise().IsMatch(sentence) || WriteAddition().IsMatch(lower)
            ? Command("yazi", ("islem", "duzelt"), ("icerik", original))
            : null;
    }

    /// <summary>"ingilizce" → "İngilizce"; boşsa boş.</summary>
    private static string Language(string said) =>
        said.Trim().Length == 0 ? "" : TurkishGrammar.Capitalize(LanguageSuffix().Replace(said.Trim().ToLower(Turkish), ""));

    [GeneratedRegex(@"^(?:okuduklarımı|okuduğum belgeleri|okuma geçmişimi) (?<all>unut|sil|temizle)$|^bu belgeyi (?:okuduklarımdan )?(?:unut|sil)$")]
    private static partial Regex ReadingForget();

    [GeneratedRegex(@"^(?:(?<t>bugün|dün|bu hafta|geçen hafta|bu ay|geçen ay|son \d+ gün(?:de)?) )?(?:neler|ne|hangi belgeleri|hangi belgelere|hangi dosyaları|hangi dosyalara|hangi pdf'?leri) (?:okudum|açtım|baktım|inceledim)$|^(?:son )?okuduklarım(?: neler)?$|^okuduğum belgeler(?:i göster)?$")]
    private static partial Regex ReadingList();

    [GeneratedRegex(@"^(?:(?:bunun|bu belgenin|bu sürümün|bu dosyanın) )?(?:öncekinden |önceki sürümden |önceki sürümüyle |eskisinden |eski sürümden )?(?:farkı|farkları) (?:ne|neler|nedir)$|^(?:bu (?:belgede|sürümde|dosyada)|burada) (?:neler|ne) değiş(?:miş|ti|mış)$|^(?:neler|ne) değiş(?:miş|ti)$|^önceki sürümle karşılaştır$")]
    private static partial Regex ReadingDiff();

    [GeneratedRegex(@"(?:^| )(?:okuduğum|incelediğim|okumuştum|okuduğumuz|okumuş olduğum)(?: |$)")]
    private static partial Regex ReadVerb();

    [GeneratedRegex(@"(?:^| )(?:açtığım|baktığım|açmıştım|bakmıştım|indirdiğim)(?: |$)")]
    private static partial Regex SeenVerb();

    [GeneratedRegex(@"belge|dosya|pdf|sözleşme|kontrat|rapor|makale|teklif|fatura|doküman|döküman|sunum|word|excel|metin|yazı|kitap|şartname|poliçe|tez|ödev|cv|özgeçmiş")]
    private static partial Regex DocumentNoun();

    [GeneratedRegex(@"^(?<x>.+?) (?:aç|tekrar aç|yeniden aç|bul|göster|getir)$")]
    private static partial Regex ReadingOpen();

    [GeneratedRegex(@"^(?:buna|bu (?:maile|e-?postaya|mesaja|yazıya)|(?:bu )?(?:maile|mesaja)) (?:(?<l>ingilizce|almanca|fransızca|ispanyolca|italyanca|rusça|arapça|türkçe) )?(?:şöyle |şunu diye |bir )?(?:cevap|yanıt) (?:yaz|ver|hazırla)[:,]?\s+(?<x>.+)$|^(?:buna|bu (?:maile|e-?postaya|mesaja)) (?<x>.+?) diye (?:(?<l>ingilizce|almanca|fransızca) )?(?:cevap|yanıt) (?:yaz|ver)$|^(?:cevap|yanıt) yaz[:,]\s+(?<x>.+)$", RegexOptions.Singleline)]
    private static partial Regex WriteReply();

    [GeneratedRegex(@"^(?:şunu |bunu )?(?<l>ingilizce|almanca|fransızca|ispanyolca|italyanca|rusça|arapça) (?:olarak |bir )?(?:yaz|mail yaz|e-?posta yaz|mesaj yaz|mesaj hazırla)[:,]?\s+(?<x>.+)$|^(?:şunu|bunu) (?<l>ingilizce|almanca|fransızca|ispanyolca|italyanca|rusça|arapça)(?:'?(?:ye|ya)) çevir(?:ip panoya koy)?[:,]\s*(?<x>.+)$", RegexOptions.Singleline)]
    private static partial Regex WriteIn();

    [GeneratedRegex(@"^(?:biraz |daha )*(?:kibar|resmi|samimi|kısa|uzun|sıcak|net|profesyonel|nazik|ciddi)(?: bir şekilde| bir dille)? (?:yap|yaz|olsun)$|^(?:biraz |bunu |taslağı )?(?:kısalt|uzat|toparla|sadeleştir)$|^imzamı ekle$|^(?:selamı|selamlamayı|hitabı) (?:kaldır|değiştir)$")]
    private static partial Regex WriteRevise();

    [GeneratedRegex(@"^(?:şunu da ekle|şunu ekle|şunu çıkar|şunu değiştir|şunu da yaz)[:,]\s*.+$", RegexOptions.Singleline)]
    private static partial Regex WriteAddition();

    [GeneratedRegex(@"(?:'?(?:ye|ya|de|da))$")]
    private static partial Regex LanguageSuffix();
}
