using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

public sealed record Reminder(string Id, DateTime Due, string Text, DateTime Created)
{
    /// <summary>Tekrar kuralı ("gunluk", "haftaici", "haftasonu", "haftalik", "aylik"); tek seferlikse null.</summary>
    public string? Repeat { get; init; }

    public bool Repeats => Repeat is not null;
}

/// <summary>Tekrarlayan hatırlatmaların kuralları: her gün, hafta içi, hafta sonu, her hafta, her ay.</summary>
public static class RepeatRule
{
    public static readonly IReadOnlyList<string> Known = ["gunluk", "haftaici", "haftasonu", "haftalik", "aylik"];

    /// <summary>"gün" → "gunluk", "hafta içi" → "haftaici", "pazartesi" → "haftalik"; tanınmazsa null.</summary>
    public static string? FromWords(string words) => words.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR")) switch
    {
        "gün" or "sabah" or "akşam" or "gece" or "öğlen" => "gunluk",
        "hafta içi" => "haftaici",
        "hafta sonu" => "haftasonu",
        "hafta" or "pazartesi" or "salı" or "çarşamba" or "perşembe" or "cuma" or "cumartesi" or "pazar" => "haftalik",
        "ay" => "aylik",
        _ => null
    };

    /// <summary>
    /// Tekrarlayan hatırlatmanın ilk zamanı. "Her gün 9'da" sabah dokuzdur: sabah/akşam denmediyse 7–11 arası saatler
    /// öğleden öncedir (tek seferlik "9'da" ise sıradaki dokuz olabilir). Bugünün saati geçtiyse ilk tekrar sonraki gündür.
    /// </summary>
    public static DateTime Start(DateTime parsed, string phrase, string rule, DateTime now)
    {
        var lowered = phrase.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        var qualified = lowered.Contains("sabah", StringComparison.Ordinal) || lowered.Contains("öğle", StringComparison.Ordinal)
                        || lowered.Contains("akşam", StringComparison.Ordinal) || lowered.Contains("gece", StringComparison.Ordinal);
        var time = parsed.TimeOfDay;
        if (!qualified && parsed.Hour is >= 19 and <= 23)
        {
            time -= TimeSpan.FromHours(12);
        }

        var start = (rule == "haftalik" || rule == "aylik" ? parsed.Date : now.Date) + time;
        while (start <= now)
        {
            start = rule switch
            {
                "haftalik" => start.AddDays(7),
                "aylik" => start.AddMonths(1),
                _ => start.AddDays(1)
            };
        }

        return First(start, rule);
    }

    /// <summary>İlk tarih kurala uymuyorsa (ör. hafta içi ama cumartesi) uyan ilk güne kaydırılır.</summary>
    public static DateTime First(DateTime due, string rule)
    {
        var date = due;
        while (!Fits(date, rule))
        {
            date = date.AddDays(1);
        }

        return date;
    }

    /// <summary>Bir sonraki tekrar; "now"dan sonraki ilk uygun zaman.</summary>
    public static DateTime Next(DateTime due, string rule, DateTime now)
    {
        var next = due;
        do
        {
            next = rule switch
            {
                "haftalik" => next.AddDays(7),
                "aylik" => next.AddMonths(1),
                _ => next.AddDays(1)
            };
        }
        while (next <= now || !Fits(next, rule));

        return next;
    }

    private static bool Fits(DateTime date, string rule) => rule switch
    {
        "haftaici" => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
        "haftasonu" => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
        _ => true
    };

    /// <summary>"her gün 09:00", "hafta içi her gün 08:30", "her pazartesi 10:00", "her ay 5'inde 09:00".</summary>
    public static string Describe(DateTime due, string rule)
    {
        var time = due.ToString("HH:mm", CultureInfo.InvariantCulture);
        return rule switch
        {
            "haftaici" => $"hafta içi her gün {time}",
            "haftasonu" => $"hafta sonları {time}",
            "haftalik" => $"her {due.ToString("dddd", CultureInfo.GetCultureInfo("tr-TR")).ToLower(CultureInfo.GetCultureInfo("tr-TR"))} {time}",
            "aylik" => $"her ayın {due.Day}. günü {time}",
            _ => $"her gün {time}"
        };
    }
}

