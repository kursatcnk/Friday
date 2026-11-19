using System.Text.RegularExpressions;

namespace Friday.Core;

public static partial class QuickIntents
{
    /// <summary>
    /// "Bunu sonra okuyacağım", "okuma listem", "sıradakini aç"; "bu şarkıyı kaydet"; "diskimde ne yer kaplıyor";
    /// "günlüğe yaz: …", "günlüğümü oku", "bu hafta günlüğüme ne yazmışım".
    /// </summary>
    private static QuickCommand? Creative(string sentence)
    {
        if (ReadLaterSave().IsMatch(sentence))
        {
            return Command("sonra_oku", ("islem", "kaydet"));
        }

        if (ReadLaterList().IsMatch(sentence))
        {
            return Command("sonra_oku", ("islem", "listele"));
        }

        if (ReadLaterOpen().IsMatch(sentence))
        {
            return Command("sonra_oku", ("islem", "ac"));
        }

        if (SaveSongRequest().IsMatch(sentence))
        {
            return Command("sarki_kaydet");
        }

        if (DiskRequest().IsMatch(sentence))
        {
            return Command("disk_analizi");
        }

        if (JournalWrite().Match(sentence) is { Success: true } write && write.Groups["t"].Value.Trim().Length > 1)
        {
            return Command("gunluk", ("islem", "yaz"), ("metin", write.Groups["t"].Value.Trim()));
        }

        if (JournalRead().Match(sentence) is { Success: true } read)
        {
            var span = read.Value.Contains("hafta", StringComparison.Ordinal) ? "hafta" : read.Value.Contains("dün", StringComparison.Ordinal) ? "dun" : "bugun";
            return Command("gunluk", ("islem", "oku"), ("donem", span));
        }

        return null;
    }

    [GeneratedRegex(@"^(?:bunu|bu sayfayı|bu siteyi|bu yazıyı|bu makaleyi) (?:sonra okuyacağım|sonra oku|sonra okumak için kaydet|sonraya kaydet|okuma listeme ekle|okuma listesine ekle)$")]
    private static partial Regex ReadLaterSave();

    [GeneratedRegex(@"^(?:okuma listem|okuma listemde ne var|okuma listemi oku|okunacaklarım|sonra okuyacaklarım)$")]
    private static partial Regex ReadLaterList();

    [GeneratedRegex(@"^(?:okuma listemden |okunacaklardan )?(?:sıradakini|sıradaki sayfayı|sıradaki yazıyı) aç$")]
    private static partial Regex ReadLaterOpen();

    [GeneratedRegex(@"^(?:bu|çalan) şarkıyı (?:kaydet|beğendim|listeme ekle|favorilere ekle|beğendiklerime ekle)$|^bu şarkı(?:yı)? (?:çok )?(?:güzel|beğendim)(?:, kaydet)?$")]
    private static partial Regex SaveSongRequest();

    [GeneratedRegex(@"^(?:diskimde|diskte|bilgisayarımda|bilgisayarda) (?:ne|neler) (?:çok )?yer kaplıyor$|^(?:diskte |bilgisayarda )?yer açmama yardım et$|^disk(?:imi)? analiz et$|^en büyük dosyalarım(?: neler)?$")]
    private static partial Regex DiskRequest();

    [GeneratedRegex(@"^(?:günlüğe|günlüğüme) (?:yaz|not al|ekle)[:,]? (?<t>.+)$|^(?<t>.+?) (?:diye )?günlüğe yaz$")]
    private static partial Regex JournalWrite();

    [GeneratedRegex(@"^(?:günlüğümü oku|bugün günlüğüme ne yazdım|dün günlüğüme ne yazdım|bu hafta günlüğüme ne yazmışım|günlüğümde bu hafta ne var)$")]
    private static partial Regex JournalRead();
}
