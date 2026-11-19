using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Kur alarmı, uyku saati hatırlatması ve bekleyen işler için sabah dürtmesi.</summary>
public sealed partial class AssistantTools
{
    private DateTime _ratesChecked;
    private DateOnly _bedWarned;
    private DateOnly _bedLate;

    // ---------- Kur alarmı ----------

    private async Task<QuickReply> RateAlertCommandAsync(string action, string currency, string levelText, string direction)
    {
        switch (Simplify(action))
        {
            case "ekle":
                if (!double.TryParse(levelText, NumberStyles.Float, CultureInfo.InvariantCulture, out var level) || level <= 0)
                {
                    return new QuickReply("Eşiği anlayamadım; ör. \"dolar 45 lirayı geçince haber ver\".");
                }

                var code = CurrencyCodes(currency).FirstOrDefault() ?? "USD";
                bool? above = Simplify(direction) switch { "ust" => true, "alt" => false, _ => null };
                string now = "";
                try
                {
                    var (rates, _) = await GetRatesAsync().ConfigureAwait(false);
                    if (rates.TryGetValue(code, out var rate))
                    {
                        above ??= level > rate;
                        now = $" Şu an {rate.ToString(rate >= 10 ? "0.00" : "0.000", Turkish)} lira.";
                    }
                }
                catch (HttpRequestException)
                {
                    // Kur alınamazsa yön söylenene göre (söylenmediyse "geçince") kurulur.
                }

                var alert = new RateAlert(code, level, above ?? true);
                Settings.RateAlerts.RemoveAll(item => item.Currency == code);
                Settings.RateAlerts.Add(alert);
                SaveSettings?.Invoke(Settings);
                ledger.Record(DataFlow.Local, "Kur alarmı", $"{CurrencyLabel(code)} {level} TL {(alert.Above == true ? "üstü" : "altı")}");
                return new QuickReply($"Tamam, {CurrencyLabel(code)} {level.ToString("0.##", Turkish)} liranın {(alert.Above == true ? "üstüne çıkınca" : "altına inince")} haber veririm.{now} Kuru 15 dakikada bir kontrol ederim.");
            case "sil":
                var count = Settings.RateAlerts.Count;
                Settings.RateAlerts.Clear();
                SaveSettings?.Invoke(Settings);
                return new QuickReply(count == 0 ? "Kurulu kur alarmı yok." : "Kur alarmlarını kaldırdım.");
            default:
                return new QuickReply(Settings.RateAlerts.Count == 0
                    ? "Kurulu kur alarmı yok. \"Dolar 45 lirayı geçince haber ver\" diyebilirsin."
                    : string.Join(" ", Settings.RateAlerts.Select(item => $"{TurkishGrammar.Capitalize(CurrencyLabel(item.Currency))} {item.Level.ToString("0.##", Turkish)} liranın {(item.Above == true ? "üstüne çıkınca" : "altına inince")}.")) + " haber vereceğim.");
        }
    }

    /// <summary>15 dakikada bir kurlar kontrol edilir; eşiği geçen alarm söylenir ve silinir. Yoksa null.</summary>
    public async Task<string?> RateAlertCheckAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (Settings.RateAlerts.Count == 0 || now - _ratesChecked < TimeSpan.FromMinutes(15))
        {
            return null;
        }

        _ratesChecked = now;
        var (rates, _) = await GetRatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        foreach (var alert in Settings.RateAlerts.ToList())
        {
            if (!rates.TryGetValue(alert.Currency, out var rate) || (alert.Above == true ? rate < alert.Level : rate > alert.Level))
            {
                continue;
            }

            Settings.RateAlerts.Remove(alert);
            SaveSettings?.Invoke(Settings);
            ledger.Record(DataFlow.Local, "Kur alarmı", $"{CurrencyLabel(alert.Currency)} eşiği geçti: {rate:0.00}");
            return $"Kur alarmı: {CurrencyLabel(alert.Currency)} {rate.ToString("0.00", Turkish)} lira oldu; {alert.Level.ToString("0.##", Turkish)} liranın {(alert.Above == true ? "üstüne çıktı" : "altına indi")}.";
        }

        return null;
    }

    // ---------- Uyku saati ----------

    private QuickReply BedTimeCommand(string action, string timeText)
    {
        switch (Simplify(action))
        {
            case "ayarla":
                if (BedTime.Parse(timeText) is not { } bed)
                {
                    return new QuickReply("Saati anlayamadım; ör. \"uyku saatim 23:30\".");
                }

                Settings.BedTime = bed.ToString("HH:mm", CultureInfo.InvariantCulture);
                SaveSettings?.Invoke(Settings);
                return new QuickReply($"Tamam, uyku saatin {Settings.BedTime}. Yarım saat önce ekranları bırakmayı hatırlatırım; çok geç kalırsan bir kez daha söylerim.");
            case "kapat":
                Settings.BedTime = "";
                SaveSettings?.Invoke(Settings);
                return new QuickReply("Uyku saati hatırlatmasını kapattım.");
            default:
                return new QuickReply(Settings.BedTime.Length == 0
                    ? "Bir uyku saati belirlemedin. \"Uyku saatim 23:30\" dersen yarım saat önce hatırlatırım."
                    : $"Uyku saatin {Settings.BedTime}; {TurkishGrammar.Duration(Math.Max(0, Math.Round(BedTime.Until(BedTime.Parse(Settings.BedTime)!.Value, DateTime.Now).TotalMinutes)))} var.");
        }
    }

    /// <summary>
    /// Uyku saatinden yarım saat önce bir kez ("Yarım saat sonra uyku saatin…"), uyku saatini yarım saat geçtiği hâlde
    /// bilgisayar başındaysan bir kez daha söylenecek cümle; yoksa null.
    /// </summary>
    public string? BedTimeNudge(DateTime now)
    {
        if (BedTime.Parse(Settings.BedTime) is not { } bed)
        {
            return null;
        }

        var left = BedTime.Until(bed, now);
        var night = DateOnly.FromDateTime(now + left);
        if (left > TimeSpan.FromMinutes(25) && left <= TimeSpan.FromMinutes(31) && _bedWarned != night)
        {
            _bedWarned = night;
            return $"Yarım saat sonra uyku saatin. Ekranları yavaş yavaş bırakmaya ne dersin?" + (Todos?.Open.Count > 0 ? " Yarına kalan işleri istersen sabah birlikte planlarız." : "");
        }

        if (left <= TimeSpan.FromMinutes(-30) && left > TimeSpan.FromMinutes(-90) && _bedLate != night)
        {
            _bedLate = night;
            return "Uyku saatini yarım saati geçti. Yarın için dinlenmen iyi olur; iyi geceler!";
        }

        return null;
    }

    // ---------- Bekleyen işler ----------

    /// <summary>Sabah karşılamasına: 3 günden uzun bekleyen bir iş varsa ona küçük bir odak önerisi.</summary>
    private void StaleTodoMorning(List<string> parts, DateTime now)
    {
        if (Todos is not { } todos || todos.Open.Where(item => now - item.Created >= TimeSpan.FromDays(3)).OrderBy(item => item.Created).FirstOrDefault() is not { } stale)
        {
            return;
        }

        var days = (int)(now.Date - stale.Created.Date).TotalDays;
        parts.Add($"\"{stale.Text}\" {days} gündür bekliyor; bugün ona 25 dakika ayırmaya ne dersin?" + (todos.Open.Count >= 3 ? " İstersen \"günümü planla\" de, işleri güne yayayım." : ""));
    }
}
