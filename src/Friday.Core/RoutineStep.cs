using System.Globalization;

namespace Friday.Core;

public enum RoutineAction
{
    App,
    Website,
    Search,
    Media,
    Say,
    Briefing,
    LinguaFlow,

    /// <summary>Ritim koçu: "ritim:mola", "ritim:odak 25", "ritim:nefes".</summary>
    Rhythm,

    /// <summary>Ses seviyesi yüzdesi: "ses:30".</summary>
    Volume,

    /// <summary>Sonraki adımdan önce bekle (saniye): "bekle:5".</summary>
    Wait,

    /// <summary>Hatırlatma kurar: "hatirlat:10 dakika sonra|çayı demle".</summary>
    Remind,

    /// <summary>Ortam sesi: "ortam:yagmur", "ortam:kahverengi 50", "ortam:kapat".</summary>
    Ambient
}

/// <summary>Rutin düzenleyicisindeki bir adım türü: dosyadaki adı, paneldeki etiketi, değer ipucu ve simgesi.</summary>
public sealed record RoutineStepKind(RoutineAction Action, string Key, string Label, string Hint, string Glyph)
{
    public bool NeedsValue => Action is not (RoutineAction.Briefing or RoutineAction.LinguaFlow);
}

/// <summary>Hazır rutin şablonu.</summary>
public sealed record RoutineTemplate(string Name, string Description, IReadOnlyList<string> Steps);

/// <summary>Rutin adımı: settings.json'da "uygulama:Spotify", "site:github.com", "soyle:Hazırım" gibi yazılır.</summary>
public sealed record RoutineStep(RoutineAction Action, string Value)
{
    /// <summary>Düzenleyicide sunulan adım türleri, sırasıyla.</summary>
    public static IReadOnlyList<RoutineStepKind> Kinds { get; } =
    [
        new(RoutineAction.App, "uygulama", "Uygulama aç", "Spotify, Visual Studio, not defteri…", ""),
        new(RoutineAction.Website, "site", "Site aç", "github.com", ""),
        new(RoutineAction.Search, "ara", "Web'de ara", "bugünün haberleri", ""),
        new(RoutineAction.Media, "medya", "Medya", "oynat_duraklat, sonraki, onceki ya da sessiz", ""),
        new(RoutineAction.Volume, "ses", "Ses seviyesi", "0–100 arası yüzde, ör. 30", ""),
        new(RoutineAction.Say, "soyle", "Bir şey söyle", "Çalışma modu hazır. Kolay gelsin.", ""),
        new(RoutineAction.Briefing, "ozet", "Günün özetini oku", "", ""),
        new(RoutineAction.Rhythm, "ritim", "Ritim", "mola, odak 25, nefes, esneme, durum ya da ertele 60", ""),
        new(RoutineAction.Remind, "hatirlat", "Hatırlatma kur", "45 dakika sonra|ara ver", ""),
        new(RoutineAction.Wait, "bekle", "Bekle", "saniye, ör. 3", ""),
        new(RoutineAction.Ambient, "ortam", "Ortam sesi", "yagmur, beyaz, kahverengi, dalga, somine ya da ruzgar (+ dakika)", "\uE93C"),
        new(RoutineAction.LinguaFlow, "linguaflow", "LinguaFlow çevirisi", "", ""),
    ];

    /// <summary>"Şablondan başla" listesi.</summary>
    public static IReadOnlyList<RoutineTemplate> Templates { get; } =
    [
        new("sabah", "Günün özeti, sesi ayarla, müziği başlat",
            ["ozet", "ses:25", "medya:oynat_duraklat"]),
        new("odak modu", "Müziği durdur, 50 dakika odak, kısa bir söz",
            ["medya:oynat_duraklat", "ritim:odak 50", "ortam:yagmur 50", "soyle:Odak başladı. Telefonu ters çevirmeye ne dersin?"]),
        new("kod zamanı", "Editörü ve GitHub'ı aç, 25 dakikalık odak başlat",
            ["uygulama:Visual Studio Code", "site:github.com", "ritim:odak 25"]),
        new("oyun modu", "Steam ve Discord'u aç, sesi aç, mola önerisini 2 saat ertele",
            ["uygulama:Steam", "uygulama:Discord", "ses:60", "ritim:ertele 120", "soyle:İyi oyunlar!"]),
        new("film gecesi", "Müziği durdur, sesi aç, iyi seyirler de",
            ["medya:oynat_duraklat", "ses:70", "soyle:İyi seyirler. Patlamış mısır hazırsa başlayabiliriz."]),
        new("çay molası", "Mola başlat, 5 dakika sonra çayı hatırlat",
            ["ritim:mola", "hatirlat:5 dakika sonra|çay demlendi"]),
        new("gün sonu", "Günün ritmini söyle, sesi kıs, iyi geceler de",
            ["ritim:durum", "ses:20", "soyle:Bugünlük bu kadar. İyi dinlenmeler."]),
    ];

