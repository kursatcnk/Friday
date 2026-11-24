using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Alışkanlık takipçisi ve harcama kaydı.</summary>
public static partial class QuickIntents
{
    /// <summary>"80", "seksen", "1.250,5", "15 bin", "iki buçuk bin" → tutar; anlaşılmazsa null.</summary>
    public static double? ParseMoney(string text)
    {
        var clean = text.Trim().ToLower(Turkish);
        var mixed = MixedAmount().Match(clean);
        if (mixed.Success && ParseAmount(mixed.Groups["n"].Value) is { } head)
        {
            var scale = mixed.Groups["u"].Value == "bin" ? 1000 : 100;
            var rest = mixed.Groups["r"].Value.Trim();
            if (rest.Length == 0)
            {
                return head * scale;
            }

            return ParseAmount(rest) is { } tail ? (head * scale) + tail : null;
        }

        return ParseAmount(clean);
    }

    /// <summary>"kahveye 80 lira harcadım" → (80, "kahveye"); harcama cümlesi değilse null.</summary>
    public static (double Amount, string Note)? ParseExpense(string sentence)
    {
        if (SpentOn().Match(sentence) is { Success: true } spent)
        {
            // "market alışverişine iki yüz elli" → sondan geriye en uzun okunabilir tutar, kalanı açıklama.
            var words = spent.Groups["pre"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var start = 0; start < words.Length; start++)
            {
                if (ParseMoney(string.Join(' ', words[start..])) is { } amount && amount > 0)
                {
                    return (amount, LeadingTime().Replace(string.Join(' ', words[..start]), "").Trim());
                }
            }

            return null;
        }

        if (BoughtFor().Match(sentence) is { Success: true } bought && ParseMoney(bought.Groups["a"].Value) is { } price && price > 0)
        {
            return (price, bought.Groups["n"].Value.Trim());
        }

        return ExpenseAdd().Match(sentence) is { Success: true } add && ParseMoney(add.Groups["a"].Value) is { } value && value > 0
            ? (value, add.Groups["n"].Value.Trim())
            : null;
    }

    private static QuickCommand? Life(string sentence)
    {
        if (HabitAdd().Match(sentence) is { Success: true } add && add.Groups["x"].Value.Trim().Length > 1)
        {
            return Command("aliskanlik", ("islem", "ekle"), ("ad", add.Groups["x"].Value.Trim()));
        }

        if (SpecialDaysQuestion().IsMatch(sentence))
        {
            return Command("ozel_gunler");
        }

        if (HabitList().IsMatch(sentence))
        {
            return Command("aliskanlik", ("islem", "listele"));
        }

        if (HabitRemove().Match(sentence) is { Success: true } remove)
        {
            return Command("aliskanlik", ("islem", "sil"), ("ad", remove.Groups["x"].Value.Trim()));
        }

        if (ExpenseUndo().IsMatch(sentence))
        {
            return Command("harcama", ("islem", "sil_son"));
        }

        if (ExpenseExport().IsMatch(sentence))
        {
            return Command("harcama", ("islem", "disa_aktar"));
        }

        if (BudgetSet().Match(sentence) is { Success: true } budget && ParseMoney(budget.Groups["a"].Value) is { } limit)
        {
            return Command("harcama", ("islem", "butce"), ("tutar", limit.ToString("0.##", CultureInfo.InvariantCulture)));
        }

        if (ExpenseQuestion().Match(sentence) is { Success: true } question)
        {
            return Command("harcama", ("islem", "ozet"), ("donem", PeriodKey(question.Groups["p"].Value)), ("kategori", question.Groups["c"].Value.Trim()));
        }

        if (ExpenseReport().Match(sentence) is { Success: true } report)
        {
            return Command("harcama", ("islem", "ozet"), ("donem", PeriodKey(report.Groups["p"].Value)));
        }

        return ParseExpense(sentence) is { } expense
            ? Command("harcama", ("islem", "ekle"), ("tutar", expense.Amount.ToString("0.##", CultureInfo.InvariantCulture)), ("aciklama", expense.Note))
            : null;
    }

    /// <summary>"Bugün spor yaptım": öyle bir alışkanlık varsa işaretlenir; yoksa soru dil modeline gider.</summary>
    private static QuickCommand? HabitDone(string sentence)
    {
        if (HabitUndo().Match(sentence) is { Success: true } undo && undo.Groups["x"].Value.Trim().Length > 1)
        {
            return new QuickCommand("aliskanlik", new Dictionary<string, string> { ["islem"] = "geri_al", ["ad"] = undo.Groups["x"].Value.Trim() }, Tentative: true);
        }

        return HabitMark().Match(sentence) is { Success: true } done && done.Groups["x"].Value.Trim().Length > 1
            ? new QuickCommand("aliskanlik", new Dictionary<string, string> { ["islem"] = "yaptim", ["ad"] = done.Groups["x"].Value.Trim() }, Tentative: true)
            : null;
    }

    private static string PeriodKey(string spoken) => spoken.Trim() switch
    {
        "bugün" or "bugünkü" => "bugun",
        "dün" or "dünkü" => "dun",
        "bu hafta" or "bu haftaki" or "haftalık" => "hafta",
        "geçen ay" or "geçen ayki" => "gecen_ay",
        _ => "ay"
    };

    [GeneratedRegex(@"^(?<n>\d+(?:[.,]\d+)?) (?<u>bin|yüz)(?: (?<r>.+))?$")]
    private static partial Regex MixedAmount();

    [GeneratedRegex(@"^(?:bugün|az önce|şimdi|demin|sabah|öğlen|akşam)\s*")]
    private static partial Regex LeadingTime();

