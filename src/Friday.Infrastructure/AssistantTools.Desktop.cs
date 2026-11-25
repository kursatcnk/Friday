using System.Globalization;
using System.Text;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Masaüstünü gören ve kullanan, interneti kendisi okuyan araçlar.</summary>
public sealed partial class AssistantTools
{
    private static readonly string[] Affirmatives = ["evet", "onayliyorum", "onayla", "tamam", "olur", "yap", "eminim", "aynen"];
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromMinutes(2);

    private readonly WebReader _web = new(http);
    private (string Key, DateTime At)? _pending;

    /// <summary>Kullanıcının bu turdaki cümlesi; değişiklik yapan işlemlerin onayı buradan okunur.</summary>
    public string LastUserText { get; set; } = "";

    private string DesktopInfo(string action, string app)
    {
        switch (Simplify(action))
        {
            case "sekmeler":
            {
                var tabs = Desktop.Tabs();
                ledger.Record(DataFlow.Local, "Masaüstü", $"Tarayıcı sekmelerinin adları okundu ({tabs.Count})");
                if (tabs.Count == 0)
                {
                    return "Açık bir tarayıcı sekmesi bulunamadı.";
                }

                return string.Join('\n', tabs.GroupBy(tab => tab.Browser).Select(group =>
                    $"{group.Key}: {group.Count()} sekme — " + string.Join("; ", group.Select((tab, index) =>
                        $"{index + 1}) {tab.Title}{(tab.Selected ? " (önde)" : "")}"))));
            }

            case "oku":
            {
                var window = Desktop.FindWindow(app);
                if (window is null)
                {
                    return $"'{app}' adında açık bir pencere bulunamadı.";
                }

                var text = Desktop.ReadText(window);
                ledger.Record(DataFlow.Local, "Masaüstü", $"{window.Process} penceresindeki yazılar okundu ({text.Length} karakter)");
                return text.Length < 20
                    ? $"{window.Process} uygulaması içeriğini Windows'a açmıyor; yazıları okunamadı. Gerekirse ekrana_bak ile görüntüye bakılabilir."
                    : $"{window.Process} — \"{window.Title}\" penceresindeki yazılar:\n{text}";
            }

            case "aktif":
            {
                var window = Desktop.Foreground();
                if (window is null)
                {
                    return "Önde bir pencere yok.";
                }

                var address = Desktop.BrowserAddress(window);
                return $"Öndeki pencere: {window.Process} — \"{window.Title}\"" + (address is null ? "" : $", adres: {address}");
            }

            default:
            {
                var windows = Desktop.Windows();
                ledger.Record(DataFlow.Local, "Masaüstü", $"Açık pencerelerin adları okundu ({windows.Count})");
                return "Açık pencereler (öndeki ilk): " + string.Join("; ", windows.Take(20).Select(window => $"{window.Process} — \"{window.Title}\""));
            }
        }
    }

