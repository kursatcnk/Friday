using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Her yerde arama, kaçırılan bildirimler, seçili kelime, ekrandaki yazı ve panodakini kaydetme.</summary>
public static partial class QuickIntents
{
    private static QuickCommand? Recall(string sentence)
    {
        foreach (var pattern in new[] { SearchAll(), SearchAbout() })
        {
            if (pattern.Match(sentence) is { Success: true } search && search.Groups["q"].Value.Trim().Trim(':', ',').Trim() is { Length: > 1 } query)
            {
                return Command("her_yerde_ara", ("sorgu", query));
            }
        }

        if (NudgeSilence().IsMatch(sentence))
        {
            return Command("durtme", ("islem", "sustur"));
        }

        if (NudgeReset().IsMatch(sentence))
        {
            return Command("durtme", ("islem", "ac"));
        }

        if (NudgeList().IsMatch(sentence))
        {
            return Command("durtme", ("islem", "listele"));
        }

        if (NextActionQuestion().IsMatch(sentence))
        {
            return Command("ne_yapayim");
        }

        if (ReceiptRequest().IsMatch(sentence))
        {
            return Command("fis");
        }

        if (WeeklyReportRequest().IsMatch(sentence))
        {
            return Command("haftalik_rapor");
        }

        if (MissedQuestion().IsMatch(sentence))
        {
            return Command("kacirdiklarim");
        }

        if (SelectedWord().IsMatch(sentence))
        {
            return Command("kelime_cevir", ("islem", "secili"));
        }

        if (SpokenWord().Match(sentence) is { Success: true } word)
        {
            return Command("kelime_cevir", ("islem", "ekle"), ("kelime", word.Groups["w"].Value.Trim()));
        }

        if (ScreenText().IsMatch(sentence))
        {
            return Command("ekran_metni");
        }

        if (ClipboardToTodo().IsMatch(sentence))
        {
            return Command("pano_kaydet", ("islem", "is"));
        }

        if (ClipboardToNote().IsMatch(sentence))
        {
            return Command("pano_kaydet", ("islem", "not"));
        }

        return ClipboardToList().Match(sentence) is { Success: true } list
            ? Command("pano_kaydet", ("islem", "liste"), ("ad", list.Groups["l"].Value.Trim()))
            : null;
    }

    // "her yerde kira ara", "kayıtlarımda ara: doktor", "friday'de ahmet bul"
    [GeneratedRegex(@"^(?:her yerde|kayıtlarımda|tüm kayıtlarımda|bütün kayıtlarımda|friday['’]?(?:de|da)) (?:ara[:,]?\s+(?<q>.+)|(?<q>.+?) (?:ara|bul|geçiyor mu))$")]
    private static partial Regex SearchAll();

    // "kira hakkında ne kaydetmiştim", "doktor ile ilgili ne not almıştım"
    [GeneratedRegex(@"^(?<q>.+?) (?:hakkında|ile ilgili|konusunda) (?:ne (?:kaydetmiştim|yazmıştım|not almıştım|demiştim|kaydettim)|neler (?:kaydetmişim|yazmışım)|kayıtlarımda ne var)$")]
    private static partial Regex SearchAbout();

    // "bunu bir daha sorma", "bu hatırlatmayı kapat", "artık söyleme"
    [GeneratedRegex(@"^(?:bunu|bunları|bu (?:hatırlatmayı|uyarıyı|öneriyi|soruyu))? ?(?:bir daha|artık|bundan sonra) (?:sorma|söyleme|hatırlatma|önerme|deme)$|^bu (?:hatırlatmayı|uyarıyı|öneriyi|soruyu) (?:kapat|sustur|kaldır)$")]
    private static partial Regex NudgeSilence();

    [GeneratedRegex(@"^(?:kapattığın|susturduğun|kapattığım|susturduğum) (?:hatırlatmaları|uyarıları|önerileri|şeyleri) (?:aç|geri aç|sıfırla|geri getir)$")]
    private static partial Regex NudgeReset();

