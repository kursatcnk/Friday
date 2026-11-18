using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Araştırma raporu, takvim, İndirilenler'i düzenleme, gün sonu özeti ve evet/hayır onayları.</summary>
public static partial class QuickIntents
{
    /// <summary>Bekleyen bir onaya "evet" mi deniyor? ("evet", "tamam yap", "olur", "onaylıyorum").</summary>
    public static bool IsYes(string text) => YesWords().IsMatch(Normalize(text));

    /// <summary>Bekleyen bir onaya "hayır" mı deniyor? ("hayır", "vazgeç", "iptal").</summary>
    public static bool IsNo(string text) => NoWords().IsMatch(Normalize(text));

    private static QuickCommand? Research(string sentence)
    {
        if (OpenReport().IsMatch(sentence))
        {
            return Command("rapor", ("islem", "ac"));
        }

        var match = ResearchRequest().Match(sentence);
        var topic = match.Success ? match.Groups["x"].Value.Trim() : "";
        // "Bunu internette araştır" bir rapor değil, dil modelinin arama aracıdır.
        return topic.Length > 1 && !NotATopic().IsMatch(sentence)
            ? Command("rapor", ("islem", "hazirla"), ("konu", topic))
            : null;
    }

    private static QuickCommand? CalendarQuestion(string sentence)
    {
        var match = CalendarRequest().Match(sentence);
        if (!match.Success)
        {
            return null;
        }

        var when = match.Groups["w"].Value switch
        {
            "yarın" => "yarin",
            "bu hafta" or "haftaya" => "hafta",
            _ when match.Groups["next"].Success => "sonraki",
            _ => "bugun"
        };
        return Command("takvim", ("gun", when));
    }

    private static QuickCommand? Downloads(string sentence) => sentence switch
    {
        _ when SortDownloads().IsMatch(sentence) => Command("indirilenler", ("islem", "plan")),
        _ when UndoSort().IsMatch(sentence) => Command("indirilenler", ("islem", "geri_al")),
        _ => null
    };

    private static QuickCommand? DayEnd(string sentence) => DayEndRequest().IsMatch(sentence) ? Command("gun_sonu") : null;

    // "yapay zekâ ajanları hakkında rapor hazırla", "elektrikli arabaları araştır", "rust dili ile ilgili araştırma yap"
    [GeneratedRegex(@"^(?<x>.+?) (?:hakkında|konusunda|ile ilgili|üzerine) (?:bir )?(?:rapor|araştırma|dosya) (?:hazırla|yap|çıkar|yaz|oluştur)$|^(?<x>.+?)(?:['’]?(?:yı|yi|yu|yü|ı|i|u|ü|nı|ni|nu|nü))? (?:araştır|araştırıp rapor hazırla|detaylı araştır)$")]
    private static partial Regex ResearchRequest();

    [GeneratedRegex(@"^(?:bunu|şunu|onu|bunları|internette|google'da|webde)(?:\s|$)")]
    private static partial Regex NotATopic();

    [GeneratedRegex(@"^(?:son |hazırladığın )?raporu (?:aç|göster)$|^raporlarım$")]
    private static partial Regex OpenReport();

    // "bugün neler var", "yarın programım ne", "bu hafta takvimimde ne var", "bugün toplantım var mı", "sıradaki toplantım ne"
    [GeneratedRegex(@"^(?<w>bugün|yarın|bu hafta|haftaya) (?:takvimimde |ajandamda )?(?:neler var|ne var|programım (?:ne|nasıl)|toplantım var mı|toplantılarım (?:ne|neler)|etkinliklerim (?:ne|neler)|planım ne)$|^(?:takvimimde|ajandamda) (?:neler|ne) var$|^(?:programım|toplantılarım|etkinliklerim) (?:ne|neler)$|^toplantım var mı$|^(?<next>sıradaki|bir sonraki) (?:toplantım|etkinliğim|randevum) (?:ne|ne zaman)$")]
    private static partial Regex CalendarRequest();

    [GeneratedRegex(@"^(?:indirilenleri|indirilenler klasörünü|indirmeleri|downloads klasörünü) (?:düzenle|topla|sırala|temizle|ayır)$")]
    private static partial Regex SortDownloads();

    [GeneratedRegex(@"^(?:indirilenler(?:deki)? )?düzenlemeyi geri al$|^indirilenleri eski haline getir$")]
    private static partial Regex UndoSort();

    [GeneratedRegex(@"^(?:günü kapat|günün özetini (?:çıkar|ver|söyle)|gün sonu(?: özeti| raporu)?|akşam özeti|bugünü özetle)$")]
    private static partial Regex DayEndRequest();

    [GeneratedRegex(@"^(?:evet|evet evet|tamam|olur|yap|evet yap|evet düzenle|düzenle|tamam yap|onaylıyorum|onayla|aynen|hadi|evet lütfen)$")]
    private static partial Regex YesWords();

    [GeneratedRegex(@"^(?:hayır|yok|vazgeç|vazgeçtim|iptal|iptal et|gerek yok|boş ver|istemiyorum)$")]
    private static partial Regex NoWords();
}
