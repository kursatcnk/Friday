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
