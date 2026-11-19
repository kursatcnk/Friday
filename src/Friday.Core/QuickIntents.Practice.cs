using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Kelime pratiği ve uygulama bazında ses.</summary>
public static partial class QuickIntents
{
    /// <summary>"müzik", "video" gibi sözler bir uygulama adı değildir; genel ses komutlarına kalır.</summary>
    private static readonly HashSet<string> NotAudioApps = ["müzik", "müziğ", "şarkı", "şarkını", "video", "ses", "bilgisayar", "bilgisayarı", "hoparlör", "kulaklık", "tv", "televizyon", "film"];

    /// <summary>"kelime ekle: serendipity = mutlu tesadüf" → ("serendipity", "mutlu tesadüf"); harfler korunur.</summary>
    public static (string Front, string Back)? WordDefinition(string text)
    {
        var original = AddressAnyCase().Replace(text.Trim(), "").Trim().TrimEnd('.', '!');
        var match = WordDefine().Match(original);
        return match.Success && match.Groups["f"].Value.Trim().Length > 0 && match.Groups["b"].Value.Trim().Length > 0
            ? (match.Groups["f"].Value.Trim(), match.Groups["b"].Value.Trim())
            : null;
    }

    private static QuickCommand? Practice(string sentence)
    {
        if (VocabularyStart().IsMatch(sentence))
        {
            return Command("kelime", ("islem", "calis"));
        }

        if (VocabularyStats().IsMatch(sentence))
        {
            return Command("kelime", ("islem", "durum"));
        }

        return VocabularyRemove().Match(sentence) is { Success: true } remove
            ? Command("kelime", ("islem", "sil"), ("kelime", remove.Groups["f"].Value.Trim()))
            : null;
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

    [GeneratedRegex(@"^(?:yeni )?kelime ekle[:,]?\s+(?<f>[^=:]+?)\s*(?:=|:| - | anlamı | demek(?:tir)? | yani )\s*(?<b>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex WordDefine();

    // "kelime çalışalım", "ingilizce pratik yapalım", "kelime testi", "kartları tekrar edelim"
    [GeneratedRegex(@"^(?:hadi )?(?:ingilizce )?(?:kelime|kelimeleri|kelimelerimi|kartları|kelime kartlarını) (?:çalış(?:alım|mak istiyorum|tır|ayım)?|tekrar (?:et|edelim|edeyim)|pratik yap(?:alım)?|sor)$|^(?:hadi )?(?:ingilizce )?(?:kelime|dil) (?:pratiği|testi|antrenmanı|çalışması|tekrarı)(?: yap(?:alım)?| aç)?$|^(?:hadi )?ingilizce (?:pratik yap(?:alım)?|çalış(?:alım)?|çalışmak istiyorum)$")]
    private static partial Regex VocabularyStart();

    [GeneratedRegex(@"^(?:kaç kelime (?:biliyorum|öğrendim)|kelime durumum|kelimelerim(?: nasıl gidiyor| ne durumda)?|kelime istatistiklerim|kelime destem(?: nasıl)?)$")]
    private static partial Regex VocabularyStats();

    [GeneratedRegex(@"^(?<f>.+?) kelimesini (?:sil|çıkar|desteden çıkar)$")]
    private static partial Regex VocabularyRemove();

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
