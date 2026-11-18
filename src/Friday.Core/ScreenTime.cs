using System.Globalization;
using System.Text.Json;

namespace Friday.Core;

/// <summary>
/// Ekran süresi: hangi uygulamada (tarayıcıda hangi bilinen sitede) ne kadar vakit geçtiği, gün gün. Yalnızca uygulama
/// ya da site adı ve saniye tutulur; pencere başlıkları, adresler ve içerikler saklanmaz. Son 14 gün kalır.
/// </summary>
public sealed class ScreenTimeLog
{
    private const int KeepDays = 14;

    private static readonly Dictionary<string, string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["brave"] = "Brave", ["opera"] = "Opera", ["vivaldi"] = "Vivaldi"
    };

    /// <summary>Tarayıcıda başlığından tanınan siteler (başlıkta geçen parça → gösterilen ad).</summary>
    private static readonly (string Needle, string Site)[] Sites =
    [
        ("YouTube", "YouTube"), ("Netflix", "Netflix"), ("Instagram", "Instagram"), ("TikTok", "TikTok"), ("Reddit", "Reddit"),
        ("Twitch", "Twitch"), ("Facebook", "Facebook"), ("Twitter", "X"), ("Disney+", "Disney+"), ("Prime Video", "Prime Video"),
        ("WhatsApp", "WhatsApp"), ("GitHub", "GitHub"), ("ChatGPT", "ChatGPT"), ("Claude", "Claude"), ("Gmail", "Gmail"),
        ("Outlook", "Outlook"), ("LinkedIn", "LinkedIn"), ("Wikipedia", "Vikipedi"), ("Vikipedi", "Vikipedi"), ("Google Docs", "Google Dokümanlar"),
        ("Spotify", "Spotify"), ("Stack Overflow", "Stack Overflow")
    ];

    /// <summary>Odak oturumunda dikkat dağıttığı kabul edilen siteler.</summary>
    private static readonly HashSet<string> Distractions = new(StringComparer.OrdinalIgnoreCase)
    {
        "YouTube", "Netflix", "Instagram", "TikTok", "Reddit", "Twitch", "Facebook", "X", "Disney+", "Prime Video"
    };

    private readonly Dictionary<string, Dictionary<string, int>> _days;

    public ScreenTimeLog()
        : this(new Dictionary<string, Dictionary<string, int>>())
    {
    }

    private ScreenTimeLog(Dictionary<string, Dictionary<string, int>> days) => _days = days;

    public static bool IsDistraction(string label) => Distractions.Contains(label);

    /// <summary>"chrome" + "Lo-fi müzik - YouTube — Google Chrome" → "YouTube"; "Code" + "Visual Studio Code" → "Visual Studio Code".</summary>
    public static string Label(string process, string? description, string title)
    {
        if (Browsers.TryGetValue(process, out var browser))
        {
            foreach (var (needle, site) in Sites)
            {
                if (title.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return site;
                }
            }

            return browser;
        }

        return string.IsNullOrWhiteSpace(description) ? TurkishGrammar.Capitalize(process) : description.Trim();
    }

    public void Add(DateTime when, string label, int seconds)
    {
        var day = Key(when);
        if (!_days.TryGetValue(day, out var apps))
        {
            _days[day] = apps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var old in _days.Keys.Order(StringComparer.Ordinal).SkipLast(KeepDays).ToList())
            {
                _days.Remove(old);
            }
        }

        apps[label] = apps.GetValueOrDefault(label) + seconds;
    }

    /// <summary>O günün uygulamaları, en çok kullanılan önce.</summary>
    public IReadOnlyList<(string App, int Seconds)> Day(DateTime when) =>
        _days.TryGetValue(Key(when), out var apps)
            ? [.. apps.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key, pair.Value))]
            : [];

    /// <summary>"Bugün 5 saat 12 dakika bilgisayardaydın. En çok Visual Studio Code (2 saat 10 dakika), YouTube (45 dakika)…"</summary>
    public string Describe(DateTime when, string dayName, string? app = null)
    {
        var apps = Day(when);
        if (apps.Count == 0)
        {
            return $"{TurkishGrammar.Capitalize(dayName)} için ekran süresi kaydı yok.";
        }

        if (!string.IsNullOrWhiteSpace(app))
        {
            var wanted = Fold(app);
            var match = apps.FirstOrDefault(item => Fold(item.App).Contains(wanted, StringComparison.Ordinal) || wanted.Contains(Fold(item.App), StringComparison.Ordinal));
            return match.App is null
                ? $"{TurkishGrammar.Capitalize(dayName)} {app} kullanmadın."
                : $"{TurkishGrammar.Capitalize(dayName)} {match.App} için {Spoken(match.Seconds)} harcadın.";
        }

        var total = apps.Sum(item => item.Seconds);
        var top = string.Join(", ", apps.Take(3).Select(item => $"{item.App} ({Spoken(item.Seconds)})"));
        return $"{TurkishGrammar.Capitalize(dayName)} {Spoken(total)} bilgisayar başındaydın. En çok: {top}.";
    }

    /// <summary>3720 → "1 saat 2 dakika"; 40 → "1 dakikadan az".</summary>
    public static string Spoken(int seconds)
    {
        var minutes = seconds / 60;
        return minutes switch
        {
            < 1 => "1 dakikadan az",
            < 60 => $"{minutes} dakika",
            _ when minutes % 60 == 0 => $"{minutes / 60} saat",
            _ => $"{minutes / 60} saat {minutes % 60} dakika"
        };
    }

    /// <summary>3720 → "1 sa 2 dk".</summary>
    public static string Compact(int seconds)
    {
        var minutes = seconds / 60;
        return minutes < 60 ? $"{Math.Max(1, minutes)} dk" : minutes % 60 == 0 ? $"{minutes / 60} sa" : $"{minutes / 60} sa {minutes % 60} dk";
    }

    public string ToJson() => JsonSerializer.Serialize(_days);

    public static ScreenTimeLog FromJson(string json)
    {
        var days = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json) ?? [];
        return new ScreenTimeLog(days.ToDictionary(
            pair => pair.Key,
            pair => new Dictionary<string, int>(pair.Value, StringComparer.OrdinalIgnoreCase)));
    }

    private static string Key(DateTime when) => when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Fold(string text) => new(text.ToLower(CultureInfo.GetCultureInfo("tr-TR"))
        .Replace('ı', 'i').Replace('ü', 'u').Replace('ö', 'o').Replace('ş', 's').Replace('ç', 'c').Replace('ğ', 'g')
        .Where(char.IsLetterOrDigit).ToArray());
}