/// <summary>
/// Kalıcı hatırlatmalar (D:\Friday\data\reminders.json). "10 dakika sonra" da "yarın 9'da" da buraya yazılır; Friday
/// kapanıp açılsa bile kaybolmaz. Friday kapalıyken zamanı geçenler açılışta "sen yokken" diye söylenir.
/// </summary>
public sealed class ReminderStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _file;
    private readonly object _gate = new();
    private List<Reminder> _items;

    public ReminderStore(string file)
    {
        _file = file;
        try
        {
            _items = File.Exists(file) ? JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(file), Options) ?? [] : [];
        }
        catch (JsonException)
        {
            _items = [];
        }
    }

    public event EventHandler? Changed;

    /// <summary>Zamanı gelmemiş hatırlatmalar, en yakını önce.</summary>
    public IReadOnlyList<Reminder> Upcoming
    {
        get
        {
            lock (_gate)
            {
                return [.. _items.OrderBy(item => item.Due)];
            }
        }
    }

    public Reminder Add(DateTime due, string text, DateTime now, string? repeat = null)
    {
        var reminder = new Reminder(Guid.NewGuid().ToString("N")[..8], repeat is null ? due : RepeatRule.First(due, repeat), text.Trim(), now)
        {
            Repeat = repeat
        };
        lock (_gate)
        {
            _items.Add(reminder);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return reminder;
    }

    /// <summary>
    /// Zamanı gelenleri döndürür (her biri bir kez söylenir). Tek seferlikler listeden çıkar, tekrarlayanlar bir sonraki
    /// zamanlarına kurulur.
    /// </summary>
    public IReadOnlyList<Reminder> TakeDue(DateTime now)
    {
        List<Reminder> due;
        lock (_gate)
        {
            due = _items.Where(item => item.Due <= now).OrderBy(item => item.Due).ToList();
            if (due.Count == 0)
            {
                return [];
            }

            _items = [.. _items.Except(due), .. due.Where(item => item.Repeat is not null)
                .Select(item => item with { Due = RepeatRule.Next(item.Due, item.Repeat!, now) })];
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return due;
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            if (_items.RemoveAll(item => item.Id == id) == 0)
            {
                return false;
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Metninde aranan ifade geçen hatırlatmaları siler.</summary>
    public int Cancel(string query)
    {
        var needle = query.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        int removed;
        lock (_gate)
        {
            removed = needle.Length == 0
                ? 0
                : _items.RemoveAll(item => item.Text.ToLower(CultureInfo.GetCultureInfo("tr-TR")).Contains(needle, StringComparison.Ordinal));
            if (removed > 0)
            {
                Save();
            }
        }

        if (removed > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>Söylenecek cümle: "Toplantıyı unutma, zamanı geldi." ya da "Hatırlatma: ilaç."</summary>
    public static string Announcement(Reminder reminder) =>
        reminder.Text == QuickIntents.AlarmText
            ? $"Günaydın! Saat {reminder.Due.ToString("HH:mm", CultureInfo.InvariantCulture)}, uyanma vakti."
            : reminder.Text.Length == 0
            ? $"{TurkishGrammar.Capitalize(TurkishGrammar.Duration((reminder.Due - reminder.Created).TotalMinutes))} doldu."
            : TurkishGrammar.IsAccusative(reminder.Text)
                ? $"{TurkishGrammar.Capitalize(reminder.Text)} unutma, zamanı geldi."
                : $"Hatırlatma: {reminder.Text}.";

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_items, Options));
    }
}

public sealed record Note(DateTime Time, string Text);

/// <summary>Sesli notlar: D:\Friday\data\notes.md, her satır bir not. Dosya Not Defteri'yle de okunup düzenlenebilir.</summary>
public sealed class NoteStore(string file)
{
    private readonly object _gate = new();

    public event EventHandler? Changed;

    public string File => file;

    public IReadOnlyList<Note> All
    {
        get
        {
            lock (_gate)
            {
                if (!System.IO.File.Exists(file))
                {
                    return [];
                }

                return System.IO.File.ReadAllLines(file)
                    .Select(Parse)
                    .OfType<Note>()
                    .ToList();
            }
        }
    }

    public Note Add(string text, DateTime now)
    {
        var note = new Note(now, text.Trim());
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (!System.IO.File.Exists(file))
            {
                System.IO.File.WriteAllText(file, "# Notlar\n\n");
            }

            System.IO.File.AppendAllText(file, $"- [{now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}] {note.Text}\n");
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return note;
    }

    private static Note? Parse(string line)
    {
        // "- [2026-11-17 14:35] süt al"
        var close = line.IndexOf(']', StringComparison.Ordinal);
        if (!line.StartsWith("- [", StringComparison.Ordinal) || close < 0)
        {
            return null;
        }

        return DateTime.TryParseExact(line[3..close], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? new Note(time, line[(close + 1)..].Trim())
            : null;
    }
}
