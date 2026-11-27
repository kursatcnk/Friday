using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Durum raporu, bilgisayar kontrolü, nöbetçiler, şans, kronometre, alarm, kapanış ve özel komutlar.</summary>
public static partial class QuickIntents
{
    /// <summary>
    /// "Baha dediğimde 'umrunda değilsin' de", "ders çalışıyorum deyince odak modunu aç": kullanıcının tanımladığı bir
    /// tetikleyici. İfade küçük harfe çevrilir, söylenecek söz yazıldığı gibi kalır.
    /// </summary>
    /// <returns>(ifade, rutin adımı) ya da null.</returns>
    public static (string Phrase, string Step)? CustomCommand(string text)
    {
        var original = AddressAnyCase().Replace(text.Trim(), "").Trim().TrimEnd('.', '!', ' ');
        // Türkçe küçük harfe çevirmek uzunluğu değiştirmez; eşleşme küçük harfte yapılır, söz özgün metinden kesilir.
        var lower = original.ToLower(Turkish);
        var match = CustomCommandRequest().Match(lower);
        if (!match.Success)
        {
            return null;
        }

        var phrase = Whitespace().Replace(match.Groups["x"].Value, " ").Trim().Trim(Quotes).Trim();
        if (phrase.Length < 2 || phrase.Split(' ').Length > 5)
        {
            return null;
        }

        var action = original.Substring(match.Groups["y"].Index, match.Groups["y"].Length).Trim().Trim(',', ' ');
        var actionLower = action.ToLower(Turkish);
        if (SayAction().Match(actionLower) is { Success: true } say)
        {
            var words = action.Substring(say.Groups["s"].Index, say.Groups["s"].Length).Trim().Trim(Quotes).Trim();
            return words.Length == 0 ? null : (phrase, $"soyle:{TurkishGrammar.Capitalize(words)}");
        }

        if (OpenAction().Match(actionLower) is { Success: true } open)
        {
            var target = action.Substring(open.Groups["a"].Index, open.Groups["a"].Length).Trim().Trim(Quotes).Trim();
            return target.Length < 2 ? null : (phrase, target.Contains('.', StringComparison.Ordinal) ? $"site:{target}" : $"uygulama:{target}");
        }

        return null;
    }

    /// <summary>Cümlede tetikleyici ifade geçiyor mu? "baha" → "Baha geldi", "Baha'yı gördüm" (ekli de olur).</summary>
    public static bool Mentions(string sentence, string phrase)
    {
        var normalized = Normalize(sentence);
        var needle = phrase.Trim().ToLower(Turkish);
        if (needle.Length == 0)
        {
            return false;
        }

        // Ek kesme işaretinden sonra her harf olabilir ("Baha'yı"); bitişikse yalnızca bilinen Türkçe ekler sayılır.
        // Böylece "bahayı" eşleşir ama "bahar" eşleşmez.
        return Regex.IsMatch(normalized, $@"(?<![\p{{L}}\d]){Regex.Escape(needle)}(?:['’]\p{{L}}{{1,8}}|{AttachedSuffixes})?(?![\p{{L}}\d])",
            RegexOptions.None, TimeSpan.FromMilliseconds(200));
    }

    private const string AttachedSuffixes =
        "(?:ları|leri|lar|ler|nın|nin|nun|nün|yla|yle|dan|den|tan|ten|yı|yi|yu|yü|ya|ye|ın|in|un|ün|da|de|ta|te|la|le|ı|i|u|ü|a|e)";

    private static readonly char[] Quotes = ['"', '\'', '“', '”', '‘', '’', '«', '»'];

    private static QuickCommand? Trigger(string sentence, IEnumerable<string> triggers)
    {
        foreach (var trigger in triggers)
        {
            if (Mentions(sentence, trigger))
            {
                return Command("tetik", ("ad", trigger));
            }
        }

        return null;
    }

    private static QuickCommand? Closing(string sentence) => ClosingWords().IsMatch(sentence) ? Command("kapanis") : null;

