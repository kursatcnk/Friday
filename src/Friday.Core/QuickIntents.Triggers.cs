using System.Text.RegularExpressions;

namespace Friday.Core;

public static partial class QuickIntents
{
    /// <summary>
    /// "Bilgisayara dönünce çamaşırları hatırlat", "Chrome'u açınca faturayı hatırlat", "Spotify açılınca X'i hatırlat",
    /// "10 dakika ertele", "market listesine süt ve ekmek ekle", "market listemde ne var", "market listesinden sütü çıkar".
    /// </summary>
    private static QuickCommand? Triggers(string sentence)
    {
        if (IntervalReminderRequest().Match(sentence) is { Success: true } every)
        {
            var amount = every.Groups["n"].Value switch
            {
                "" or "bir" => 1,
                "yarım" => 0.5,
                "iki" => 2,
                "üç" => 3,
                "dört" => 4,
                var digits => double.TryParse(digits, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 1
            };
            var minutes = Math.Clamp((int)Math.Round(every.Groups["u"].Value == "saat" ? amount * 60 : amount), 5, 12 * 60);
            var text = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Command("hatirlatici", ("islem", "kur"), ("zaman", $"{text} dakika sonra"), ("metin", every.Groups["t"].Value.Trim()), ("tekrar", $"her:{text}"));
        }

        if (WeeklyReviewRequest().IsMatch(sentence))
        {
            return Command("haftalik_ozet");
        }

        if (ReturnReminderRequest().Match(sentence) is { Success: true } back)
        {
            return Command("kosullu_hatirlatici", ("kosul", "donus"), ("metin", back.Groups["t"].Value.Trim()));
        }

        if (AppReminderRequest().Match(sentence) is { Success: true } app)
        {
            var name = app.Groups["a"].Value.Trim().TrimEnd('\'');
            return Command("kosullu_hatirlatici", ("kosul", "uygulama:" + name), ("metin", app.Groups["t"].Value.Trim()));
        }

        if (ReminderSnoozeRequest().Match(sentence) is { Success: true } snooze)
        {
            // Sayı söylenmezse boş gider: hatırlatma 10, mola önerisi 15 dakika ertelenir.
            var minutes = ParseNumber(snooze.Groups["n"].Value) is { } value ? value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "";
            return Command("ertele", ("dakika", minutes));
        }

        if (ListAddRequest().Match(sentence) is { Success: true } add)
        {
            return Command("liste", ("islem", "ekle"), ("ad", add.Groups["l"].Value.Trim()), ("oge", add.Groups["i"].Value.Trim()));
        }

        if (ListRemoveRequest().Match(sentence) is { Success: true } remove)
        {
            return Command("liste", ("islem", "cikar"), ("ad", remove.Groups["l"].Value.Trim()), ("oge", remove.Groups["i"].Value.Trim()));
        }

        if (ListClearRequest().Match(sentence) is { Success: true } clear)
        {
            return Command("liste", ("islem", "temizle"), ("ad", clear.Groups["l"].Value.Trim()));
        }

        if (ListReadRequest().Match(sentence) is { Success: true } read)
        {
            return Command("liste", ("islem", "oku"), ("ad", read.Groups["l"].Value.Trim()));
        }

        return ListNamesRequest().IsMatch(sentence) ? Command("liste", ("islem", "listeler")) : null;
    }

    [GeneratedRegex(@"^her (?:(?<n>\d+|yarım|bir|iki|üç|dört) )?(?<u>dakika|saat)(?:da|ta|te)?(?: başı)?(?: bir)? (?<t>.+?) (?:bana )?hatırlat$")]
    private static partial Regex IntervalReminderRequest();

    [GeneratedRegex(@"^(?:bu haftayı özetle|haftamı özetle|haftalık özet|haftalık rapor|bu hafta neler yaptım|bu hafta nasıl geçti|bu haftanın özeti)$")]
    private static partial Regex WeeklyReviewRequest();

    [GeneratedRegex(@"^(?:bilgisayara |masaya )?(?:dönünce|döndüğümde|geri gelince|geri geldiğimde|gelince) (?<t>.+?) (?:bana )?hatırlat$")]
    private static partial Regex ReturnReminderRequest();

    [GeneratedRegex(@"^(?<a>[\p{L}\d .+']{2,30}?)(?:'?[ıiuü]|'?[yn][ıiuü])? (?:açınca|açtığımda|açılınca|açıldığında|girince|girdiğimde) (?<t>.+?) (?:bana )?hatırlat$")]
    private static partial Regex AppReminderRequest();

    [GeneratedRegex(@"^(?:hatırlatmayı )?(?:(?<n>[\p{L}\d]+(?: [\p{L}]+)?) dakika )?ertele$|^(?:biraz )?sonra (?:tekrar )?hatırlat$")]
    private static partial Regex ReminderSnoozeRequest();

    [GeneratedRegex(@"^(?<l>[\p{L}\d ]{2,25}?) (?:listesine|listeme) (?<i>.+?) (?:ekle|yaz)$")]
    private static partial Regex ListAddRequest();

    [GeneratedRegex(@"^(?<l>[\p{L}\d ]{2,25}?) listesinden (?<i>.+?) (?:çıkar|sil|kaldır)$")]
    private static partial Regex ListRemoveRequest();

    [GeneratedRegex(@"^(?<l>[\p{L}\d ]{2,25}?) listesini (?:temizle|sil|boşalt)$")]
    private static partial Regex ListClearRequest();

    [GeneratedRegex(@"^(?<l>[\p{L}\d ]{2,25}?) (?:listemde ne var|listesinde ne var|listesini oku|listem|listemi oku|listesi)$")]
    private static partial Regex ListReadRequest();

    [GeneratedRegex(@"^(?:listelerim|listelerim neler|hangi listelerim var)$")]
    private static partial Regex ListNamesRequest();
}
