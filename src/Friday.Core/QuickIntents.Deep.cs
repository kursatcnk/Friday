using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

public static partial class QuickIntents
{
    /// <summary>
    /// "4 pomodoro yap", "pomodoroyu durdur"; "ruh halim 7", "bugün kendimi yorgun hissediyorum";
    /// "geçici dosyaları temizle", "bilgisayarı temizle"; "seyahat listesini kontrol et".
    /// </summary>
    private static QuickCommand? Deep(string sentence)
    {
        if (PomodoroStop().IsMatch(sentence))
        {
            return Command("pomodoro", ("islem", "durdur"));
        }

        if (PomodoroStart().Match(sentence) is { Success: true } pomodoro)
        {
            var count = pomodoro.Groups["n"].Success ? ParseNumber(pomodoro.Groups["n"].Value) ?? 4 : 4;
            return Command("pomodoro", ("islem", "baslat"), ("adet", Math.Clamp((int)count, 1, 8).ToString(CultureInfo.InvariantCulture)));
        }

        if (MoodRequest().Match(sentence) is { Success: true } mood)
        {
            var value = mood.Groups["v"].Value;
            var score = value switch
            {
                "harika" or "çok iyi" or "süper" => 9,
                "mutlu" or "iyi" or "güzel" => 7,
                "idare eder" or "orta" or "fena değil" => 5,
                "yorgun" or "stresli" or "gergin" => 4,
                "kötü" or "üzgün" => 3,
                "çok kötü" or "berbat" => 1,
                _ => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? Math.Clamp(number, 1, 10) : 5
            };
            return Command("ruh_hali", ("puan", score.ToString(CultureInfo.InvariantCulture)), ("kelime", value));
        }

        if (CleanupRequest().IsMatch(sentence))
        {
            return Command("temizlik");
        }

        if (ChecklistRequest().Match(sentence) is { Success: true } checklist)
        {
            return Command("kontrol_listesi", ("ad", checklist.Groups["l"].Value.Trim()));
        }

        return null;
    }

    [GeneratedRegex(@"^(?<n>\d+|bir|iki|üç|dört|beş|altı|yedi|sekiz) pomodoro(?: yap| başlat| başla| aç| yapalım| turu)?$|^pomodoro serisi(?:ni)?(?: başlat| aç| yap)?$")]
    private static partial Regex PomodoroStart();

    [GeneratedRegex(@"^pomodoro(?:yu|ları|ları)? (?:durdur|bitir|iptal et|kapat)$")]
    private static partial Regex PomodoroStop();

    [GeneratedRegex(@"^(?:bugün |şu an )?ruh halim (?<v>\d+|çok iyi|iyi|idare eder|orta|fena değil|kötü|çok kötü|berbat|harika)(?:/10| üzerinden 10)?$|^(?:bugün |şu an )?kendimi (?<v>çok iyi|iyi|kötü|çok kötü|yorgun|mutlu|harika|berbat|stresli|gergin|üzgün|süper) hissediyorum$")]
    private static partial Regex MoodRequest();

    [GeneratedRegex(@"^(?:geçici dosyaları|geri dönüşüm kutusunu|çöp kutusunu|bilgisayarı|diski) (?:temizle|boşalt)$|^(?:biraz )?yer aç$|^temizlik yap$")]
    private static partial Regex CleanupRequest();

    [GeneratedRegex(@"^(?<l>[\p{L}\d ]{2,25}?) (?:kontrol listesini|listesini) (?:başlat|kontrol et|kontrol edelim|gözden geçir|gözden geçirelim)$")]
    private static partial Regex ChecklistRequest();
}