    [GeneratedRegex(@"^(?:kapattığın|susturduğun|kapattığım|susturduğum) (?:hatırlatmalar|uyarılar|öneriler|şeyler)(?: neler| ne)?$|^neleri (?:susturdun|kapattın)$")]
    private static partial Regex NudgeList();

    [GeneratedRegex(@"^(?:şimdi |şu an |peki )?ne yap(?:ayım|malıyım|sam|sam iyi olur)$|^sırada ne var$|^(?:bir )?öneri(?:n)? (?:var mı|ne|ver)$|^(?:bir )?öneri ver$|^neyle başlayayım$|^nereden başlayayım$")]
    private static partial Regex NextActionQuestion();

    [GeneratedRegex(@"^(?:panodaki|kopyaladığım|bu) (?:fişi|faturayı|fiş görselini) (?:harcamaya|harcamalara|harcamalarıma) (?:ekle|kaydet|işle)$|^fişi (?:harcamaya |harcamalara )?(?:kaydet|ekle|işle)$|^panodaki fişi (?:kaydet|işle|oku)$")]
    private static partial Regex ReceiptRequest();

    [GeneratedRegex(@"^haftalık rapor(?:u|umu)? (?:kaydet|hazırla|çıkar|oluştur|dosyaya kaydet)$|^haftamı (?:dosyaya )?(?:kaydet|raporla)$|^bu haftanın raporunu (?:kaydet|hazırla|çıkar)$")]
    private static partial Regex WeeklyReportRequest();

    [GeneratedRegex(@"^(?:ben )?yokken ne (?:dedin|oldu|söyledin)$|^(?:kaçırdıklarım|kaçırdığım bildirimler|son bildirimler|son bildirimlerim)(?: neler| ne)?$|^(?:az önce|demin) ne (?:dedin|söyledin)(?: kendiliğinden)?$|^son hatırlatmalar(?:ı)? (?:neydi|ne idi|tekrar et)$")]
    private static partial Regex MissedQuestion();

    [GeneratedRegex(@"^(?:seçili|seçtiğim|bu) (?:kelimeyi|ifadeyi|sözcüğü) (?:desteye|desteme|kelimelerime|kartlarıma|kelime desteme) ekle$|^seçileni (?:desteye|desteme|kelimelerime) ekle$")]
    private static partial Regex SelectedWord();

    // "resilient kelimesini desteye ekle" (İngilizce kelime, anlamı yerel modelden)
    [GeneratedRegex(@"^(?<w>[a-z][a-z'\- ]{1,30}?) (?:kelimesini|sözcüğünü|ifadesini) (?:desteye|desteme|kelimelerime|kartlarıma) ekle$")]
    private static partial Regex SpokenWord();

    [GeneratedRegex(@"^(?:ekrandaki|bu penceredeki|penceredeki|ekranda(?:ki)? gördüğüm) (?:yazıyı|metni|yazıları) (?:kopyala|çıkar|panoya al|panoya kopyala|al)$")]
    private static partial Regex ScreenText();

    [GeneratedRegex(@"^(?:panodakini|panodaki metni|kopyaladığımı|kopyaladığım metni) (?:yapılacaklara|yapılacaklarıma|görevlerime|yapılacaklar listesine) ekle$")]
    private static partial Regex ClipboardToTodo();

    [GeneratedRegex(@"^(?:panodakini|panodaki metni|kopyaladığımı|kopyaladığım metni) (?:not al|not et|notlarıma ekle|nota ekle|notlara ekle)$")]
    private static partial Regex ClipboardToNote();

    [GeneratedRegex(@"^(?:panodakini|kopyaladığımı) (?<l>[\p{L} ]{2,30}?) listesine ekle$")]
    private static partial Regex ClipboardToList();
}
