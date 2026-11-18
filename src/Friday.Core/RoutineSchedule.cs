using System.Globalization;

namespace Friday.Core;

/// <summary>
/// Rutinin kendiliğinden çalışma zamanı. settings.json'da "09:00 haftaici", "18:30 hergun", "10:00 haftasonu" ya da
/// "acilis" (Friday açılınca, ör. Windows'la başlarken) olarak yazılır.
/// </summary>
public sealed record RoutineSchedule(TimeOnly? At, string Days, bool OnStart)
{
    /// <summary>Olay: "donus" (bilgisayara dönünce) ya da "uygulama:spotify" (o uygulama öne gelince).</summary>
    public string? Trigger { get; init; }

    public const string Startup = "acilis";

    /// <summary>Düzenleyicideki gün seçenekleri (anahtar, etiket).</summary>
    public static IReadOnlyList<(string Key, string Label)> DayOptions { get; } =
    [
        ("hergun", "Her gün"),
        ("haftaici", "Hafta içi"),
        ("haftasonu", "Hafta sonu")
    ];

    public static RoutineSchedule? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.Equals("donus", StringComparison.OrdinalIgnoreCase))
        {
            return new RoutineSchedule(null, "hergun", OnStart: false) { Trigger = "donus" };
        }

        if (trimmed.StartsWith("uygulama:", StringComparison.OrdinalIgnoreCase) && trimmed.Length > "uygulama:".Length + 1)
        {
            return new RoutineSchedule(null, "hergun", OnStart: false) { Trigger = "uygulama:" + trimmed["uygulama:".Length..].Trim() };
        }

        var parts = trimmed.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is [Startup])
        {
            return new RoutineSchedule(null, "hergun", OnStart: true);
        }

        var clock = parts[0].Replace('.', ':');
        if (!TimeOnly.TryParseExact(clock, ["H:mm", "HH:mm", "%H"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            return null;
        }

        var days = parts.Length > 1 && DayOptions.Any(option => option.Key == parts[1]) ? parts[1] : "hergun";
        return new RoutineSchedule(at, days, OnStart: false);
    }

    public string Format() => Trigger ?? (OnStart ? Startup : $"{At!.Value.ToString("HH:mm", CultureInfo.InvariantCulture)} {Days}");

    /// <summary>"Hafta içi her gün 09:00", "Her gün 18:30", "Friday açılınca".</summary>
    public string Describe() => Trigger is { } trigger
        ? trigger == "donus" ? "Bilgisayara dönünce" : $"{TurkishGrammar.Capitalize(trigger["uygulama:".Length..])} açılınca"
        : OnStart
        ? "Friday açılınca"
        : $"{Days switch { "haftaici" => "Hafta içi her gün", "haftasonu" => "Hafta sonu", _ => "Her gün" }} {At!.Value.ToString("HH:mm", CultureInfo.InvariantCulture)}";

    public bool Matches(DayOfWeek day) => Days switch
    {
        "haftaici" => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
        "haftasonu" => day is DayOfWeek.Saturday or DayOfWeek.Sunday,
        _ => true
    };

    /// <summary>
    /// Saat geldiyse ve bugün bu saatte daha çalışmadıysa. Üç dakikalık bir pencere var: Friday o an konuşuyorsa bir
    /// sonraki yoklamada çalışır; bilgisayar o saatte kapalıysa sonradan çalışmaz (sabah rutini öğlen açılmaz).
    /// </summary>
    public bool IsDue(DateTime now, DateTime? lastRun)
    {
        if (OnStart || At is not { } at || !Matches(now.DayOfWeek))
        {
            return false;
        }

        var due = now.Date + at.ToTimeSpan();
        return now >= due && now < due.AddMinutes(3) && (lastRun is null || lastRun < due);
    }
}
