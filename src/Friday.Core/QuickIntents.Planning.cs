using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Gün planı, bağlantı testi, işe odak ve uygulama bazında ses.</summary>
public static partial class QuickIntents
{
    /// <summary>"müzik", "video" gibi sözler bir uygulama adı değildir; genel ses komutlarına kalır.</summary>
    private static readonly HashSet<string> NotAudioApps = ["müzik", "müziğ", "şarkı", "şarkını", "video", "ses", "bilgisayar", "bilgisayarı", "hoparlör", "kulaklık", "tv", "televizyon", "film"];

    private static QuickCommand? Planning(string sentence)
    {
        if (DayPlanRequest().IsMatch(sentence))
        {
            return Command("gun_plani");
        }

        if (NetworkQuestion().IsMatch(sentence))
        {
            return Command("baglanti");
        }

        if (FocusTaskRequest().Match(sentence) is { Success: true } focus
            && focus.Groups["x"].Value.Trim() is { Length: > 2 } task
            && !task.Contains("dakik", StringComparison.Ordinal) && !task.Contains("saat", StringComparison.Ordinal)
            && ParseNumber(task.Split(' ')[0]) is null && task is not ("biraz" or "işe" or "işime" or "buna" or "şuna"))
        {
            var minutes = focus.Groups["n"].Success && ParseNumber(focus.Groups["n"].Value) is { } value
                ? (focus.Groups["u"].Value == "saat" ? value * 60 : value).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                : "";
            return Command("isle_odak", ("is", task), ("dakika", minutes));
        }

        return null;
    }

    /// <summary>"Spotify'ın sesini kıs", "Discord'u sessize al", "Chrome'un sesini yüzde 30 yap", "hangi uygulamalar ses çıkarıyor".</summary>
    private static QuickCommand? AppVolume(string sentence)
    {
        if (AppVolumeList().IsMatch(sentence))
        {
            return Command("uygulama_sesi", ("islem", "listele"));
        }

        string? app = null;
        string action = "";
        var level = "";
        if (AppVolumeSet().Match(sentence) is { Success: true } set)
        {
            (app, action, level) = (set.Groups["a"].Value, "ayarla", set.Groups["n"].Value);
        }
        else if (AppVolumeStep().Match(sentence) is { Success: true } step)
        {
            app = step.Groups["a"].Value;
            action = step.Groups["op"].Value switch
            {
                "kıs" or "azalt" or "düşür" => "kis",
                "kapat" or "sustur" => "sessiz",
                "geri aç" or "geri getir" => "sesli",
                _ => "ac"
            };
        }
        else if (AppMute().Match(sentence) is { Success: true } mute)
        {
            (app, action) = (mute.Groups["a"].Value, "sessiz");
        }

        app = app?.Trim();
        return app is { Length: >= 2 } && !NotAudioApps.Contains(app)
            ? Command("uygulama_sesi", ("islem", action), ("ad", app), ("deger", level))
            : null;
    }

    [GeneratedRegex(@"^(?:günümü|bugünü|günü|bugünümü) planla(?:r mısın|yalım)?$|^(?:bugün için |günlük )?(?:bir )?plan (?:yap|çıkar|hazırla)$|^(?:bugünün|günün) planı(?: ne| nedir)?$|^bugün ne zaman ne yapayım$")]
    private static partial Regex DayPlanRequest();

    [GeneratedRegex(@"^(?:internet(?:im)?|internet bağlantı(?:m|mı)?|bağlantı(?:m|mı)?|wi-?fi(?:m|ım)?) (?:nasıl|yavaş mı|çalışıyor mu|kontrol et|test et|var mı|iyi mi|sorunlu mu)$|^(?:ping at|internet testi(?: yap)?|bağlantı testi(?: yap)?)$|^internet(?:imi|i)? (?:kontrol et|test et)$|^internet neden (?:yavaş|gitti|kesiliyor)$")]
    private static partial Regex NetworkQuestion();

    // "faturayı ödemeye 25 dakika odaklan", "sunuma odaklanalım", "rapor için 1 saat odak aç"
    [GeneratedRegex(@"^(?<x>\p{L}[\p{L}\d ]{1,40}?)(?:['’]?(?:ya|ye|na|ne|a|e)| için) (?:(?<n>\d+|[\p{L}]+(?: [\p{L}]+)?) (?<u>dakika|saat)(?:lık)? )?(?:odaklan(?:ayım|alım|acağım|mak istiyorum)?|odak(?: aç)?)$")]
    private static partial Regex FocusTaskRequest();

    [GeneratedRegex(@"^(?:hangi uygulamalar(?: ses çıkarıyor| ses veriyor| çalıyor)?|uygulama sesleri(?:ni göster)?|ses karıştırıcı(?:yı aç)?|ses mikseri(?:ni aç)?)$")]
    private static partial Regex AppVolumeList();

    // "spotify'ın sesini yüzde 30 yap", "chrome sesini 20'ye indir"
    [GeneratedRegex(@"^(?<a>[\p{L}\d .+-]{2,30}?)(?:['’]?(?:nın|nin|nun|nün|ın|in|un|ün))? sesini (?:yüzde )?(?<n>\d{1,3})['’]?(?:e|a|ye|ya)? (?:yap|ayarla|indir|getir|çıkar|düşür|kıs|aç)$")]
    private static partial Regex AppVolumeSet();

    // "spotify'ın sesini kıs", "discord'un sesini biraz aç", "chrome'un sesini kapat", "spotify'ın sesini geri aç"
    [GeneratedRegex(@"^(?<a>[\p{L}\d .+-]{2,30}?)(?:['’]?(?:nın|nin|nun|nün|ın|in|un|ün))? sesini (?:biraz )?(?<op>geri aç|geri getir|kıs|azalt|düşür|aç|artır|yükselt|kapat|sustur)$")]
    private static partial Regex AppVolumeStep();

    [GeneratedRegex(@"^(?<a>[\p{L}\d .+-]{2,30}?)(?:['’]?(?:yı|yi|yu|yü|ı|i|u|ü|nı|ni|nu|nü))? sessize al$")]
    private static partial Regex AppMute();
}