    // "kahveye 80 lira harcadım", "market alışverişine iki yüz elli lira verdim", "200 tl harcadım"
    [GeneratedRegex(@"^(?<pre>.+?) (?:lira|tl|₺)(?:lık)? (?:harcadım|harcadık|verdim|ödedim|gitti|bayıldım)$")]
    private static partial Regex SpentOn();

    // "80 liralık kahve aldım", "450 liraya market alışverişi yaptım"
    [GeneratedRegex(@"^(?:bugün |az önce )?(?<a>[\d.,]+(?: bin)?|[\p{L}]+(?: [\p{L}]+){0,3}?) (?:lira|tl)(?:lık|ya)? (?<n>.+?) (?:aldım|harcadım|ödedim|yaptım|ısmarladım|söyledim|sipariş ettim)$")]
    private static partial Regex BoughtFor();

    // "harcama ekle: 120 lira taksi"
    [GeneratedRegex(@"^harcama (?:ekle|kaydet|gir|yaz)[:,]? (?<a>[\d.,]+(?: bin)?|[\p{L}]+(?: [\p{L}]+){0,3}?) (?:lira|tl)(?: (?<n>.+))?$")]
    private static partial Regex ExpenseAdd();

    // "bu ay ne kadar harcadım", "bu hafta kahveye ne kadar harcadım", "dün kaç lira harcadım"
    [GeneratedRegex(@"^(?:(?<p>bugün|dün|bu hafta|bu ay|geçen ay) )?(?:(?<c>[\p{L}]+?)['’]?(?:ya|ye|na|ne|a|e) )?(?:toplam )?(?:ne kadar|kaç lira|kaç para|kaç tl) (?:para )?(?:harcadım|harcamışım|harcadık|gitti|verdim|verdik|ödedim|ödedik)$")]
    private static partial Regex ExpenseQuestion();

    [GeneratedRegex(@"^(?:(?<p>bugünkü|dünkü|bu haftaki|bu ayki|geçen ayki|aylık|haftalık) )?harcamalar(?:ım)?(?:ı|ımı)?(?: neler| ne kadar| ne durumda| özeti| göster| oku| söyle)?$|^harcama özeti$|^bütçem(?: ne durumda| nasıl gidiyor| nasıl| ne kadar kaldı)$|^(?:bu ay )?ne kadar bütçem kaldı$")]
    private static partial Regex ExpenseReport();

    [GeneratedRegex(@"^(?:aylık )?bütçe(?:m|mi|yi)? (?<a>[\d.,]+(?: bin)?|[\p{L}]+(?: [\p{L}]+){0,3}?) (?:lira|tl)(?:['’]?(?:ye|ya|e|a))?(?: olsun| yap| ayarla| olarak ayarla| çek)?$")]
    private static partial Regex BudgetSet();

    [GeneratedRegex(@"^(?:son|o|bu) harcama(?:yı|mı)? (?:sil|geri al|iptal et|kaldır)$|^son harcamayı yanlış (?:girdim|söyledim)$")]
    private static partial Regex ExpenseUndo();

    [GeneratedRegex(@"^harcama(?:lar)?(?:ı|ımı)? (?:dışa aktar|excel['’]?e aktar|tabloya aktar|csv olarak kaydet|dosyaya kaydet)$")]
    private static partial Regex ExpenseExport();

    // "alışkanlık ekle: kitap okumak", "meditasyonu alışkanlıklarıma ekle", "kitap okuma alışkanlığı ekle"
    [GeneratedRegex(@"^(?:yeni alışkanlık|alışkanlık ekle|alışkanlıklarıma ekle)[:,]?\s+(?<x>.+)$|^(?<x>.+?) (?:alışkanlıklarıma|alışkanlık listeme|alışkanlık olarak) ekle$|^(?<x>.+?) alışkanlığı(?:nı)? (?:ekle|aç|takip et|başlat)$")]
    private static partial Regex HabitAdd();

    [GeneratedRegex(@"^alışkanlıklarım(?: neler| nasıl(?: gidiyor)?| ne durumda)?$|^alışkanlıklarımı (?:oku|söyle|göster)$|^alışkanlık (?:durumum|serim|serilerim|raporu|raporum)$|^serilerim(?: nasıl)?$|^bugün (?:hangi alışkanlıkları|neleri) yapmadım$")]
    private static partial Regex HabitList();

    [GeneratedRegex(@"^(?:yaklaşan |önümüzdeki )?(?:özel|önemli) günler(?:im)?(?: neler| ne| var mı)?$|^yaklaşan (?:doğum günleri|özel günler|önemli günler|bayramlar)(?: neler| var mı)?$|^(?:yakında )?kimin doğum günü (?:var|yaklaşıyor)$")]
    private static partial Regex SpecialDaysQuestion();

    [GeneratedRegex(@"^(?<x>.+?) alışkanlığını (?:sil|kaldır|bırak|takip etme)$")]
    private static partial Regex HabitRemove();

    // "bugün spor yaptım", "kitap okudum", "ilacımı aldım", "yürüyüşe gittim"
    [GeneratedRegex(@"^(?:bugün |az önce |şimdi |sonunda |demin )?(?<x>.+?) (?:yaptım|ettim|okudum|gittim|koştum|yürüdüm|yazdım|izledim|çalıştım|aldım|uyudum|kalktım|dinledim|çizdim|pratik yaptım)$")]
    private static partial Regex HabitMark();

    [GeneratedRegex(@"^(?:bugün )?(?<x>.+?) (?:yapmadım|okumadım|gitmedim|koşmadım|almadım|çalışmadım)(?:,? yanlış(?: işaretledim| oldu)?)?$|^(?<x>.+?) işaretini (?:kaldır|geri al)$")]
    private static partial Regex HabitUndo();
}