    private string DesktopControl(string action, string target, string text)
    {
        var operation = Simplify(action);
        switch (operation)
        {
            case "sekmeyegec":
            {
                var tab = Desktop.FindTab(target);
                if (tab is null)
                {
                    return $"'{target}' içeren bir sekme bulunamadı.";
                }

                ledger.Record(DataFlow.Local, "Masaüstü", $"\"{tab.Title}\" sekmesine geçildi");
                return Desktop.SelectTab(tab) ? $"\"{tab.Title}\" sekmesine geçildi." : "Sekmeye geçilemedi.";
            }

            case "onegetir":
            {
                var window = Desktop.FindWindow(target);
                if (window is null)
                {
                    return $"'{target}' adında açık bir pencere bulunamadı.";
                }

                ledger.Record(DataFlow.Local, "Masaüstü", $"{window.Process} öne getirildi");
                return Desktop.BringToFront(window.Handle) ? $"{window.Process} öne getirildi." : "Windows pencereyi öne getirmeye izin vermedi.";
            }

            case "sekmeyikapat":
            {
                var tab = Desktop.FindTab(target);
                if (tab is null)
                {
                    return $"'{target}' içeren bir sekme bulunamadı.";
                }

                if (!Confirmed($"kapat|{tab.Title}", $"\"{tab.Title}\" sekmesi kapatılacak", out var ask))
                {
                    return ask;
                }

                ledger.Record(DataFlow.Local, "Masaüstü", $"\"{tab.Title}\" sekmesi kullanıcının onayıyla kapatıldı");
                return Desktop.CloseTab(tab) ? $"\"{tab.Title}\" sekmesi kapatıldı." : "Sekme kapatılamadı.";
            }

            case "tikla":
            {
                var parts = target.Split('>', 2, StringSplitOptions.TrimEntries);
                var window = parts.Length == 2 ? Desktop.FindWindow(parts[0]) : Desktop.Foreground();
                var button = parts.Length == 2 ? parts[1] : target;
                if (window is null)
                {
                    return "Hedef pencere bulunamadı.";
                }

                if (!Confirmed($"tikla|{window.Process}|{Simplify(button)}", $"{window.Process} penceresinde \"{button}\" düğmesine basılacak", out var ask))
                {
                    return ask;
                }

                var clicked = Desktop.Click(window, button);
                ledger.Record(DataFlow.Local, "Masaüstü", $"{window.Process}: \"{clicked ?? button}\" kullanıcının onayıyla tıklandı");
                return clicked is null ? $"\"{button}\" adında bir düğme bulunamadı." : $"\"{clicked}\" düğmesine basıldı.";
            }

            case "yaz":
            {
                var window = Desktop.FindWindow(target);
                if (window is null || string.IsNullOrWhiteSpace(text))
                {
                    return "Yazılacak metin ya da hedef pencere eksik.";
                }

                if (!Confirmed($"yaz|{window.Process}|{text}", $"{window.Process} penceresine \"{text}\" yazılacak", out var ask))
                {
                    return ask;
                }

                ledger.Record(DataFlow.Local, "Masaüstü", $"{window.Process} penceresine {text.Length} karakter kullanıcının onayıyla yazıldı");
                return Desktop.Type(window, text) ? "Yazıldı." : "Pencere öne getirilemediği için yazılamadı.";
            }

            default:
                return $"Bilinmeyen işlem: {action}";
        }
    }

