using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Yapılacaklar, tekrarlayan hatırlatmalar, ortam sesleri, pano geçmişi, tekrar, uygulama kapatma ve kur çevirme.</summary>
public static partial class QuickIntents
{
    private static readonly Dictionary<string, string> AmbientKinds = new()
    {
        ["yağmur"] = "yagmur", ["beyaz gürültü"] = "beyaz", ["pembe gürültü"] = "pembe", ["kahverengi gürültü"] = "kahverengi",
        ["odak"] = "kahverengi", ["dalga"] = "dalga", ["deniz"] = "dalga", ["okyanus"] = "dalga", ["şömine"] = "somine",
        ["rüzgar"] = "ruzgar", ["rüzgâr"] = "ruzgar"
    };

    /// <summary>"100", "1.250,5", "yüz", "iki yüz", "bin", "beş bin" → tutar; anlaşılmazsa null.</summary>
    public static double? ParseAmount(string text)
    {
        var clean = text.Trim().ToLower(Turkish);
        if (clean.Length == 0)
        {
            return null;
        }

        if (char.IsDigit(clean[0]))
        {
            var normalized = clean.Contains(',', StringComparison.Ordinal) ? clean.Replace(".", "", StringComparison.Ordinal).Replace(',', '.') : clean.Replace(".", "", StringComparison.Ordinal);
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double total = 0;
        double current = 0;
        foreach (var word in words)
        {
            switch (word)
            {
                case "yüz":
                    current = (current == 0 ? 1 : current) * 100;
                    break;
                case "bin":
                    total += (current == 0 ? 1 : current) * 1000;
                    current = 0;
                    break;
                default:
                    if (ParseNumber(word) is not { } part)
                    {
                        return null;
                    }

                    current += part;
                    break;
            }
        }

        return total + current;
    }

    private static QuickCommand? Todo(string sentence)
    {
        if (TodoAdd().Match(sentence) is { Success: true } add && add.Groups["x"].Value.Trim().Length > 1)
        {
            return Command("yapilacak", ("islem", "ekle"), ("metin", add.Groups["x"].Value.Trim()));
        }

        if (TodoList().IsMatch(sentence))
        {
            return Command("yapilacak", ("islem", "listele"));
        }

        return TodoClear().IsMatch(sentence) ? Command("yapilacak", ("islem", "temizle")) : null;
    }

    /// <summary>"Faturayı ödedim": listede eşleşen iş varsa tamamlanır; yoksa soru dil modeline gider.</summary>
    private static QuickCommand? TodoDone(string sentence) =>
        TodoComplete().Match(sentence) is { Success: true } done && done.Groups["x"].Value.Trim().Length > 2
            ? new QuickCommand("yapilacak", new Dictionary<string, string> { ["islem"] = "tamamla", ["metin"] = done.Groups["x"].Value.Trim() }, Tentative: true)
            : null;

    private static QuickCommand? RecurringReminder(string sentence)
    {
        var match = RecurringRequest().Match(sentence);
        if (!match.Success)
        {
            return null;
        }

        var unit = match.Groups["wd"].Success ? match.Groups["wd"].Value : match.Groups["r"].Value;
        if (RepeatRule.FromWords(unit) is not { } rule)
        {
            return null;
        }

        // "her sabah 8'de" → "sabah 8'de"; "her pazartesi 10'da" → "pazartesi 10'da".
        var part = match.Groups["r"].Value;
        var when = (part is "sabah" or "akşam" or "gece" or "öğlen" or "pazartesi" or "salı" or "çarşamba" or "perşembe" or "cuma" or "cumartesi" or "pazar"
            ? part + " " : "") + match.Groups["when"].Value.Trim();
        return Command("hatirlatici", ("islem", "ekle"), ("zaman", when), ("metin", match.Groups["note"].Value.Trim()), ("tekrar", rule));
    }

    private static QuickCommand? Ambient(string sentence)
    {
        if (AmbientStop().IsMatch(sentence))
        {
            return Command("ortam", ("islem", "kapat"));
        }

        var match = AmbientStart().Match(sentence);
        if (!match.Success)
        {
            return null;
        }

        var minutes = match.Groups["n"].Success && ParseNumber(match.Groups["n"].Value) is { } value ? value.ToString("0", CultureInfo.InvariantCulture) : "";
        return Command("ortam", ("islem", "ac"), ("tur", AmbientKinds[match.Groups["k"].Value]), ("dakika", minutes));
    }

    /// <summary>"2. kopyaladığımı yapıştır": "2." cümle sonu sanılmasın diye bölünmemiş metne bakılır.</summary>
    private static QuickCommand? ClipboardPasteOrdinal(string text)
    {
        var raw = AddressAnyCase().Replace(text.Trim(), "").Trim().TrimEnd('.', '!', '?').ToLower(Turkish);
        return ClipboardPaste().IsMatch(raw) ? ClipboardHistory(raw) : null;
    }

    private static QuickCommand? ClipboardHistory(string sentence)
    {
        if (ClipboardList().IsMatch(sentence))
        {
            return Command("pano_gecmisi", ("islem", "listele"));
        }

        var match = ClipboardPaste().Match(sentence);
        if (!match.Success)
        {
            return null;
        }

        var index = match.Groups["o"].Success
            ? (ParseNumber(match.Groups["o"].Value) ?? 1) + 1
            : match.Groups["n"].Success ? ParseNumber(match.Groups["n"].Value) ?? 1 : 1;
        return Command("pano_gecmisi", ("islem", "yapistir"), ("sira", index.ToString("0", CultureInfo.InvariantCulture)));
    }

    private static QuickCommand? Repeat(string sentence) => RepeatRequest().IsMatch(sentence) ? Command("tekrar") : null;

    /// <summary>"Discord'u kapat", "bu pencereyi kapat": pencere bulunursa kapatılır, bulunmazsa soru dil modeline gider.</summary>
    private static QuickCommand? CloseApp(string sentence)
    {
        var match = CloseRequest().Match(sentence);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["x"].Value.Trim();
        return name.Length < 2 || name.Split(' ').Length > 3
            ? null
            : new QuickCommand("uygulama_kapat", new Dictionary<string, string> { ["ad"] = name }, Tentative: true);
    }

    private static QuickCommand? CurrencyConversion(string sentence)
    {
        var match = ConversionRequest().Match(sentence);
        if (!match.Success || ParseAmount(match.Groups["a"].Value) is not { } amount)
        {
            return null;
        }

        static string Code(string word) => word switch
        {
            "lira" or "tl" or "türk lirası" => "TRY",
            "dolar" => "USD",
            "euro" or "avro" => "EUR",
            "sterlin" => "GBP",
            "frank" => "CHF",
            "yen" => "JPY",
            _ => "RUB"
        };

        var from = Code(match.Groups["from"].Value);
        // "100 dolar ne kadar": hedef söylenmediyse Türk lirası.
        var to = match.Groups["to"].Success ? Code(match.Groups["to"].Value) : "TRY";
        return from == to
            ? null
            : Command("doviz", ("miktar", amount.ToString("0.##", CultureInfo.InvariantCulture)), ("kaynak", from), ("hedef", to));
    }

    [GeneratedRegex(@"^(?:yapılacaklar(?: listesi)?(?:ma|ne|na|a)?|yapılacaklarıma|yapılacaklara|görevlerime|görev listeme|listeme) ekle[:,]?\s+(?<x>.+)$|^(?<x>.+?) (?:yapılacaklara|yapılacaklarıma|yapılacaklar listesine|görevlerime|görev listeme|listeme) ekle$|^(?:yeni görev|görev ekle|yapılacak ekle)[:,]?\s+(?<x>.+)$|^(?:yapılacaklara|yapılacaklarıma|yapılacaklar listesine|görevlerime) (?<x>.+?) (?:ekle|yaz)$")]
    private static partial Regex TodoAdd();

    [GeneratedRegex(@"^(?:yapılacaklarım|yapılacaklar listem|yapılacaklar|görevlerim|yapılacak işlerim)(?: neler| ne| var mı| ne durumda)?$|^yapılacakları (?:oku|söyle|göster)$|^bugün ne yapmam (?:lazım|gerekiyor)$|^(?:yapılacaklarımda|yapılacaklarda|yapılacaklar listemde|görevlerimde) ne(?:ler)? var$")]
    private static partial Regex TodoList();

    [GeneratedRegex(@"^(?:tamamlananları|bitenleri|bitmiş görevleri) (?:temizle|sil)$")]
    private static partial Regex TodoClear();

    [GeneratedRegex(@"^(?<x>.+?) (?:işini |görevini )?(?:tamamladım|bitirdim|hallettim|tamamlandı)$")]
    private static partial Regex TodoComplete();

    // "her gün 9'da ilacımı hatırlat", "her sabah 8'de su içmeyi hatırlat", "hafta içi her gün 18'de spor için hatırlat"
    [GeneratedRegex(@"^(?:(?<wd>hafta içi|hafta sonu) )?her (?<r>gün|sabah|akşam|gece|öğlen|pazartesi|salı|çarşamba|perşembe|cuma|cumartesi|pazar|hafta|ay)(?: günü)? (?<when>(?:saat ?)?\d{1,2}(?:[:.]\d{2})?(?:['’]?(?:de|da|te|ta))?) (?:bana )?(?<note>.+?) (?:için )?(?:beni |bana )?(?:hatırlat|uyar|haber ver)$")]
    private static partial Regex RecurringRequest();

    [GeneratedRegex(@"^(?:(?<n>[\p{L}\d]+(?: [\p{L}]+)?) dakika(?:lık)? )?(?<k>yağmur|beyaz gürültü|pembe gürültü|kahverengi gürültü|odak|dalga|deniz|okyanus|şömine|rüzgar|rüzgâr)(?: sesi(?:ni)?)? aç$")]
    private static partial Regex AmbientStart();

    [GeneratedRegex(@"^(?:yağmur|ortam|arka plan|odak|dalga|deniz|okyanus|şömine|rüzgar|rüzgâr) sesini (?:kapat|durdur|kes)$|^(?:beyaz |pembe |kahverengi )?gürültüyü (?:kapat|durdur|kes)$|^(?:ortam|arka plan) seslerini (?:kapat|durdur)$")]
    private static partial Regex AmbientStop();

    [GeneratedRegex(@"^(?:son )?(?:kopyaladıklarım|kopyaladığım şeyler|kopyaladıklarımı göster)(?: neler| ne)?$|^pano geçmişi(?:ni göster| ne| neler)?$")]
    private static partial Regex ClipboardList();

    [GeneratedRegex(@"^(?:(?<n>\d+|bir|iki|üç|dört|beş|altı|yedi|sekiz|dokuz|on)(?:\.|['’]?(?:inci|ıncı|uncu|üncü|nci|ncı|ncu|ncü|ci|cı|cu|cü))? )?(?:kopyaladığımı|kopyalananı) yapıştır$|^(?<o>bir|iki|üç) önce kopyaladığımı yapıştır$")]
    private static partial Regex ClipboardPaste();

    [GeneratedRegex(@"^(?:tekrar et|tekrarla|tekrar söyle|bir daha söyle|ne dedin|ne demiştin|anlamadım,?(?: tekrar(?: et)?)?|son söylediğini tekrarla|duyamadım)$")]
    private static partial Regex RepeatRequest();

    [GeneratedRegex(@"^(?<x>[\p{L}\d .+-]{2,40}?)(?:['’]?(?:yı|yi|yu|yü|ı|i|u|ü|nı|ni|nu|nü))? kapat$")]
    private static partial Regex CloseRequest();

    // "100 dolar kaç lira", "250 euro ne kadar eder", "1000 lira kaç dolar", "50 doları liraya çevir"
    [GeneratedRegex(@"^(?<a>[\d.,]+|[\p{L}]+(?: [\p{L}]+){0,2}) (?<from>dolar|euro|avro|sterlin|lira|tl|frank|yen|ruble)(?:['’]?(?:ı|i|u|ü|yı|yi|yu|yü))? (?:kaç|ne kadar) (?<to>türk lirası|lira|tl|dolar|euro|avro|sterlin)(?: eder| yapar)?$|^(?<a>[\d.,]+|[\p{L}]+(?: [\p{L}]+){0,2}) (?<from>dolar|euro|avro|sterlin|lira|tl)(?:['’]?(?:ı|i|u|ü|yı|yi|yu|yü))? (?<to>lira|tl|dolar|euro|avro|sterlin)(?:ya|ye|a|e) çevir$|^(?<a>[\d.,]+|[\p{L}]+(?: [\p{L}]+){0,2}) (?<from>dolar|euro|avro|sterlin|frank|yen|ruble)(?:['’]?(?:ı|i|u|ü|yı|yi|yu|yü))? (?:kaç para|kaç|ne kadar)(?: eder| yapar| tutar)?$")]
    private static partial Regex ConversionRequest();
}