    public RoutineStepKind Kind => Kinds.First(kind => kind.Action == Action);

    /// <summary>Sayı değeri ("ses:30" → 30, "bekle:3" → 3).</summary>
    public int Number => int.TryParse(Value.TrimEnd('%', 's', 'n', ' '), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;

    public static RoutineStep Parse(string text)
    {
        var separator = text.IndexOf(':', StringComparison.Ordinal);
        var kind = (separator < 0 ? text : text[..separator]).Trim().ToLowerInvariant();
        var value = separator < 0 ? "" : text[(separator + 1)..].Trim();
        var action = kind switch
        {
            "uygulama" or "app" => RoutineAction.App,
            "site" => RoutineAction.Website,
            "ara" => RoutineAction.Search,
            "medya" => RoutineAction.Media,
            "soyle" or "söyle" => RoutineAction.Say,
            "ozet" or "özet" => RoutineAction.Briefing,
            "linguaflow" => RoutineAction.LinguaFlow,
            "ritim" => RoutineAction.Rhythm,
            "ses" => RoutineAction.Volume,
            "bekle" => RoutineAction.Wait,
            "hatirlat" or "hatırlat" => RoutineAction.Remind,
            "ortam" => RoutineAction.Ambient,
            _ => throw new FormatException($"Bilinmeyen rutin adımı: {text}")
        };

        if (value.Length == 0 && action is not (RoutineAction.Briefing or RoutineAction.LinguaFlow))
        {
            throw new FormatException($"Rutin adımında değer eksik: {text}");
        }

        if (action is RoutineAction.Volume or RoutineAction.Wait
            && !int.TryParse(value.TrimEnd('%', 's', 'n', ' '), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            throw new FormatException($"Rutin adımında sayı bekleniyordu: {text}");
        }

        if (action is RoutineAction.Remind && value.Split('|') is not [{ Length: > 0 }, { Length: > 0 }])
        {
            throw new FormatException($"Hatırlatma adımı \"zaman|metin\" biçiminde olmalı: {text}");
        }

        return new RoutineStep(action, value);
    }

    /// <summary>"yagmur" → "Yağmur sesi".</summary>
    public static string AmbientName(string kind) => kind switch
    {
        "yagmur" => "Yağmur sesi",
        "beyaz" => "Beyaz gürültü",
        "pembe" => "Pembe gürültü",
        "kahverengi" => "Kahverengi gürültü",
        "dalga" => "Dalga sesi",
        "somine" => "Şömine sesi",
        "ruzgar" => "Rüzgâr sesi",
        _ => "Ortam sesi"
    };

    /// <summary>settings.json'daki yazımı: "uygulama:Spotify", "ozet".</summary>
    public string Format() => Kind.NeedsValue ? $"{Kind.Key}:{Value}" : Kind.Key;

    /// <summary>Paneldeki okunuşu: "Spotify uygulamasını aç", "Sesi %30 yap", "3 sn bekle".</summary>
    public string Describe() => Action switch
    {
        RoutineAction.App => $"{Value} uygulamasını aç",
        RoutineAction.Website => $"{Value} sitesini aç",
        RoutineAction.Search => $"\"{Value}\" diye ara",
        RoutineAction.Media => Value.Replace('_', ' ') switch
        {
            "oynat duraklat" => "Müziği oynat ya da duraklat",
            "sonraki" => "Sonraki şarkı",
            "onceki" => "Önceki şarkı",
            "sessiz" => "Sesi kapat",
            var other => $"Medya: {other}"
        },
        RoutineAction.Volume => $"Sesi %{Math.Clamp(Number, 0, 100)} yap",
        RoutineAction.Say => $"\"{Value}\" de",
        RoutineAction.Briefing => "Günün özetini oku",
        RoutineAction.Rhythm => Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) switch
        {
            ["mola", ..] => "Rehberli mola, dönünce kaldığın yeri söyle",
            ["odak", var minutes, ..] => $"{minutes} dakikalık odak başlat",
            ["odak"] => "Odak oturumu başlat",
            ["nefes", ..] => "Nefes egzersizi",
            ["esneme", ..] => "Esneme molası",
            ["durum", ..] => "Günün ritmini söyle",
            ["ertele", var minutes, ..] => $"Mola önerisini {minutes} dakika ertele",
            _ => $"Ritim: {Value}"
        },
        RoutineAction.Remind => Value.Split('|', 2) is [var when, var what] ? $"{when.Trim()} \"{what.Trim()}\" hatırlat" : $"Hatırlat: {Value}",
        RoutineAction.Wait => $"{Number} sn bekle",
        RoutineAction.Ambient => Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) switch
        {
            ["kapat", ..] => "Ortam sesini kapat",
            [var kind, var minutes, ..] => $"{AmbientName(kind)} çal ({minutes} dk)",
            [var kind] => $"{AmbientName(kind)} çal",
            _ => "Ortam sesi"
        },
        RoutineAction.LinguaFlow => "LinguaFlow çevirisini başlat",
        _ => Value
    };
}
