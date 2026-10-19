using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Dil modelinin bilgisayarda yapabileceği işler. Bilerek dar tutuldu: dosya silme, kapatma, komut çalıştırma
/// gibi geri alınamayan araçlar yoktur. Her araç gizlilik defterine ne yaptığını ve verinin bilgisayardan çıkıp
/// çıkmadığını yazar.
/// </summary>
public sealed partial class AssistantTools(
    HttpClient http,
    OllamaChatClient chat,
    MemoryStore memory,
    PrivacyLedger ledger,
    ConversationLog history)
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Araç çalışırken arayüzde gösterilen kısa açıklamalar.</summary>
    private static readonly Dictionary<string, string> Activities = new()
    {
        ["saat_ve_tarih"] = "Saate bakıyorum",
        ["uygulama_ac"] = "Uygulamayı açıyorum",
        ["web_sitesi_ac"] = "Siteyi açıyorum",
        ["web_ara"] = "Web'de arıyorum",
        ["medya"] = "Medyayı kontrol ediyorum",
        ["hava_durumu"] = "Hava durumuna bakıyorum",
        ["zamanlayici"] = "Zamanlayıcı kuruyorum",
        ["ekrana_bak"] = "Ekrana bakıyorum",
        ["pano"] = "Panodaki metinle çalışıyorum",
        ["hatirla"] = "Hafızama yazıyorum",
        ["unut"] = "Hafızamdan siliyorum",
        ["dosya_bul"] = "Dosya radarı taranıyor",
        ["dosya_ac"] = "Dosyayı açıyorum",
        ["sistem_durumu"] = "Sistemi inceliyorum",
        ["rutin"] = "Rutini çalıştırıyorum",
        ["gunluk_ozet"] = "Günün özetini hazırlıyorum",
        ["gecmis"] = "Sohbet geçmişine bakıyorum",
        ["linguaflow"] = "LinguaFlow'a bağlanıyorum",
        ["masaustu"] = "Masaüstüne bakıyorum",
        ["masaustu_kontrol"] = "Masaüstünde işlem yapıyorum",
        ["internette_ara"] = "İnternette araştırıyorum",
        ["sayfayi_oku"] = "Sayfayı okuyorum",
        ["doviz"] = "Kurlara bakıyorum",
        ["video_ac"] = "Videoyu buluyorum",
    };

    private readonly List<(DateTime Due, string Note)> _timers = [];

    /// <summary>Zamanlayıcı dolunca söylenecek metin.</summary>
    public event EventHandler<string>? TimerElapsed;

    public FridaySettings Settings { get; set; } = new();

    public static string DescribeActivity(string tool) => Activities.GetValueOrDefault(tool, "Çalışıyorum");

    public JsonArray Definitions { get; } =
    [
        Tool("saat_ve_tarih", "Şu anki saati, tarihi ve günü verir.", []),
        Tool("uygulama_ac", "Bilgisayarda bir uygulamayı açar (ör. not defteri, hesap makinesi, Spotify, Chrome, Steam).",
            [("ad", "string", "Uygulamanın adı", true)]),
        Tool("web_sitesi_ac", "Varsayılan tarayıcıda bir web sitesini açar.",
            [("adres", "string", "Site adresi, ör. youtube.com", true)]),
        Tool("web_ara", "Tarayıcıda Google arama sayfasını açar. Yalnızca kullanıcı aramayı tarayıcıda görmek isterse kullan; bilgi gerekiyorsa internette_ara kullan.",
            [("sorgu", "string", "Aranacak metin", true)]),
        Tool("video_ac", "YouTube'da video arar ve ilk sonucu tarayıcıda açar; video adresi verilirse onu açar. 'X videosunu aç', 'YouTube'da Y aç', 'Z şarkısının klibini aç' için.",
            [("arama", "string", "Aranacak video ya da video adresi", true)]),
        Tool("doviz", "Güncel döviz kuru (dolar, euro, sterlin…) Merkez Bankası'nın resmî günlük kurlarından. 'Dolar kaç lira' gibi sorular için önce bunu kullan.",
            [("birim", "string", "dolar, euro, sterlin… ya da boş (hepsi)", false)]),
        Tool("internette_ara", "İnternette arar, sonuç sayfalarını kendisi okur ve bilgiyi getirir. Güncel bilgi, haber, fiyat, kur, maç sonucu, tarifler ve bilmediğin her şey için kullan.",
            [("soru", "string", "Aranacak soru ya da anahtar sözcükler", true)]),
        Tool("sayfayi_oku", "Bir web sayfasının metnini okur. Adres verilmezse tarayıcıda açık olan sekmeyi okur (ör. 'bu sayfayı özetle', 'bu makalede ne diyor').",
            [("adres", "string", "Sayfa adresi; açık sekme için boş bırak", false)]),
        Tool("masaustu", "Bilgisayarda açık olanları gerçek verilerle okur: hangi pencereler/programlar açık, tarayıcıda hangi sekmeler açık, öndeki pencere ne, bir penceredeki yazılar.",
            [("islem", "string", "pencereler, sekmeler, aktif ya da oku", true), ("uygulama", "string", "oku için uygulama adı, ör. Outlook", false)]),
        Tool("masaustu_kontrol", "Masaüstünde işlem yapar: sekmeye_gec, sekmeyi_kapat, one_getir (pencereyi öne al), tikla (hedef 'Uygulama > düğme adı'), yaz (hedef pencereye metin yazar). Araç onay isterse kullanıcıya sor.",
            [("islem", "string", "sekmeye_gec, sekmeyi_kapat, one_getir, tikla ya da yaz", true), ("hedef", "string", "Sekme adı, uygulama adı ya da 'Uygulama > düğme'", true),
             ("metin", "string", "yaz için metin", false)]),
        Tool("medya", "Müzik/videoyu ve sesi kontrol eder; ne_caliyor ile o an çalan şarkıyı/videoyu söyler.",
            [("komut", "string", "oynat_duraklat, sonraki, onceki, ses_ac, ses_kis, sessiz ya da ne_caliyor", true)]),
        Tool("hava_durumu", "Bir şehrin şu anki hava durumu ve gün gün tahmini (en fazla 7 gün): sıcaklık, yağış ihtimali ve miktarı. Hava ve yağmur soruları için internette_ara yerine bunu kullan.",
            [("sehir", "string", "Şehir adı; söylenmediyse boş bırak", false),
             ("gun", "number", "Kaç günlük tahmin: bugün/yarın için 2, hafta için 7", false)]),
        Tool("zamanlayici", "Belirtilen süre sonra kullanıcıya sesli hatırlatma yapar.",
            [("dakika", "number", "Kaç dakika sonra", true), ("not", "string", "Hatırlatılacak şey", false)]),
        Tool("ekrana_bak", "Kullanıcının ekranına bakar ve soruyu ekranda görünene göre cevaplar (ör. bu hata ne, burada ne yazıyor, bunu çevir).",
            [("soru", "string", "Ekranla ilgili soru", true), ("tum_ekran", "boolean", "Sadece aktif pencere yerine tüm ekran", false)]),
        Tool("pano", "Kullanıcının kopyaladığı (panodaki) metinle çalışır. oku dışındaki işlemlerin sonucu panoya geri konur.",
            [("islem", "string", "oku, ozetle, cevir, duzelt, resmilestir, maddele ya da acikla", true),
             ("dil", "string", "cevir için hedef dil, ör. İngilizce", false)]),
        Tool("hatirla", "Kullanıcının kalıcı olarak hatırlamanı istediği bir bilgiyi kaydeder.",
            [("bilgi", "string", "Kullanıcıdan üçüncü şahısla söz eden, kendi içinde anlamlı tek cümle, ör. 'Kullanıcının arabasının muayenesi 20 Kasım'da.'", true)]),
        Tool("unut", "Hafızadaki bir bilgiyi siler.", [("konu", "string", "Silinecek bilgide geçen sözcük", true)]),
        Tool("dosya_bul", "Bilgisayarda dosya arar (Windows Arama dizini). Sonuçlar numaralıdır.",
            [("arama", "string", "Dosya adında ya da içinde geçen sözcükler; yoksa boş", false),
             ("tur", "string", "pdf, word, excel, sunum, resim, video, muzik, arsiv, kod, kurulum ya da boş", false),
             ("son_gun", "number", "Son kaç gün içinde değişmiş; dün için 1, bilinmiyorsa 0", false),
             ("klasor", "string", "indirilenler, belgeler, masaustu, resimler ya da boş", false),
             ("ac", "boolean", "En uygun sonucu hemen aç", false)]),
        Tool("dosya_ac", "dosya_bul sonuçlarından birini açar.",
            [("numara", "number", "Sonuç numarası", true), ("klasorde_goster", "boolean", "Dosyayı açmak yerine klasörünü göster", false)]),
        Tool("sistem_durumu", "Bilgisayarın durumu: işlemci, bellek, disk, ekran kartı sıcaklığı (derece), fan ve güç, en çok kaynak kullanan uygulamalar. 'Bilgisayar kaç derece', 'neden yavaş' için.", []),
        Tool("rutin", "Tanımlı bir rutini çalıştırır (ör. çalışma modu, mola).", [("ad", "string", "Rutinin adı", true)]),
        Tool("gunluk_ozet", "Günaydın brifingi: tarih, hava, zamanlayıcılar ve hatırlatmalar.", []),
        Tool("gecmis", "Belirli bir günde kullanıcıyla neler konuşulduğunu verir.",
            [("gun", "string", "bugun, dun ya da yyyy-aa-gg", false)]),
        Tool("linguaflow", "LinguaFlow canlı çeviri uygulamasını açıp ekrandaki video/toplantı için çeviriyi başlatır ya da durdurur.",
            [("islem", "string", "baslat ya da durdur", true)]),
    ];

    public async Task<string> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            return call.Name switch
            {
                "saat_ve_tarih" => DateTime.Now.ToString("d MMMM yyyy dddd, HH:mm", Turkish),
                "uygulama_ac" => OpenApp(Argument(call, "ad")),
                "web_sitesi_ac" => OpenWebsite(Argument(call, "adres")),
                "web_ara" => Search(Argument(call, "sorgu")),
                "medya" => await MediaAsync(Argument(call, "komut")).ConfigureAwait(false),
                "hava_durumu" => await WeatherAsync(Argument(call, "sehir"), cancellationToken,
                    int.TryParse(Argument(call, "gun"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) ? days : 2).ConfigureAwait(false),
                "zamanlayici" => StartTimer(Argument(call, "dakika"), Argument(call, "not")),
                "ekrana_bak" => await LookAtScreenAsync(Argument(call, "soru"), Flag(call, "tum_ekran"), cancellationToken).ConfigureAwait(false),
                "pano" => await ClipboardAsync(Argument(call, "islem"), Argument(call, "dil"), cancellationToken).ConfigureAwait(false),
                "hatirla" => Remember(Argument(call, "bilgi")),
                "unut" => Forget(Argument(call, "konu")),
                "dosya_bul" => FindFiles(call),
                "dosya_ac" => OpenFound(Argument(call, "numara"), Flag(call, "klasorde_goster")),
                "sistem_durumu" => await SystemAsync(cancellationToken).ConfigureAwait(false),
                "rutin" => await RunRoutineAsync(Argument(call, "ad"), cancellationToken).ConfigureAwait(false),
                "gunluk_ozet" => await BriefingAsync(cancellationToken).ConfigureAwait(false),
                "gecmis" => History(Argument(call, "gun")),
                "linguaflow" => await LinguaFlowAsync(Argument(call, "islem"), cancellationToken).ConfigureAwait(false),
                "video_ac" => await OpenVideoAsync(Argument(call, "arama"), cancellationToken).ConfigureAwait(false),
                "doviz" => await ExchangeRatesAsync(Argument(call, "birim"), cancellationToken).ConfigureAwait(false),
                "internette_ara" => await SearchWebAsync(Argument(call, "soru"), cancellationToken).ConfigureAwait(false),
                "sayfayi_oku" => await ReadPageAsync(Argument(call, "adres"), cancellationToken).ConfigureAwait(false),
                // UI Automation çağrıları zaman alabilir; asistanın akışını bekletmesin diye arka planda çalışır.
                "masaustu" => await Task.Run(() => DesktopInfo(Argument(call, "islem"), Argument(call, "uygulama")), cancellationToken).ConfigureAwait(false),
                "masaustu_kontrol" => await Task.Run(() => DesktopControl(Argument(call, "islem"), Argument(call, "hedef"), Argument(call, "metin")), cancellationToken).ConfigureAwait(false),
                _ => $"Böyle bir araç yok: {call.Name}"
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return $"Hata: {exception.Message}";
        }
    }

    private string OpenApp(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Uygulama adı söylenmedi.";
        }

        if (Settings.Apps.TryGetValue(name.Trim(), out var command))
        {
            Launch(command);
            ledger.Record(DataFlow.Local, "Uygulama", $"{name} açıldı");
            return $"{name} açıldı.";
        }

        // Başlat menüsündeki kısayollar arasında ada en çok benzeyeni bul.
        var wanted = Simplify(name);
        if (wanted.Length < 2)
        {
            return $"'{name}' adında bir uygulama bulunamadı.";
        }

        var shortcut = StartMenuShortcuts()
            .Select(path => (path, key: Simplify(Path.GetFileNameWithoutExtension(path))))
            .Where(item => item.key.Length >= 2)
            .Where(item => item.key.Contains(wanted, StringComparison.Ordinal) || wanted.Contains(item.key, StringComparison.Ordinal))
            .OrderBy(item => Math.Abs(item.key.Length - wanted.Length))
            .Select(item => item.path)
            .FirstOrDefault();
        if (shortcut is null)
        {
            return $"'{name}' adında bir uygulama bulunamadı.";
        }

        Launch(shortcut);
        var opened = Path.GetFileNameWithoutExtension(shortcut);
        ledger.Record(DataFlow.Local, "Uygulama", $"{opened} açıldı");
        return $"{opened} açıldı.";
    }

    private string OpenWebsite(string address)
    {
        var text = address.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return "Geçerli bir web adresi değil.";
        }

        Launch(uri.AbsoluteUri);
        ledger.Record(DataFlow.Outbound, "Tarayıcı", $"{uri.Host} açıldı");
        return $"{uri.Host} açıldı.";
    }

    private string Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Aranacak bir şey söylenmedi.";
        }

        Launch("https://www.google.com/search?q=" + Uri.EscapeDataString(query));
        ledger.Record(DataFlow.Outbound, "Google araması", $"\"{query}\" tarayıcıda arandı");
        return $"'{query}' için arama sonuçları tarayıcıda açıldı.";
    }

    private async Task<string> MediaAsync(string command)
    {
        if (Simplify(command) is "necaliyor" or "calan")
        {
            ledger.Record(DataFlow.Local, "Medya", "Çalan şarkının bilgisi Windows'tan okundu");
            return await NowPlaying.DescribeAsync().ConfigureAwait(false);
        }

        var (key, repeat) = Simplify(command) switch
        {
            "oynatduraklat" or "oynat" or "duraklat" or "durdur" => (VirtualKey.PlayPause, 1),
            "sonraki" => (VirtualKey.Next, 1),
            "onceki" => (VirtualKey.Previous, 1),
            "sesac" or "sesiac" => (VirtualKey.VolumeUp, 5),
            "seskis" or "sesikis" => (VirtualKey.VolumeDown, 5),
            "sessiz" => (VirtualKey.Mute, 1),
            _ => ((VirtualKey)0, 0)
        };
        if (repeat == 0)
        {
            return $"Bilinmeyen medya komutu: {command}";
        }

        for (var index = 0; index < repeat; index++)
        {
            NativeMethods.keybd_event((byte)key, 0, 0, 0);
            NativeMethods.keybd_event((byte)key, 0, 2, 0);
        }

        return "Tamam.";
    }

    /// <param name="days">Tahmin günü sayısı (2–7); haftalık sorular için 7.</param>
    private async Task<string> WeatherAsync(string city, CancellationToken cancellationToken, int days = 2)
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
            return $"'{place}' bulunamadı.";
        }

        var latitude = location["latitude"]!.GetValue<double>().ToString(CultureInfo.InvariantCulture);
        var longitude = location["longitude"]!.GetValue<double>().ToString(CultureInfo.InvariantCulture);
        var forecast = await GetJsonAsync(
            $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}" +
            "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum" +
            $"&timezone=auto&forecast_days={days}",
            cancellationToken).ConfigureAwait(false);

        var current = forecast!["current"]!;
        var daily = forecast["daily"]!;
        string Day(int index)
        {
            var date = DateTime.Parse(daily["time"]![index]!.GetValue<string>(), CultureInfo.InvariantCulture);
            var label = index switch { 0 => "Bugün", 1 => "Yarın", _ => date.ToString("dddd d MMMM", Turkish) };
            var rain = daily["precipitation_sum"]![index]?.GetValue<double>() ?? 0;
            return $"{label}: {WeatherCodes.Describe(daily["weather_code"]![index]!.GetValue<int>())}, " +
                   $"{Round(daily["temperature_2m_min"]![index])}–{Round(daily["temperature_2m_max"]![index])} derece, " +
                   $"yağış ihtimali yüzde {Round(daily["precipitation_probability_max"]![index])}" +
                   (rain >= 0.1 ? $", {rain.ToString("0.#", Turkish)} mm yağış" : "");
        }

        var count = daily["time"]!.AsArray().Count;
        return $"{location["name"]}: şu an {Round(current["temperature_2m"])} derece " +
               $"(hissedilen {Round(current["apparent_temperature"])}), {WeatherCodes.Describe(current["weather_code"]!.GetValue<int>())}, " +
               $"rüzgâr {Round(current["wind_speed_10m"])} km/sa.\n" +
               string.Join('\n', Enumerable.Range(0, count).Select(Day));
    }

    private string StartTimer(string minutesText, string note)
    {
        if (!double.TryParse(minutesText, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) || minutes <= 0 || minutes > 24 * 60)
        {
            return "Geçerli bir süre söylenmedi.";
        }

        var message = string.IsNullOrWhiteSpace(note) ? "Zamanlayıcı doldu." : $"Hatırlatma: {note}";
        var entry = (Due: DateTime.Now.AddMinutes(minutes), Note: string.IsNullOrWhiteSpace(note) ? "zamanlayıcı" : note);
        lock (_timers)
        {
            _timers.Add(entry);
        }

        _ = Task.Delay(TimeSpan.FromMinutes(minutes)).ContinueWith(_ =>
        {
            lock (_timers)
            {
                _timers.Remove(entry);
            }

            TimerElapsed?.Invoke(this, message);
        }, TaskScheduler.Default);
        return $"{minutes.ToString("0.#", Turkish)} dakikalık zamanlayıcı kuruldu.";
    }

    private async Task<string> SystemAsync(CancellationToken cancellationToken)
    {
        ledger.Record(DataFlow.Local, "Sistem", "İşlemci, bellek ve disk kullanımı okundu");
        return await SystemStatus.DescribeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> LinguaFlowAsync(string action, CancellationToken cancellationToken)
    {
        var start = Simplify(action) is not ("durdur" or "kapat" or "bitir");
        ledger.Record(DataFlow.Local, "LinguaFlow", start ? "Canlı çeviri başlatıldı" : "Canlı çeviri durduruldu");
        return await LinguaFlowLauncher.ToggleAsync(Settings.LinguaFlowPath, start, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonNode?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var text = await http.GetStringAsync(new Uri(url), timeout.Token).ConfigureAwait(false);
        return JsonNode.Parse(text);
    }

    private static string Round(JsonNode? value) =>
        Math.Round(value?.GetValue<double>() ?? 0).ToString(CultureInfo.InvariantCulture);

    private static void Launch(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    private static IEnumerable<string> StartMenuShortcuts()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.StartMenu })
        {
            var root = Environment.GetFolderPath(folder);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                // Kaldırma kısayolları asla açılmaz.
                if (!name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("kaldır", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>"Visual Studio Code" ile "visual studio kod" gibi söylenişleri karşılaştırılabilir yapar.</summary>
    internal static string Simplify(string text) =>
        new(text.ToLower(Turkish)
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string Argument(ToolCall call, string name) =>
        call.Arguments.TryGetValue(name, out var value) ? value : "";

    private static bool Flag(ToolCall call, string name) =>
        Simplify(Argument(call, name)) is "true" or "evet" or "1";

    private static JsonObject Tool(string name, string description, (string Name, string Type, string Description, bool Required)[] parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var parameter in parameters)
        {
            properties[parameter.Name] = new JsonObject { ["type"] = parameter.Type, ["description"] = parameter.Description };
            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required }
            }
        };
    }

    private enum VirtualKey : byte
    {
        Mute = 0xAD,
        VolumeDown = 0xAE,
        VolumeUp = 0xAF,
        Next = 0xB0,
        Previous = 0xB1,
        PlayPause = 0xB3
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
    }
}
