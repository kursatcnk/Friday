using System.Globalization;
using System.Text.Json.Nodes;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Hızlı yolun cevabı. Sessiz cevaplar (müziği durdurmak gibi) okunmaz, yalnızca ekranda görünür; Script varsa (nefes
/// egzersizi) cümleler aralarında beklenerek tek tek okunur.
/// </summary>
public sealed record QuickReply(string Text, bool Silent = false, IReadOnlyList<CoachStep>? Script = null)
{
    /// <summary>Kapsülün üstünde gösterilecek kart (hava, döviz, durum raporu…); yoksa null.</summary>
    public AnswerCard? Card { get; init; }
}

/// <summary>"Bugün" ekranındaki hava özeti.</summary>
public sealed record WeatherCard(string Place, int Temperature, string Description, int Max, int Min, int RainChance);

/// <summary>
/// Dil modelsiz hızlı yol: kalıbı belli komutlar burada çalışır ve sonucu doğrudan konuşulacak bir cümleye çevrilir.
/// Cümleler veriden kurulur; model olmadığı için uydurma ihtimali de yoktur.
/// </summary>
public sealed partial class AssistantTools
{
    private static readonly TimeSpan RateCacheLife = TimeSpan.FromMinutes(15);

    /// <summary>"bun", "pencere" gibi uygulama olmayan nesneler Başlat menüsünde aranmaz.</summary>
    private static readonly HashSet<string> NotApps =
    [
        "bu", "bun", "su", "sun", "o", "on", "hepsi", "pencere", "sekme", "sayfa", "dosya", "link", "baglanti", "video", "sarki",
        "muzik", "ses", "isik", "isiklar", "kapi", "perde", "ekran", "bilgisayar", "klima", "televizyon", "mod", "kamera on"
    ];

    private readonly object _rateLock = new();
    private IReadOnlyList<CoachStep>? _script;
    private string _lastRhythmText = "";
    private Task<(Dictionary<string, double> Rates, string Source)>? _rateTask;
    private DateTime _rateAt;

    /// <returns>Konuşulacak cevap; komut burada yapılamıyorsa null (soru dil modeline gider).</returns>
    public async Task<QuickReply?> RunQuickAsync(QuickCommand quick, CancellationToken cancellationToken)
    {
        quick = quick with { Arguments = Shielded(quick.Tool, quick.Arguments) };
        NoteUntrusted(quick.Tool);
        if (Rehearsal?.Invoke(quick.Tool, quick.Arguments) is { } staged)
        {
            return new QuickReply(staged);
        }

        string Arg(string key) => quick.Arguments.TryGetValue(key, out var value) ? value : "";
        _ = TakeCard();
        try
        {
            var reply = quick.Tool switch
            {
                "saat_ve_tarih" => new QuickReply(Arg("odak") switch
                {
                    "tarih" => $"Bugün {DateTime.Now.ToString("d MMMM, dddd", Turkish)}.",
                    "haftagunu" when TurkishTime.DayOf(Arg("tarih"), DateTime.Now) is { } day => TurkishTime.Weekday(day, DateOnly.FromDateTime(DateTime.Now)),
                    "dunya" when WorldClock.Describe(Arg("yer"), DateTime.UtcNow, TimeZoneInfo.Local) is { } world => world,
                    "ileri" when int.TryParse(Arg("gun"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) => TurkishTime.Offset(days, DateOnly.FromDateTime(DateTime.Now)),
                    "aralik" when TurkishTime.Between(Arg("bas"), Arg("son"), DateTime.Now) is { } between => between,
                    "yas" when TurkishTime.Age(Arg("dogum"), DateOnly.FromDateTime(DateTime.Now)) is { } age => age,
                    _ => $"Saat {DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)}."
                }),
                "hava_durumu" => new QuickReply(await WeatherSpeechAsync(Arg("sehir"), Arg("odak"), Arg("soru") == "yagmur", cancellationToken).ConfigureAwait(false)),
                "doviz" => Arg("miktar").Length > 0
                    ? await ConvertCurrencyAsync(Arg("miktar"), Arg("kaynak"), Arg("hedef")).ConfigureAwait(false)
                    : new QuickReply(await CurrencySpeechAsync(Arg("birim"), cancellationToken).ConfigureAwait(false)),
                "medya" => await MediaReplyAsync(Arg("komut"), Arg("deger")).ConfigureAwait(false),
                "zamanlayici" => Arg("islem") == "kalan" ? TimeLeft(DateTime.Now) : new QuickReply(TimerSpeech(Arg("dakika"), Arg("not"))),
                "uygulama_ac" => FindApp(Arg("ad")) is { } app ? OpenAndRecord(app) : null,
                "rutin" => await RoutineReplyAsync(Arg("ad"), cancellationToken).ConfigureAwait(false),
                "video_ac" => new QuickReply(await OpenVideoAsync(Arg("arama"), cancellationToken).ConfigureAwait(false)),
                "ritim" => RhythmCommand(Arg("islem"), Arg("dakika"), Arg("is"), Arg("donem")),
                "hatirlatici" when Arg("islem") == "ne_zaman" => ReminderCommand(Arg("islem"), "", Arg("metin")) is { Length: > 0 } when ? new QuickReply(when) : null,
                "hatirlatici" => new QuickReply(ReminderCommand(Arg("islem"), Arg("zaman"), Arg("metin"), Arg("tekrar"))),
                "not" => new QuickReply(NoteCommand(Arg("islem"), Arg("metin"))),
                "hesap" => SpokenMath.Answer(Arg("soru")) is { } answer ? new QuickReply(answer) : null,
                "gun_sayaci" => DayCountdown.Answer(Arg("hedef"), memory.Items, DateOnly.FromDateTime(DateTime.Now)) is { } left ? new QuickReply(left) : null,
                "durum_raporu" => await StatusReportAsync(cancellationToken).ConfigureAwait(false),
                "sistem" => SystemCommand(Arg("islem"), Arg("dakika")),
                "nobet" => await WatchCommandAsync(Arg("islem"), Arg("derece"), cancellationToken).ConfigureAwait(false),
                "sans" => LuckCommand(Arg("islem"), Arg("adet"), Arg("en_az"), Arg("en_cok")),
                "kronometre" => StopwatchCommand(Arg("islem")),
                "ozel_komut" => AddCustomCommand(Arg("ifade"), Arg("adim")),
                "tetik" => await TriggerReplyAsync(Arg("ad"), cancellationToken).ConfigureAwait(false),
                "secili" => await SelectionAsync(Arg("islem"), Arg("dil"), cancellationToken).ConfigureAwait(false),
                "yaz" => TypeText(Arg("metin")),
                "pencere" => WindowCommand(Arg("islem")),
                "masa_duzeni" => await WorkspaceCommandAsync(Arg("islem"), Arg("ad"), cancellationToken).ConfigureAwait(false),
                "ekran_suresi" => ScreenTimeCommand(Arg("gun"), Arg("uygulama")),
                "rahatsiz_etme" => new QuickReply(QuietCommand(Arg("islem"), Arg("dakika"))),
                "kosullu_hatirlatici" => new QuickReply(TriggeredReminder(Arg("kosul"), Arg("metin"))),
                "liste" => new QuickReply(ListCommand(Arg("islem"), Arg("ad"), Arg("oge"))),
                "haftalik_ozet" => new QuickReply(WeeklyReview()),
                "sonra_oku" => new QuickReply(ReadLater(Arg("islem"))),
                "sarki_kaydet" => new QuickReply(await SaveSongAsync().ConfigureAwait(false)),
                "odak_hedefi" => new QuickReply(FocusGoal(Arg("dakika"))),
                "pomodoro" => new QuickReply(PomodoroCommand(Arg("islem"), Arg("adet"))),
                "ruh_hali" => new QuickReply(MoodCommand(Arg("puan"), Arg("kelime"))),
                "temizlik" => await Task.Run(CleanupOffer, cancellationToken).ConfigureAwait(false),
                "kontrol_listesi" => StartChecklist(Arg("ad")),
                "kontrol_cevap" => ChecklistAnswer(Arg("metin")),
                "disk_analizi" => new QuickReply(await Task.Run(DiskReport, cancellationToken).ConfigureAwait(false)),
                "gunluk" => new QuickReply(JournalCommand(Arg("islem"), Arg("metin"), Arg("donem"))),
                "ertele" => Snooze(Arg("dakika")),
                "haber" => await NewsAsync(Arg("kategori"), cancellationToken).ConfigureAwait(false),
                "rapor" => ResearchCommand(Arg("islem"), Arg("konu")),
                "takvim" => await CalendarCommandAsync(Arg("gun"), cancellationToken).ConfigureAwait(false),
                "indirilenler" => DownloadsCommand(Arg("islem")),
                "gun_sonu" => await DayEndAsync(cancellationToken).ConfigureAwait(false),
                "onay" => await ConfirmAsync(Arg("cevap") == "evet", cancellationToken).ConfigureAwait(false),
                "yapilacak" => TodoCommand(Arg("islem"), Arg("metin")),
                "ortam" => AmbientCommand(Arg("islem"), Arg("tur"), Arg("dakika")),
                "pano_gecmisi" => ClipboardHistoryCommand(Arg("islem"), Arg("sira")),
                "uygulama_kapat" => CloseAppCommand(Arg("ad")),
                "aliskanlik" => HabitCommand(Arg("islem"), Arg("ad")),
                "ozel_gunler" => SpecialDaysCommand(),
                "haftalik_rapor" => WeeklyReportFile(),
                "ne_yapayim" => NextAction(),
                "geri_al" => UndoCommand(Arg("islem")),
                "duzeltme" => CorrectionCommand(Arg("islem")),
                "aktif_saatler" => ActiveHoursAnswer(),
                "kulak" => HearingCommand(Arg("islem"), Arg("ad")),
                "uslup" => StyleCommand(Arg("islem")),
                "kisayol_ogrenimi" => ShortcutCommand(Arg("islem")),
                "o_gun" => DayRecall(Arg("gun")),
                "kaldigim_yer" => Resume(),
                "neden" => WhyCommand(),
                "en_son" => LastTimeCommand(Arg("konu")),
                "verimlilik" => ProductivityReport(),
                "okunus" => PronounceCommand(Arg("islem"), Arg("kelime"), Arg("okunus")),
                "ogrendiklerin" => LearnedReport(),
                "durtme" => NudgeCommand(Arg("islem")),
                "fis" => await ReceiptAsync(cancellationToken).ConfigureAwait(false),
                "her_yerde_ara" => SearchEverywhere(Arg("sorgu")),
                "kacirdiklarim" => MissedAnnouncements(),
                "kelime_cevir" => await WordMeaningAsync(Arg("islem"), Arg("kelime"), cancellationToken).ConfigureAwait(false),
                "ekran_metni" => await ScreenTextAsync(cancellationToken).ConfigureAwait(false),
                "onumdeki" => new QuickReply(await FrontAsync(Arg("islem"), Arg("soru"), cancellationToken).ConfigureAwait(false)),
                "okuduklarim" => new QuickReply(await ReadingAsync(Arg("islem"), Arg("soru"), Arg("ad"), Arg("zaman"), cancellationToken).ConfigureAwait(false)),
                "yazi" => new QuickReply(await WriteAsync(Arg("islem"), Arg("icerik"), Arg("dil"), cancellationToken).ConfigureAwait(false)),
                "pano_kaydet" => SaveClipboard(Arg("islem"), Arg("ad")),
                "doviz_alarmi" => await RateAlertCommandAsync(Arg("islem"), Arg("birim"), Arg("deger"), Arg("yon")).ConfigureAwait(false),
                "uyku_saati" => BedTimeCommand(Arg("islem"), Arg("saat")),
                "sabit_gider" => BillCommand(Arg("islem"), Arg("ad"), Arg("tutar"), Arg("gun")),
                "sure_siniri" => LimitCommand(Arg("islem"), Arg("ad"), Arg("dakika")),
                "gun_plani" => await DayPlanAsync(cancellationToken).ConfigureAwait(false),
                "isle_odak" => FocusOnTask(Arg("is"), Arg("dakika")),
                "baglanti" => await NetworkCommandAsync(cancellationToken).ConfigureAwait(false),
                "pano_gorseli" => await ClipboardImageAsync(Arg("islem"), Arg("soru"), cancellationToken).ConfigureAwait(false),
                "uygulama_sesi" => await Task.Run(() => AppVolumeCommand(Arg("islem"), Arg("ad"), Arg("deger")), cancellationToken).ConfigureAwait(false),
                "kisaltma" => SnippetCommand(Arg("islem"), Arg("ad"), Arg("metin")),
                "serbest_cevap" => AnswerPending(Arg("metin")),
                "gunluk_sorusu" => new QuickReply(JournalQuestion(DateTime.Now, force: true) ?? "Günlük şu an hazır değil."),
                "harcama" => ExpenseCommand(Arg("islem"), Arg("tutar"), Arg("aciklama"), Arg("donem"), Arg("kategori")),
                _ => null
            };
            return reply is not null && TakeCard() is { } card ? reply with { Card = card } : reply;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new QuickReply("İnternete şu an ulaşamıyorum; bağlantı gelince tekrar sorarsan bakarım.");
        }
    }

    private async Task<string> WeatherSpeechAsync(string city, string focus, bool rainQuestion, CancellationToken cancellationToken)
    {
        var forecast = await FetchForecastAsync(city, focus is "hafta" or "haftasonu" ? 7 : 2, cancellationToken).ConfigureAwait(false);
        if (forecast is null)
        {
            return $"{(string.IsNullOrWhiteSpace(city) ? Settings.City : city)} diye bir yer bulamadım.";
        }

        ShowWeatherCard(forecast, focus);

        var where = TurkishGrammar.Locative(forecast.Place);
        static string Degrees(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);

        if (focus is "hafta" or "haftasonu")
        {
            var days = focus == "hafta"
                ? forecast.Days
                : forecast.Days.Where(day => day.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday).ToList();
            if (days.Count == 0)
            {
                days = forecast.Days;
            }

            var label = focus == "hafta" ? "Bu hafta" : "Hafta sonu";
            var rainy = days.Where(day => day.RainChance >= 60).Select(day => day.Date.ToString("dddd", Turkish)).ToList();
            return $"{label} {where} sıcaklık {Degrees(days.Min(day => day.Min))} ile {Degrees(days.Max(day => day.Max))} derece arasında. " +
                   (rainy.Count == 0 ? "Yağışlı bir gün görünmüyor." : $"Yağmur beklenen günler: {TurkishGrammar.Join(rainy)}.");
        }

        var tomorrow = focus == "yarin";
        var target = forecast.Days[Math.Min(tomorrow ? 1 : 0, forecast.Days.Count - 1)];
        var dayLabel = tomorrow ? "yarın" : "bugün";
        var chance = Math.Round(target.RainChance);
        if (rainQuestion)
        {
            return chance switch
            {
                >= 60 => $"Evet, {dayLabel} {where} yüzde {chance} ihtimalle yağış var; şemsiyeni yanına al.",
                >= 30 => $"İhtimal var: {dayLabel} yağış ihtimali yüzde {chance}. Yanına bir şemsiye almak iyi olur.",
                _ => $"Pek sanmıyorum; {dayLabel} {where} yağış ihtimali yüzde {chance}."
            };
        }

        var rain = chance >= 50
            ? $"yüzde {chance} ihtimalle yağış var, şemsiyeni unutma."
            : chance >= 25 ? $"yağış ihtimali yüzde {chance}." : "yağmur beklenmiyor.";
        if (tomorrow)
        {
            return $"Yarın {where} hava {WeatherCodes.Describe(target.Code)}, {Degrees(target.Min)} ile {Degrees(target.Max)} derece arası; {rain}";
        }

        var feels = Math.Abs(forecast.Feels - forecast.Temperature) >= 3 ? $", hissedilen {Degrees(forecast.Feels)}" : "";
        return $"{TurkishGrammar.Capitalize(where)} şu an {Degrees(forecast.Temperature)} derece{feels}, hava {WeatherCodes.Describe(forecast.Code)}. " +
               $"Gün içinde en yüksek {Degrees(target.Max)} derece; {rain}";
    }

    /// <summary>"Bugün" ekranındaki hava kartı; internet yoksa ya da yer bulunamazsa null.</summary>
    public async Task<WeatherCard?> WeatherCardAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await FetchForecastAsync("", 2, cancellationToken).ConfigureAwait(false) is not { } forecast)
            {
                return null;
            }

            var today = forecast.Days[0];
            return new WeatherCard(forecast.Place, (int)Math.Round(forecast.Temperature), WeatherCodes.Describe(forecast.Code),
                (int)Math.Round(today.Max), (int)Math.Round(today.Min), (int)Math.Round(today.RainChance));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Open-Meteo'dan anlık durum ve gün gün tahmin; yer bulunamazsa null.</summary>
    private async Task<Forecast?> FetchForecastAsync(string city, int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 2, 7);
        var place = string.IsNullOrWhiteSpace(city) ? Settings.City : city.Trim();
        ledger.Record(DataFlow.Outbound, "Hava durumu", $"open-meteo.com · şehir: {place}");
        var geo = await GetJsonAsync(
            $"https://geocoding-api.open-meteo.com/v1/search?count=1&language=tr&name={Uri.EscapeDataString(place)}",
            cancellationToken).ConfigureAwait(false);
        var location = geo?["results"]?[0];
        if (location is null)
        {
            return null;
        }

        var latitude = location["latitude"]!.GetValue<double>().ToString(CultureInfo.InvariantCulture);
        var longitude = location["longitude"]!.GetValue<double>().ToString(CultureInfo.InvariantCulture);
        var data = await GetJsonAsync(
            $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}" +
            "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum" +
            $"&timezone=auto&forecast_days={days}",
            cancellationToken).ConfigureAwait(false);

        var current = data!["current"]!;
        var daily = data["daily"]!;
        static double Number(JsonNode? node) => node?.GetValue<double>() ?? 0;
        var list = Enumerable.Range(0, daily["time"]!.AsArray().Count).Select(index => new ForecastDay(
            DateTime.Parse(daily["time"]![index]!.GetValue<string>(), CultureInfo.InvariantCulture),
            daily["weather_code"]![index]?.GetValue<int>() ?? 0,
            Number(daily["temperature_2m_min"]![index]),
            Number(daily["temperature_2m_max"]![index]),
            Number(daily["precipitation_probability_max"]![index]),
            Number(daily["precipitation_sum"]![index]))).ToList();
        return new Forecast(
            location["name"]?.GetValue<string>() ?? place,
            Number(current["temperature_2m"]),
            Number(current["apparent_temperature"]),
            current["weather_code"]?.GetValue<int>() ?? 0,
            Number(current["wind_speed_10m"]),
            list);
    }

    private async Task<string> CurrencySpeechAsync(string currency, CancellationToken cancellationToken)
    {
        var (rates, source) = await GetRatesAsync().ConfigureAwait(false);
        ShowRatesCard(rates, CurrencyCodes(currency), source);
        var parts = CurrencyCodes(currency)
            .Where(rates.ContainsKey)
            .Select(code => $"{CurrencyLabel(code)} {rates[code].ToString(rates[code] >= 10 ? "0.00" : "0.000", Turkish)} lira")
            .ToList();
        return parts.Count == 0 ? "Bu kurun bilgisini bulamadım." : TurkishGrammar.Capitalize(string.Join(", ", parts)) + ".";
    }

    /// <summary>
    /// TL karşılıkları (1 birim = x TL). TCMB'nin bülteni bu bilgisayardan 9 sn'ye kadar gecikebiliyor; bu yüzden TCMB
    /// ve open.er-api.com aynı anda istenir, önce gelen kullanılır ve sonuç 15 dk saklanır.
    /// </summary>
    private Task<(Dictionary<string, double> Rates, string Source)> GetRatesAsync()
    {
        lock (_rateLock)
        {
            // Aynı anda gelen sorular tek isteği paylaşır; başarılı sonuç 15 dk saklanır.
            if (_rateTask is { } task && (!task.IsCompleted || (task.IsCompletedSuccessfully && DateTime.UtcNow - _rateAt < RateCacheLife)))
            {
                return task;
            }

            _rateAt = DateTime.UtcNow;
            _rateTask = FetchRatesAsync();
            return _rateTask;
        }
    }

    private async Task<(Dictionary<string, double> Rates, string Source)> FetchRatesAsync()
    {
        ledger.Record(DataFlow.Outbound, "Döviz kuru", "tcmb.gov.tr ve open.er-api.com · günlük kurlar");
        using var race = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var pending = new List<Task<(Dictionary<string, double>, string)>> { CentralBankAsync(race.Token), OpenRatesAsync(race.Token) };
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(done);
            if (done.IsCompletedSuccessfully && done.Result.Item1.Count > 0)
            {
                await race.CancelAsync().ConfigureAwait(false);
                return done.Result;
            }
        }

        throw new HttpRequestException("Kur kaynaklarına ulaşılamadı.");
    }

    private async Task<(Dictionary<string, double>, string)> CentralBankAsync(CancellationToken cancellationToken)
    {
        var xml = System.Xml.Linq.XDocument.Parse(
            await http.GetStringAsync(new Uri("https://www.tcmb.gov.tr/kurlar/today.xml"), cancellationToken).ConfigureAwait(false));
        var rates = new Dictionary<string, double>();
        foreach (var element in xml.Descendants("Currency"))
        {
            var code = element.Attribute("Kod")?.Value;
            if (code is not null
                && double.TryParse(element.Element("ForexSelling")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var selling)
                && int.TryParse(element.Element("Unit")?.Value, out var unit) && unit > 0)
            {
                rates[code] = selling / unit;
            }
        }

        return (rates, $"TCMB {xml.Root?.Attribute("Tarih")?.Value}");
    }

    private async Task<(Dictionary<string, double>, string)> OpenRatesAsync(CancellationToken cancellationToken)
    {
        var json = JsonNode.Parse(await http.GetStringAsync(new Uri("https://open.er-api.com/v6/latest/TRY"), cancellationToken).ConfigureAwait(false));
        var rates = new Dictionary<string, double>();
        if (json?["rates"] is JsonObject table)
        {
            foreach (var (code, value) in table)
            {
                // Tabloda 1 TL'nin karşılığı var; 1 birimin TL karşılığı tersidir.
                if (value?.GetValue<double>() is > 0 and var perLira)
                {
                    rates[code] = 1 / perLira;
                }
            }
        }

        return (rates, "piyasa ortalaması (open.er-api.com)");
    }

    private static List<string> CurrencyCodes(string currency)
    {
        var wanted = Simplify(currency);
        var codes = CurrencyNames.Where(pair => wanted.Contains(pair.Key, StringComparison.Ordinal)).Select(pair => pair.Value).Distinct().ToList();
        return codes.Count > 0 ? codes : wanted.Length == 3 ? [wanted.ToUpperInvariant()] : ["USD", "EUR"];
    }

    private static string CurrencyLabel(string code) => code switch
    {
        "USD" => "dolar",
        "EUR" => "euro",
        "GBP" => "sterlin",
        "CHF" => "İsviçre frangı",
        "JPY" => "yen",
        "RUB" => "ruble",
        _ => code
    };

    private async Task<QuickReply> MediaReplyAsync(string command, string value)
    {
        var result = await MediaAsync(command, value).ConfigureAwait(false);
        return command switch
        {
            "ne_caliyor" or "ses_seviyesi" => new QuickReply(result),
            "ses_ayarla" => new QuickReply(result, Silent: true),
            "oynat_duraklat" => new QuickReply("Müzik durduruldu ya da devam ettirildi.", Silent: true),
            "sonraki" => new QuickReply("Sonraki parçaya geçildi.", Silent: true),
            "onceki" => new QuickReply("Önceki parçaya dönüldü.", Silent: true),
            "ses_ac" => new QuickReply("Ses açıldı.", Silent: true),
            "ses_kis" => new QuickReply("Ses kısıldı.", Silent: true),
            "sessiz" => new QuickReply("Ses kapatıldı.", Silent: true),
            _ => new QuickReply(result)
        };
    }

    /// <summary>"Ne kadar kaldı": çalışan zamanlayıcılar (en fazla üç) ve süren odak.</summary>
    internal QuickReply TimeLeft(DateTime now)
    {
        var parts = Countdowns.Active(reminders.Upcoming, now).Take(3)
            .Select(item => item.Text.Trim().Length == 0
                ? $"zamanlayıcının bitmesine {SpokenDuration.Of(item.Due - now)} var"
                : $"{TurkishGrammar.ToYou(item.Text.Trim())} için {SpokenDuration.Of(item.Due - now)} kaldı")
            .ToList();
        if (Rhythm?.Snapshot() is { FocusEnd: { } end, FocusPausedAt: null } && end > now)
        {
            parts.Add($"odağın bitmesine {SpokenDuration.Of(end - now)} var");
        }

        return new QuickReply(parts.Count == 0
            ? "Çalışan bir zamanlayıcı ya da odak yok."
            : TurkishGrammar.Capitalize(TurkishGrammar.Join(parts)) + ".");
    }

    private string TimerSpeech(string minutesText, string note)
    {
        var result = StartTimer(minutesText, note);
        if (!double.TryParse(minutesText, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) || result.StartsWith("Geçerli", StringComparison.Ordinal))
        {
            return result;
        }

        var span = TurkishGrammar.Duration(minutes);
        return string.IsNullOrWhiteSpace(note)
            ? $"Tamam, {span} sonra haber veririm."
            : $"Tamam, {span} sonra {note} hatırlatırım.";
    }

    private async Task<QuickReply?> RoutineReplyAsync(string name, CancellationToken cancellationToken)
    {
        var match = Settings.Routines.FirstOrDefault(routine => Simplify(routine.Key) == Simplify(name));
        if (match.Key is null)
        {
            return null;
        }

        // Brifing birden çok bilgiyi birleştirir; cümleyi model kurar (RunStepsAsync null döner).
        if (await RunStepsAsync(match.Value, cancellationToken).ConfigureAwait(false) is not { } reply)
        {
            return null;
        }

        ledger.Record(DataFlow.Local, "Rutin", $"{match.Key} çalıştı");
        RecordHabit($"rutin:{match.Key}", match.Key);
        return reply.Text.Length > 0 || reply.Script is not null ? reply : reply with { Text = $"{TurkishGrammar.Capitalize(match.Key)} hazır." };
    }

    private QuickReply OpenAndRecord((string Target, string Name) app)
    {
        var text = Open(app);
        RecordHabit($"uygulama:{app.Name}", app.Name);
        return new QuickReply(text);
    }

    /// <summary>Ritim koçu (mola, odak, nefes egzersizi); kapalıysa null.</summary>
    public RhythmCoach? Rhythm { get; set; }

    /// <summary>Dil modeli ritim aracını çağırdıysa okunacak rehberli metin (nefes, esneme); bir kez alınır.</summary>
    public IReadOnlyList<CoachStep>? TakeScript() => Interlocked.Exchange(ref _script, null);

    private QuickReply RhythmCommand(string action, string minutesText, string task = "", string period = "")
    {
        if (Rhythm is not { Enabled: true } coach)
        {
            return new QuickReply("Ritim koçu kapalı; Ayarlar'dan açabilirsin.");
        }

        var minutes = double.TryParse(minutesText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        var reply = Simplify(action) switch
        {
            "mola" => coach.StartBreak(),
            "molabitir" => coach.EndBreak(),
            "odak" => coach.StartFocus(minutes),
            "odakbitir" => coach.EndFocus(),
            "odakduraklat" => coach.PauseFocus(),
            "odakdevam" => coach.ResumeFocus(),
            "odakuzat" => coach.ExtendFocus(minutes),
            "molauzat" => coach.ExtendBreak(minutes),
            "odaklar" => coach.FocusSessions(task, period),
            "ertele" => coach.Snooze(minutes),
            "nefes" => RhythmCoach.Breathe(),
            "esneme" => RhythmCoach.Stretch(),
            "su" => WaterWithHabit(coach),
            "durum" => coach.Status(),
            "hafta" => coach.WeekSummary(),
            "kaldigimyer" => Resume(),
            _ => new QuickReply($"Bilinmeyen ritim işlemi: {action}")
        };
        ledger.Record(DataFlow.Local, "Ritim", $"{action} istendi");
        _script = reply.Script;
        _lastRhythmText = reply.Text;
        return reply;
    }

    /// <summary>"Su içtim": ritim sayacı artar; "su içmek" gibi bir alışkanlık takip ediliyorsa o da işaretlenir.</summary>
    private QuickReply WaterWithHabit(RhythmCoach coach)
    {
        var reply = coach.Water();
        if (HabitBook is { } book && book.Find("su") is { } habit && HabitBook.Key(habit.Name).StartsWith("su", StringComparison.Ordinal))
        {
            book.Mark(habit.Name, DateOnly.FromDateTime(DateTime.Now));
        }

        return reply;
    }

    /// <summary>Rutin adımı: "mola", "odak 25", "nefes".</summary>
    private string RhythmStep(string value)
    {
        var parts = value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? "" : RhythmCommand(parts[0], parts.Length > 1 ? parts[1] : "").Text;
    }

    /// <summary>Ayardaki ya da Başlat menüsündeki uygulama; bulunamazsa null.</summary>
    private (string Target, string Name)? FindApp(string name)
    {
        var spoken = name.Trim();
        if (spoken.Length == 0)
        {
            return null;
        }

        if (Settings.Apps.TryGetValue(spoken, out var command))
        {
            return (command, spoken);
        }

        var wanted = Simplify(spoken);
        if (wanted.Length < 3 || NotApps.Contains(wanted))
        {
            return null;
        }

        var shortcut = StartMenuShortcuts()
            .Select(path => (path, key: Simplify(Path.GetFileNameWithoutExtension(path))))
            .Where(item => item.key.Length >= 3)
            // Kısa adlar ("not") yalnızca tam eşleşir; uzunlar kısayol adının içinde aranabilir.
            .Where(item => item.key == wanted
                || (wanted.Length >= 4 && item.key.Contains(wanted, StringComparison.Ordinal))
                || (item.key.Length >= 4 && wanted.Contains(item.key, StringComparison.Ordinal)))
            .OrderBy(item => item.key == wanted ? 0 : 1)
            .ThenBy(item => Math.Abs(item.key.Length - wanted.Length))
            .Select(item => item.path)
            .FirstOrDefault();
        if (shortcut is not null)
        {
            return (shortcut, Path.GetFileNameWithoutExtension(shortcut));
        }

        // Mağaza uygulamaları (WhatsApp, Spotify, Hesap Makinesi…): Başlat menüsü klasörlerinde kısayolları yoktur.
        var store = StoreApps.All;
        if (store.FirstOrDefault(app => Simplify(app.Name) is var key && (key == wanted || (wanted.Length >= 4 && key.StartsWith(wanted, StringComparison.Ordinal))))
            is { Name.Length: > 0 } stored)
        {
            return (stored.Target, stored.Name);
        }

        // Türkçe okunuşla söylenen ad ("vatsap", "krom", "spotifay", "eksel"): sesçe en yakın kısayol, Mağaza uygulaması ya da kayıtlı uygulama.
        var shortcuts = StartMenuShortcuts().ToList();
        var names = shortcuts.Select(Path.GetFileNameWithoutExtension).OfType<string>().Concat(store.Select(app => app.Name)).Concat(Settings.Apps.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (PhoneticName.Best(spoken, names) is not { } heard)
        {
            return null;
        }

        FridayLog.Info($"Uygulama adı sesçe eşleşti: \"{spoken}\" → {heard}");
        if (Settings.Apps.TryGetValue(heard, out var saved))
        {
            return (saved, heard);
        }

        return shortcuts.FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), heard, StringComparison.OrdinalIgnoreCase)) is { } link
            ? (link, heard)
            : (store.First(app => string.Equals(app.Name, heard, StringComparison.OrdinalIgnoreCase)).Target, heard);
    }

    private string Open((string Target, string Name) app)
    {
        Launch(app.Target);
        ledger.Record(DataFlow.Local, "Uygulama", $"{app.Name} açıldı");
        return $"{TurkishGrammar.Capitalize(app.Name)} açılıyor.";
    }

    private sealed record ForecastDay(DateTime Date, int Code, double Min, double Max, double RainChance, double Rain);

    private sealed record Forecast(string Place, double Temperature, double Feels, int Code, double Wind, List<ForecastDay> Days);
}
