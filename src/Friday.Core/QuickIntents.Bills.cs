using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Sabit giderler (kira, fatura, abonelik) ve uygulama süre sınırları.</summary>
public static partial class QuickIntents
{
    private static QuickCommand? Bills(string sentence)
    {
        if (BillList().IsMatch(sentence))
        {
            return Command("sabit_gider", ("islem", "listele"));
        }

        if (BillRemove().Match(sentence) is { Success: true } remove)
        {
            return Command("sabit_gider", ("islem", "sil"), ("ad", remove.Groups["n"].Value.Trim()));
        }

        foreach (var pattern in new[] { BillDayFirst(), BillAmountFirst() })
        {
            if (pattern.Match(sentence) is { Success: true } bill && ParseMoney(bill.Groups["a"].Value) is { } amount && amount > 0
                && int.TryParse(bill.Groups["d"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) && day is >= 1 and <= 31)
            {
                return Command("sabit_gider", ("islem", "ekle"), ("ad", bill.Groups["n"].Value.Trim()),
                    ("tutar", amount.ToString("0.##", CultureInfo.InvariantCulture)), ("gun", day.ToString(CultureInfo.InvariantCulture)));
            }
        }

        if (LimitList().IsMatch(sentence))
        {
            return Command("sure_siniri", ("islem", "listele"));
        }

        if (LimitRemove().Match(sentence) is { Success: true } unlimit)
        {
            return Command("sure_siniri", ("islem", "sil"), ("ad", unlimit.Groups["a"].Value.Trim()));
        }

        if (LimitSet().Match(sentence) is { Success: true } limit && ParseNumber(limit.Groups["n"].Value) is { } value && value > 0)
        {
            var minutes = limit.Groups["u"].Value == "saat" ? value * 60 : value;
            return Command("sure_siniri", ("islem", "ekle"), ("ad", limit.Groups["a"].Value.Trim()), ("dakika", minutes.ToString("0", CultureInfo.InvariantCulture)));
        }

        return null;
    }

    /// <summary>"Kirayı ödedim": kayıtlı bir sabit giderse harcamalara işlenir; değilse yapılacaklarda aranır, o da yoksa dil modeline gider.</summary>
    private static QuickCommand? BillPaid(string sentence) =>
        BillPay().Match(sentence) is { Success: true } paid && paid.Groups["n"].Value.Trim().Length > 2
            ? new QuickCommand("sabit_gider", new Dictionary<string, string> { ["islem"] = "odendi", ["ad"] = paid.Groups["n"].Value.Trim() }, Tentative: true)
            : null;

    // "kira her ayın 5'inde 15 bin lira", "netflix aboneliğim her ayın 12'si 230 lira"
    [GeneratedRegex(@"^(?<n>.+?) (?:faturam |faturası |aboneliğim |aidatım |ödemem )?her ayın (?<d>\d{1,2})['’]?\p{L}* (?:günü )?(?<a>[\d.,]+(?: bin)?|[\p{L}]+(?: [\p{L}]+){0,3}?) (?:lira|tl)$")]
    private static partial Regex BillDayFirst();

    // "netflix aboneliğim ayda 230 lira, her ayın 12'si", "internet faturam aylık 450 tl her ayın 20'sinde"
    [GeneratedRegex(@"^(?<n>.+?) (?:faturam |faturası |aboneliğim |aidatım |ödemem )?(?:ayda|aylık) (?<a>[\d.,]+(?: bin)?|[\p{L}]+(?: [\p{L}]+){0,3}?) (?:lira|tl),? (?:her ayın )?(?<d>\d{1,2})['’]?\p{L}*(?: günü)?$")]
    private static partial Regex BillAmountFirst();

    [GeneratedRegex(@"^(?:sabit giderlerim|aboneliklerim|faturalarım|düzenli ödemelerim|aylık sabit giderlerim)(?: neler| ne kadar| ne)?$|^sabit giderler(?:imi)? (?:göster|söyle|oku)$")]
    private static partial Regex BillList();

    [GeneratedRegex(@"^(?<n>.+?) (?:aboneliğini|faturasını|ödemesini|sabit giderini) (?:sil|kaldır|listeden çıkar)$")]
    private static partial Regex BillRemove();

    // "kirayı ödedim", "elektrik faturasını ödedim", "netflix ödendi"
    [GeneratedRegex(@"^(?:bugün )?(?<n>.+?) (?:ödedim|yatırdım|ödendi|ödemesini yaptım)$")]
    private static partial Regex BillPay();

    // "youtube'a günde 1 saat sınır koy", "instagram için günlük 30 dakika limit"
    [GeneratedRegex(@"^(?<a>[\p{L}\d .+-]{2,30}?)(?:['’]?(?:ya|ye|na|ne|a|e)| için) (?:günde |günlük )?(?<n>\d+|[\p{L}]+(?: [\p{L}]+)?) (?<u>dakika|saat)(?:lık)? (?:sınır|limit)(?: koy| ekle| ayarla| getir| olsun)?$")]
    private static partial Regex LimitSet();

    [GeneratedRegex(@"^(?<a>[\p{L}\d .+-]{2,30}?)(?:['’]?(?:nın|nin|nun|nün|ın|in|un|ün))? (?:sınırını|limitini|süre sınırını) (?:kaldır|sil)$")]
    private static partial Regex LimitRemove();

    [GeneratedRegex(@"^(?:süre sınırlarım|uygulama sınırlarım|sınırlarım|limitlerim|ekran süresi sınırlarım)(?: neler| ne)?$")]
    private static partial Regex LimitList();
}