    private static QuickCommand? StatusReport(string sentence) => StatusRequest().IsMatch(sentence) ? Command("durum_raporu") : null;

    private static QuickCommand? SystemControl(string sentence)
    {
        if (ShutdownRequest().Match(sentence) is { Success: true } shutdown)
        {
            var amount = ParseNumber(shutdown.Groups["n"].Value) ?? 1;
            var minutes = shutdown.Groups["u"].Value == "saat" ? amount * 60 : amount;
            return Command("sistem", ("islem", "kapat"), ("dakika", Math.Max(1, minutes).ToString("0", CultureInfo.InvariantCulture)));
        }

        if (ThemeRequest().Match(sentence) is { Success: true } theme)
        {
            return Command("sistem", ("islem", theme.Groups["t"].Value is "karanlık" or "koyu" or "gece" ? "tema_koyu" : "tema_acik"));
        }

        var action = sentence switch
        {
            _ when LockRequest().IsMatch(sentence) => "kilitle",
            _ when SleepRequest().IsMatch(sentence) => "uyku",
            _ when MonitorOffRequest().IsMatch(sentence) => "ekran_kapat",
            _ when ShowDesktopRequest().IsMatch(sentence) => "masaustu",
            _ when ScreenshotRequest().IsMatch(sentence) => "ekran_goruntusu",
            _ when ShutdownCancel().IsMatch(sentence) => "kapat_iptal",
            _ => ""
        };
        return action.Length > 0 ? Command("sistem", ("islem", action)) : null;
    }

    private static QuickCommand? Watch(string sentence)
    {
        if (GpuWatchRequest().Match(sentence) is { Success: true } gpu)
        {
            return Command("nobet", ("islem", "gpu"), ("derece", gpu.Groups["n"].Value));
        }

        var action = sentence switch
        {
            _ when DownloadWatchRequest().IsMatch(sentence) => "indirme",
            _ when IdleWatchRequest().IsMatch(sentence) => "bosta",
            _ when InternetWatchRequest().IsMatch(sentence) => "internet",
            _ when WatchList().IsMatch(sentence) => "liste",
            _ when WatchCancel().IsMatch(sentence) => "iptal",
            _ => ""
        };
        return action.Length > 0 ? Command("nobet", ("islem", action)) : null;
    }

    private static QuickCommand? Luck(string sentence)
    {
        if (CoinRequest().IsMatch(sentence))
        {
            return Command("sans", ("islem", "yazitura"));
        }

        if (DiceRequest().Match(sentence) is { Success: true } dice)
        {
            var count = dice.Groups["n"].Success ? ParseNumber(dice.Groups["n"].Value) ?? 1 : 1;
            return Command("sans", ("islem", "zar"), ("adet", count.ToString("0", CultureInfo.InvariantCulture)));
        }

        if (NumberPick().Match(sentence) is { Success: true } pick)
        {
            return Command("sans", ("islem", "sayi"), ("en_az", pick.Groups["a"].Value), ("en_cok", pick.Groups["b"].Value));
        }

        return null;
    }

    private static QuickCommand? Stopwatch(string sentence) => sentence switch
    {
        _ when StopwatchStart().IsMatch(sentence) => Command("kronometre", ("islem", "baslat")),
        _ when StopwatchStop().IsMatch(sentence) => Command("kronometre", ("islem", "durdur")),
        _ when StopwatchResume().IsMatch(sentence) => Command("kronometre", ("islem", "devam")),
        _ when StopwatchLap().IsMatch(sentence) => Command("kronometre", ("islem", "tur")),
        _ when StopwatchReset().IsMatch(sentence) => Command("kronometre", ("islem", "sifirla")),
        _ when StopwatchStatus().IsMatch(sentence) => Command("kronometre", ("islem", "durum")),
        _ => null
    };

