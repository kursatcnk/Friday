using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Kullanıcının takvimi: Google Takvim ya da Outlook'un gizli iCal (.ics) adresinden okunur, 15 dk saklanır.
/// Adres yalnızca settings.json'da durur. Etkinlikten 10 dk önce sesli haber verilir; takvim yoksa saat çalışmaz.
/// </summary>
public sealed class CalendarService(HttpClient http, PrivacyLedger ledger) : IDisposable
{
    private static readonly TimeSpan CacheLife = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AlertBefore = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly HashSet<string> _alerted = [];
    private string _url = "";
    private string? _ics;
    private DateTime _fetched;
    private DateTime _recorded;
    private Timer? _clock;

    /// <summary>Etkinlik yaklaştı ("10 dakika sonra: Haftalık toplantı.").</summary>
    public event EventHandler<string>? Alert;

    /// <summary>Takvim yenilendi; panel "Bugün" ekranını günceller.</summary>
    public event EventHandler? Changed;

    public bool IsConfigured
    {
        get
        {
            lock (_gate)
            {
                return _url.Length > 0;
            }
        }
    }

    public void Configure(string url)
    {
        var clean = Normalize(url);
        lock (_gate)
        {
            if (clean == _url)
            {
                return;
            }

            _url = clean;
            _ics = null;
            _alerted.Clear();
            _clock?.Dispose();
            _clock = clean.Length == 0 ? null : new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
        }
    }

    /// <summary>[from, to) aralığındaki etkinlikler; gerekirse takvimi yeniden indirir. Takvim yoksa ya da ulaşılamazsa boş.</summary>
    public async Task<IReadOnlyList<CalendarEvent>> EventsAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        await RefreshAsync(force: false, cancellationToken).ConfigureAwait(false);
        return Cached(from, to);
    }

    /// <summary>İnternete çıkmadan, son indirilen takvimden.</summary>
    public IReadOnlyList<CalendarEvent> Cached(DateTime from, DateTime to)
    {
        string? ics;
        lock (_gate)
        {
            ics = _ics;
        }

        return ics is null ? [] : Calendar.Parse(ics, from, to);
    }

    /// <summary>Ayarlardaki "Bağlantıyı dene": adresi indirir ve önümüzdeki 30 günün etkinlik sayısını söyler.</summary>
    public async Task<string> TestAsync(string url, CancellationToken cancellationToken)
    {
        var clean = Normalize(url);
        if (clean.Length == 0)
        {
            return "Önce takvimin .ics adresini yapıştır.";
        }

        try
        {
            var ics = await DownloadAsync(clean, cancellationToken).ConfigureAwait(false);
            if (!ics.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                return "Bu adres bir takvim (.ics) değil.";
            }

            var count = Calendar.Parse(ics, DateTime.Now, DateTime.Now.AddDays(30)).Count;
            return $"Takvim bağlandı: önümüzdeki 30 günde {count} etkinlik var.";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return "Takvime ulaşılamadı; adresi kontrol et.";
        }
    }

    private async Task RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        string url;
        lock (_gate)
        {
            url = _url;
            if (url.Length == 0 || (!force && _ics is not null && DateTime.Now - _fetched < CacheLife))
            {
                return;
            }
        }

        try
        {
            var ics = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (url != _url)
                {
                    return;
                }

                _ics = ics;
                _fetched = DateTime.Now;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            FridayLog.Warn($"Takvim okunamadı: {exception.Message}");
        }
    }

    private async Task<string> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var uri = new Uri(url);
        var ics = await http.GetStringAsync(uri, timeout.Token).ConfigureAwait(false);
        // Defter her indirmede değil, saatte bir kez yazılır.
        if (DateTime.Now - _recorded > TimeSpan.FromHours(1))
        {
            _recorded = DateTime.Now;
            ledger.Record(DataFlow.Outbound, "Takvim", $"{uri.Host} · takvim okundu (15 dk'da bir yenilenir)");
        }

        return ics;
    }

    private async Task TickAsync()
    {
        try
        {
            await RefreshAsync(force: false, CancellationToken.None).ConfigureAwait(false);
            var now = DateTime.Now;
            foreach (var item in Cached(now, now + AlertBefore + TimeSpan.FromMinutes(1)).Where(item => !item.AllDay && item.Start > now))
            {
                var key = $"{item.Title}|{item.Start:O}";
                lock (_gate)
                {
                    if (item.Start - now > AlertBefore || !_alerted.Add(key))
                    {
                        continue;
                    }
                }

                var minutes = Math.Max(1, (int)Math.Round((item.Start - now).TotalMinutes));
                Alert?.Invoke(this, $"{TurkishGrammar.Duration(minutes)} sonra: {item.Title}" + (item.Location is { } place ? $", {place}." : "."));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            FridayLog.Warn($"Takvim yoklaması: {exception.Message}");
        }
    }

    /// <summary>"webcal://…" → "https://…".</summary>
    private static string Normalize(string url)
    {
        var clean = url.Trim();
        return clean.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + clean["webcal://".Length..] : clean;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _clock?.Dispose();
            _clock = null;
        }
    }
}
