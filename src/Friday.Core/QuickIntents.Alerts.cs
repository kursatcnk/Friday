using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Kur eşiği uyarısı: birim ("USD"), eşik (TL) ve yön (üstüne çıkınca mı, altına inince mi; null: şimdiki kura göre).</summary>
public sealed record RateAlert(string Currency, double Level, bool? Above);

/// <summary>Uyku saati: "23:30"; değer yoksa kapalı.</summary>
public static class BedTime
{
    /// <summary>"23:30", "23", "gece 12", "00.30" → saat; anlaşılmazsa null.</summary>
    public static TimeOnly? Parse(string text)
    {
        var match = Regex.Match(text.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR")), @"(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?");
        if (!match.Success || !int.TryParse(match.Groups["h"].Value, CultureInfo.InvariantCulture, out var hour))
        {
            return null;
        }

        var minute = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        if (hour == 24)
        {
            hour = 0;
        }

        return hour is >= 0 and <= 23 && minute is >= 0 and <= 59 ? new TimeOnly(hour, minute) : null;
    }

    /// <summary>Uyku saatine kalan süre (geçtiyse eksi); gece yarısını aşan saatlerde de doğru (−12 sa, +12 sa].</summary>
    public static TimeSpan Until(TimeOnly bed, DateTime now)
    {
        var delta = bed.ToTimeSpan() - now.TimeOfDay;
        if (delta <= TimeSpan.FromHours(-12))
        {
            delta += TimeSpan.FromDays(1);
        }
        else if (delta > TimeSpan.FromHours(12))
        {
            delta -= TimeSpan.FromDays(1);
        }

        return delta;
    }
}

/// <summary>Kur alarmı ve uyku saati.</summary>
public static partial class QuickIntents
{
    private static QuickCommand? Alerts(string sentence)
    {
        if (RateAlertList().IsMatch(sentence))
        {
            return Command("doviz_alarmi", ("islem", "listele"));
        }

        if (RateAlertCancel().IsMatch(sentence))
        {
            return Command("doviz_alarmi", ("islem", "sil"));
        }

        if (RateAlertRequest().Match(sentence) is { Success: true } alert && ParseAmount(alert.Groups["n"].Value) is { } level && level > 0)
        {
            var direction = alert.Groups["up"].Success ? "ust" : alert.Groups["down"].Success ? "alt" : "";
            return Command("doviz_alarmi", ("islem", "ekle"), ("birim", alert.Groups["c"].Value), ("deger", level.ToString("0.####", CultureInfo.InvariantCulture)), ("yon", direction));
        }

        if (BedTimeOff().IsMatch(sentence))
        {
            return Command("uyku_saati", ("islem", "kapat"));
        }

        if (BedTimeQuestion().IsMatch(sentence))
        {
            return Command("uyku_saati", ("islem", "durum"));
        }

        return BedTimeSet().Match(sentence) is { Success: true } bed && BedTime.Parse(bed.Groups["t"].Value) is { } time
            ? Command("uyku_saati", ("islem", "ayarla"), ("saat", time.ToString("HH:mm", CultureInfo.InvariantCulture)))
            : null;
    }

    // "dolar 45 lirayı geçince haber ver", "euro 50'nin altına düşünce söyle", "sterlin 60 olunca uyar"
    [GeneratedRegex(@"^(?<c>dolar|euro|avro|sterlin|frank)(?: kuru)? (?<n>[\d.,]+)(?:['’]?\p{L}{1,4})?(?: (?:lira|tl)['’]?\p{L}{0,4})? (?:(?<up>geçince|aşınca|üstüne çıkınca|çıkınca)|(?<down>altına (?:düşünce|inince)|düşünce|inince)|olunca) (?:bana )?(?:haber ver|söyle|uyar|bildir|haber et)$")]
    private static partial Regex RateAlertRequest();

    [GeneratedRegex(@"^(?:döviz|kur|dolar|euro) alarmlarım(?: neler| ne)?$|^kur alarmları(?:mı)?(?: göster| söyle)?$")]
    private static partial Regex RateAlertList();

    [GeneratedRegex(@"^(?:döviz|kur|dolar|euro) alarm(?:ını|larını|ı) (?:iptal et|sil|kaldır|kapat)$")]
    private static partial Regex RateAlertCancel();

    // "uyku saatim 23:30", "uyku saatimi 23'e ayarla", "her gece 23:30'da yatmak istiyorum"
    [GeneratedRegex(@"^uyku saat(?:im|imi)? (?:gece )?(?<t>\d{1,2}(?:[:.]\d{2})?)(?:['’]?\p{L}{1,3})?(?: olsun| yap| ayarla| olarak ayarla)?$|^(?:her gece |genelde )?(?:gece )?(?<t>\d{1,2}(?:[:.]\d{2})?)['’]?(?:de|da|te|ta) (?:yatmak istiyorum|yatıyorum|uyumak istiyorum)$")]
    private static partial Regex BedTimeSet();

    [GeneratedRegex(@"^uyku (?:saati )?(?:hatırlatmasını|hatırlatıcısını|uyarısını) (?:kapat|iptal et|kaldır)$|^uyku saatimi (?:kaldır|sil)$")]
    private static partial Regex BedTimeOff();

    [GeneratedRegex(@"^uyku saatim (?:ne|kaç|kaçta)$|^kaçta yatmalıyım$")]
    private static partial Regex BedTimeQuestion();
}