    /// <summary>"Beni yarın 7'de uyandır", "sabah 6:30'da uyandır" → hatırlatma ("Günaydın, uyanma vakti").</summary>
    private static QuickCommand? WakeUp(string sentence) =>
        WakeUpRequest().Match(sentence) is { Success: true } wake && wake.Groups["when"].Value.Trim().Length > 0
            ? Command("hatirlatici", ("islem", "kur"), ("zaman", wake.Groups["when"].Value.Trim()), ("metin", AlarmText))
            : null;

    /// <summary>Alarm hatırlatmasının metni; duyurusu ayrıca okunur.</summary>
    public const string AlarmText = "uyanma vakti";

    [GeneratedRegex(@"^(?:hey\s+)?friday[,!.\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex AddressAnyCase();

    // "baha dediğimde x de", "baha ismini duyduğun anda, x söyle", "ders çalışıyorum deyince spotify'ı aç"
    [GeneratedRegex(@"^(?:ben\s+)?(?:sana\s+)?(?<x>.{2,60}?)\s+(?:(?:diye|kelimesini|ismini|adını|sözünü|lafını|cümlesini)\s+)?(?:dediğimde|dediğim zaman|dediğim an|dediğim anda|deyince|dersem|söylediğimde|söyleyince|duyduğunda|duyunca|duyduğun anda|duyduğum anda|duyduğun zaman|duyarsan)[,:]?\s+(?<y>.+)$")]
    private static partial Regex CustomCommandRequest();

    [GeneratedRegex(@"^(?<s>.+?)(?:\s+diye)?\s+(?:de|söyle|cevap ver|yanıt ver|diye cevap ver)$")]
    private static partial Regex SayAction();

    [GeneratedRegex(@"^(?<a>.+?)(?:['’](?:yı|yi|yu|yü|ı|i|u|ü|nı|ni|nu|nü))?\s+(?:aç|başlat|çalıştır)$")]
    private static partial Regex OpenAction();

    [GeneratedRegex(@"^(?:tamam |çok )?(?:teşekkürler|teşekkür ederim|sağ ol|sağol|eyvallah|eline sağlık|tamam bu kadar|bu kadar|şimdilik bu kadar|görüşürüz|hoşça kal|iyi geceler friday|sus|yeter)(?: friday)?$")]
    private static partial Regex ClosingWords();

    [GeneratedRegex(@"^(?:bana )?(?:durum raporu(?: ver)?|rapor ver|sistem raporu|genel durum(?: ne)?|her şey yolunda mı|ne durumdayız|durumu özetle)$")]
    private static partial Regex StatusRequest();

    [GeneratedRegex(@"^(?:bilgisayarı |ekranı |pc'yi )?kilitle$")]
    private static partial Regex LockRequest();

    [GeneratedRegex(@"^(?:bilgisayarı )?(?:uyku moduna al|uykuya al|uyut)$")]
    private static partial Regex SleepRequest();

    [GeneratedRegex(@"^(?:ekranı|monitörü|ekranları) (?:kapat|karart)$")]
    private static partial Regex MonitorOffRequest();

    [GeneratedRegex(@"^(?:masaüstünü göster|her şeyi küçült|bütün pencereleri küçült|tüm pencereleri küçült|pencereleri küçült)$")]
    private static partial Regex ShowDesktopRequest();

    [GeneratedRegex(@"^(?<t>karanlık|koyu|gece|aydınlık|açık) (?:moda|modu|mod|temaya|temayı|tema) (?:geç|aç|yap|al)$")]
    private static partial Regex ThemeRequest();

    [GeneratedRegex(@"^(?:bir )?ekran görüntüsü (?:al|çek|kaydet)$")]
    private static partial Regex ScreenshotRequest();

    // "bilgisayarı kapat", "30 dakika sonra bilgisayarı kapat", "bir saat sonra bilgisayarı kapat"
    [GeneratedRegex(@"^(?:(?<n>[\p{L}\d]+(?: [\p{L}]+)?) (?<u>dakika|saat) sonra )?bilgisayarı kapat$")]
    private static partial Regex ShutdownRequest();

    [GeneratedRegex(@"^(?:kapatmayı|kapanmayı|kapanışı) (?:iptal et|durdur)$|^bilgisayarı kapatma$")]
    private static partial Regex ShutdownCancel();

    [GeneratedRegex(@"^(?:indirme|indirmeler|indirmem|dosya) (?:bitince|tamamlanınca|inince) (?:haber ver|söyle|uyar|beni uyar)$")]
    private static partial Regex DownloadWatchRequest();

    [GeneratedRegex(@"^(?:bilgisayar|işlem|render|derleme|dışa aktarma|kurulum|güncelleme|kodlama) (?:boşa çıkınca|bitince|tamamlanınca) (?:haber ver|söyle|uyar|beni uyar)$")]
    private static partial Regex IdleWatchRequest();

    [GeneratedRegex(@"^internet (?:gelince|bağlanınca|düzelince|gelirse) (?:haber ver|söyle|uyar)$")]
    private static partial Regex InternetWatchRequest();

    [GeneratedRegex(@"^(?:ekran kartı|gpu)(?: sıcaklığı)? (?<n>\d{2,3}) dereceyi (?:geçerse|aşarsa) (?:haber ver|uyar|söyle)$")]
    private static partial Regex GpuWatchRequest();

    [GeneratedRegex(@"^(?:neyi bekliyorsun|neleri izliyorsun|nöbette ne var|nöbetçiler|neyi izliyorsun)$")]
    private static partial Regex WatchList();

    [GeneratedRegex(@"^(?:nöbeti|izlemeyi|beklemeyi|nöbetçileri) (?:iptal et|bırak|durdur|kapat)$")]
    private static partial Regex WatchCancel();

    [GeneratedRegex(@"^(?:bir )?yazı tura (?:at|atar mısın|atalım)$")]
    private static partial Regex CoinRequest();

    [GeneratedRegex(@"^(?:(?<n>iki|üç|dört|beş|\d) )?zar (?:at|atar mısın|atalım)$")]
    private static partial Regex DiceRequest();

    [GeneratedRegex(@"^(?<a>\d+) (?:ile|ve) (?<b>\d+) arasında (?:bir )?sayı (?:tut|seç|söyle)$")]
    private static partial Regex NumberPick();

    [GeneratedRegex(@"^(?:kronometre(?:yi)?|süre tutmaya) (?:aç|başlat|başla)$|^süre tut(?:ar mısın|alım)?$")]
    private static partial Regex StopwatchStart();

    [GeneratedRegex(@"^kronometre(?:yi)? (?:durdur|duraklat|kapat|bitir|beklet)$")]
    private static partial Regex StopwatchStop();

    [GeneratedRegex(@"^kronometre(?:ye|yi)? (?:devam(?: et)?|sürdür|devam ettir)$")]
    private static partial Regex StopwatchResume();

    // "tur", "tur at", "turu kaydet", "kronometrede tur", "ara zaman"
    [GeneratedRegex(@"^(?:tur(?: at| al| kaydet)?|turu (?:kaydet|al)|kronometrede tur(?: al| at)?|ara zaman(?: al)?)$")]
    private static partial Regex StopwatchLap();

    [GeneratedRegex(@"^kronometre(?:yi)? (?:sıfırla|temizle)$")]
    private static partial Regex StopwatchReset();

    [GeneratedRegex(@"^kronometre(?: kaç| ne kadar| ne durumda)?(?: oldu| gösteriyor)?$")]
    private static partial Regex StopwatchStatus();

    [GeneratedRegex(@"^(?:beni )?(?<when>(?:(?:bugün|yarın|öbür gün|pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar) ?)?(?:(?:sabah|öğlen|öğle|akşam|gece) ?)?(?:saat ?)?(?:\d{1,2}(?:[:.]\d{2})?(?:['’]?(?:de|da|te|ta))?)?) (?:beni )?uyandır$")]
    private static partial Regex WakeUpRequest();
}