    /// <summary>
    /// Değişiklik yapan işlemler iki adımda çalışır: ilk çağrı işlemi bekletir ve kullanıcıya sorulmasını ister;
    /// yalnızca kullanıcının bir sonraki cümlesi onay ise ("evet", "tamam"…) aynı işlem yapılır. Model kendi başına
    /// onay veremez.
    /// </summary>
    internal bool Confirmed(string key, string description, out string ask)
    {
        var words = LastUserText.ToLower(Turkish)
            .Split([' ', ',', '.', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Simplify);
        if (_pending is { } pending && pending.Key == key && DateTime.UtcNow - pending.At < ConfirmationWindow
            && words.Any(word => Affirmatives.Contains(word)))
        {
            _pending = null;
            ask = "";
            return true;
        }

        _pending = (key, DateTime.UtcNow);
        ask = $"ONAY GEREKİYOR: {description}. Bunu yapmadan önce kullanıcıya tek cümleyle sor. " +
              "Kullanıcı bir sonraki cümlesinde 'evet' derse aynı aracı aynı bilgilerle yeniden çağır.";
        return false;
    }

    private async Task<string> SearchWebAsync(string question, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return "Aranacak bir şey söylenmedi.";
        }

        ledger.Record(DataFlow.Outbound, "İnternet araması", $"duckduckgo.com · \"{question}\"");
        var results = await _web.SearchAsync(question, cancellationToken).ConfigureAwait(false);
        if (results.Count == 0)
        {
            return "İnternette sonuç bulunamadı.";
        }

        // İlk üç uygun sayfa aynı anda, soruyla ilgili paragraflarına odaklanarak okunur; video siteleri metin vermez.
        var readable = results
            .Where(result => !result.Url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        var pages = await Task.WhenAll(readable.Select(async result =>
        {
            try
            {
                return (result, text: await _web.ReadAsync(new Uri(result.Url), 2000, cancellationToken, question).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                return (result, text: "");
            }
        })).ConfigureAwait(false);

        var answer = new StringBuilder("Arama sonuçları:\n");
        foreach (var (result, index) in results.Select((result, index) => (result, index)))
        {
            answer.AppendLine(CultureInfo.InvariantCulture, $"{index + 1}) {result.Title} ({Host(result.Url)}): {result.Snippet}");
        }

        foreach (var (result, text) in pages.Where(page => page.text.Length > 0))
        {
            ledger.Record(DataFlow.Outbound, "Web sayfası", $"{Host(result.Url)} okundu");
            answer.AppendLine(CultureInfo.InvariantCulture, $"\n[{Host(result.Url)} sayfasından]\n{text}");
        }

        answer.Append("\nCevabı bu bilgilere dayandır ve hangi siteden aldığını kısaca söyle.");
        return answer.ToString();
    }

    private async Task<string> ReadPageAsync(string address, CancellationToken cancellationToken)
    {
        OpenWindow? window = null;
        if (string.IsNullOrWhiteSpace(address))
        {
            // Adres verilmediyse tarayıcıda o an açık olan sekme okunur.
            window = Desktop.Foreground() is { } front && Desktop.BrowserAddress(front) is not null
                ? front
                : Desktop.Windows().FirstOrDefault(candidate => Desktop.BrowserAddress(candidate) is not null);
            address = window is null ? "" : Desktop.BrowserAddress(window) ?? "";
        }

        if (!address.Contains("://", StringComparison.Ordinal) && address.Length > 0)
        {
            address = "https://" + address;
        }

        // Model ev ağındaki ya da bu bilgisayardaki bir adresi (modem arayüzü, yerel servisler) ancak kullanıcı o adresi
        // söylediyse okur; yoksa okunan bir sayfa Friday'i ağın içine bakmaya yönlendirebilirdi.
        if (window is null && ContentTrust.IsPrivateAddress(address) && Uri.TryCreate(address, UriKind.Absolute, out var inner)
            && !LastUserText.Contains(inner.Host, StringComparison.OrdinalIgnoreCase))
        {
            ledger.Record(DataFlow.Local, "Dış içerik kalkanı", $"{inner.Host} ev ağında; kullanıcı istemediği için okunmadı");
            return "Bu adres ev ağında ya da bu bilgisayarda; kullanıcı açıkça istemediği için okunmadı.";
        }

        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            try
            {
                var text = await _web.ReadAsync(uri, 6000, cancellationToken).ConfigureAwait(false);
                ledger.Record(DataFlow.Outbound, "Web sayfası", $"{uri.Host} okundu");
                if (text.Length > 300)
                {
                    return $"{uri.Host} sayfasının metni:\n{text}";
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // Giriş gerektiren sayfalar indirilemez; tarayıcıdaki görünür metin denenir.
            }
        }

        if (window is not null)
        {
            var visible = Desktop.ReadText(window, 6000);
            ledger.Record(DataFlow.Local, "Masaüstü", $"{window.Process} sekmesindeki görünür yazılar okundu");
            if (visible.Length > 100)
            {
                return $"Tarayıcıda açık sayfanın görünür metni:\n{visible}";
            }
        }

        return "Sayfa okunamadı. Açık bir tarayıcı sekmesi ya da geçerli bir adres gerekiyor.";
    }

    private static readonly Dictionary<string, string> CurrencyNames = new()
    {
        ["dolar"] = "USD", ["usd"] = "USD", ["euro"] = "EUR", ["avro"] = "EUR", ["eur"] = "EUR", ["sterlin"] = "GBP",
        ["pound"] = "GBP", ["gbp"] = "GBP", ["frank"] = "CHF", ["isvicre"] = "CHF", ["yen"] = "JPY", ["ruble"] = "RUB",
        ["yuan"] = "CNY", ["riyal"] = "SAR", ["dirhem"] = "AED", ["manat"] = "AZN", ["kanada"] = "CAD", ["avustralya"] = "AUD",
    };

    /// <summary>Güncel kurlar (TCMB ya da piyasa ortalaması, hangisi önce gelirse); sitelerin JavaScript'ine bağlı değil.</summary>
    private async Task<string> ExchangeRatesAsync(string currency, CancellationToken cancellationToken)
    {
        var (rates, source) = await GetRatesAsync().ConfigureAwait(false);
        var lines = CurrencyCodes(currency)
            .Where(rates.ContainsKey)
            .Select(code => $"1 {CurrencyLabel(code)} = {rates[code].ToString("0.####", Turkish)} TL")
            .ToList();
        return lines.Count == 0
            ? $"'{currency}' için kur bulunamadı."
            : $"Kaynak: {source}. " + string.Join("; ", lines);
    }

    private static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", "", StringComparison.Ordinal) : url;
}
