using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Friday.Core;

public sealed record Reminder(string Id, DateTime Due, string Text, DateTime Created)
{
    /// <summary>Tekrar kuralı ("gunluk", "haftaici", "haftasonu", "haftalik", "aylik"); tek seferlikse null.</summary>
    public string? Repeat { get; init; }

    public bool Repeats => Repeat is not null;

    /// <summary>Saat yerine olay: "donus" (bilgisayara dönünce) ya da "uygulama:chrome" (o uygulama öne gelince).</summary>
    public string? Trigger { get; init; }

    /// <summary>"Bilgisayara dönünce", "Chrome açılınca".</summary>
    public string? TriggerText => Trigger switch
    {
        null => null,
        "donus" => "Bilgisayara dönünce",
        var app => $"{TurkishGrammar.Capitalize(app["uygulama:".Length..])} açılınca"
    };
}

/// <summary>Tekrarlayan hatırlatmaların kuralları: her gün, hafta içi, hafta sonu, her hafta, her ay.</summary>
public static partial class RepeatRule
{
    public static readonly IReadOnlyList<string> Known = ["gunluk", "haftaici", "haftasonu", "haftalik", "aylik"];

    /// <summary>Aralıklı hatırlatmalar gece susar: 22.00'den sonraki ilk tekrar ertesi sabah 08.00'e kayar.</summary>
    private static readonly TimeSpan DayStart = TimeSpan.FromHours(8);
    private static readonly TimeSpan DayEnd = TimeSpan.FromHours(22);

    /// <summary>"her:30" → 30 dakika; aralık kuralı değilse null.</summary>
    public static int? Interval(string? rule) =>
        rule is not null && rule.StartsWith("her:", StringComparison.Ordinal)
        && int.TryParse(rule.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes >= 5
            ? minutes
            : null;

    /// <summary>Bilinen tekrar kuralı mı (günlük, hafta içi… ya da "her:N" dakika)?</summary>
    public static bool IsKnown(string? rule) => rule is not null && (Known.Contains(rule) || Interval(rule) is not null);

    private static DateTime Awake(DateTime time) =>
        time.TimeOfDay < DayStart ? time.Date + DayStart
        : time.TimeOfDay >= DayEnd ? time.Date.AddDays(1) + DayStart
        : time;

    /// <summary>
    /// Zaman ifadesindeki tekrar: "her sabah 08:00" → ("gunluk", "sabah 08:00"), "her pazartesi 10'da" → ("haftalik",
    /// "pazartesi 10'da"), "hafta içi her gün 8:30" → ("haftaici", "8:30"). Tekrar yoksa null. Dil modeli tekrarı zaman
    /// alanına yazıyordu ("her sabah 08:00") ve hatırlatma tek seferlik "yarın 08:00" olarak kuruluyordu.
    /// </summary>
    public static (string Rule, string When)? FromPhrase(string when)
    {
        var match = RepeatPhrase().Match(when.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR")));
        if (!match.Success)
        {
            return null;
        }

        var unit = match.Groups["wd"].Success ? match.Groups["wd"].Value : match.Groups["r"].Value;
        if (FromWords(unit) is not { } rule)
        {
            return null;
        }

        var part = match.Groups["r"].Value;
        var keep = part is "sabah" or "akşam" or "gece" or "öğlen" or "pazartesi" or "salı" or "çarşamba" or "perşembe" or "cuma" or "cumartesi" or "pazar";
        var rest = match.Groups["rest"].Value.Trim();
        return (rule, ((keep ? part + " " : "") + rest).Trim());
    }

    [GeneratedRegex(@"^(?:(?<wd>hafta içi|hafta sonu) )?her (?<r>gün|sabah|akşam|gece|öğlen|pazartesi|salı|çarşamba|perşembe|cuma|cumartesi|pazar|hafta|ay)(?: günü)?(?<rest>.*)$")]
    private static partial Regex RepeatPhrase();

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
        if (Interval(rule) is not null)
        {
            return Awake(due);
        }

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
        if (Interval(rule) is { } minutes)
        {
            var step = due;
            do
            {
                step = step.AddMinutes(minutes);
            }
            while (step <= now);

            return Awake(step);
        }

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
        if (Interval(rule) is { } every)
        {
            return every switch
            {
                60 => "her saat",
                30 => "her yarım saatte bir",
                _ when every % 60 == 0 => $"her {every / 60} saatte bir",
                _ => $"her {every} dakikada bir"
            };
        }

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
public sealed partial class ReminderStore : IRestorableStore
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

    /// <inheritdoc/>
    public string Title => "hatırlatıcılar";

    /// <inheritdoc/>
    public void Restore(string file, string? content)
    {
        lock (_gate)
        {
            _items = content is null ? [] : JsonSerializer.Deserialize<List<Reminder>>(content, Options) ?? [];
            UndoLog.WriteBack(file, content);
        }

        Changed?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Olaya bağlı hatırlatma: zamanı yoktur, olay gelince bir kez söylenir.</summary>
    public Reminder AddTriggered(string trigger, string text, DateTime now)
    {
        var reminder = new Reminder(Guid.NewGuid().ToString("N")[..8], DateTime.MaxValue, text.Trim(), now) { Trigger = trigger };
        lock (_gate)
        {
            _items.Add(reminder);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return reminder;
    }

    /// <summary>Olay geldi: "donus" ya da öne gelen uygulamanın adı. Eşleşenler listeden çıkar ve döner.</summary>
    public IReadOnlyList<Reminder> TakeTriggered(string trigger, string? app = null)
    {
        List<Reminder> fired;
        lock (_gate)
        {
            fired = _items.Where(item => item.Trigger is { } wanted && (wanted == trigger
                || (app is not null && wanted.StartsWith("uygulama:", StringComparison.Ordinal) && AppMatches(wanted["uygulama:".Length..], app)))).ToList();
            if (fired.Count == 0)
            {
                return [];
            }

            _items = [.. _items.Except(fired)];
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return fired;
    }

    /// <summary>"chrome" ↔ "Chrome", "vs code" ↔ "Visual Studio Code"? Harfleri sadeleştirip içerme bakılır.</summary>
    public static bool AppMatches(string wanted, string app)
    {
        static string Fold(string text) => new(text.ToLower(CultureInfo.GetCultureInfo("tr-TR"))
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
            .Where(char.IsLetterOrDigit).ToArray());
        var a = Fold(wanted);
        var b = Fold(app);
        return a.Length >= 3 && b.Length >= 3 && (b.Contains(a, StringComparison.Ordinal) || a.Contains(b, StringComparison.Ordinal));
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

    /// <summary>
    /// Saatli bir hatırlatmayı yeni zamanına taşır ("dişçi hatırlatmasını yarın 3'e al"). Hatırlatma metninde aranan söz
    /// geçenlerin en yakını seçilir; söz boşsa en son kurulan. Yeni zamanı eskisinden hesaplayan <paramref name="retime"/>
    /// null dönerse taşınmaz. Taşınamadıysa null; taşındıysa eski ve yeni hali.
    /// </summary>
    /// <summary>Metninde aranan sözcükler (kök eşleşmesiyle) geçen en yakın hatırlatma ("dişçi" → "Dişçi randevusu"); yoksa null.</summary>
    public Reminder? Find(string query, DateTime now)
    {
        var needle = query.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        if (needle.Length < 2)
        {
            return null;
        }

        lock (_gate)
        {
            return _items.Where(item => item.Due > now && item.Text.Length > 0 && Mentions(item.Text, needle)).MinBy(item => item.Due);
        }
    }

    /// <summary>
    /// "Dişçi var, bir saat önce": hatırlatmanın metnindeki "N dakika/saat/gün önce(den)" kısmı; zamandan düşülecek süre ve
    /// kalan metin. Yoksa null. Eskiden bu söz metne yazılıyor, hatırlatma etkinliğin kendi saatine kuruluyordu.
    /// </summary>
    public static (TimeSpan Lead, string Text)? LeadTime(string text)
    {
        var match = LeadPhrase().Match(text.Trim());
        if (!match.Success || QuickIntents.ParseNumber(match.Groups["n"].Value) is not { } amount || amount <= 0)
        {
            return null;
        }

        var lead = match.Groups["u"].Value switch
        {
            "saat" => TimeSpan.FromHours(amount),
            "gün" => TimeSpan.FromDays(amount),
            _ => TimeSpan.FromMinutes(amount)
        };
        var rest = text.Trim()[..match.Index].Trim().TrimEnd(',').Trim();
        rest = TrailingVar().Replace(rest, "");
        return (lead, rest);
    }

    [GeneratedRegex(@"[,]?\s*(?<n>\d+|yarım|\p{L}+(?: \p{L}+)?) (?<u>dakika|saat|gün) önce(?:den)?(?: haber ver| hatırlat)?$")]
    private static partial Regex LeadPhrase();

    // "dişçi var" → "dişçi": etkinliğin adı kalsın.
    [GeneratedRegex(@" var$")]
    private static partial Regex TrailingVar();

    public (Reminder Old, Reminder New)? Move(string query, Func<Reminder, DateTime?> retime)
    {
        var needle = query.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        (Reminder Old, Reminder New) moved;
        lock (_gate)
        {
            var timed = _items.Where(item => item.Trigger is null && item.Text != QuickIntents.AlarmText).ToList();
            var found = needle.Length == 0
                ? timed.MaxBy(item => item.Created)
                : timed.Where(item => Mentions(item.Text, needle)).MinBy(item => item.Due);
            if (found is null || retime(found) is not { } due)
            {
                return null;
            }

            moved = (found, found with { Due = due });
            _items[_items.IndexOf(found)] = moved.New;
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return moved;
    }

    /// <summary>"dişçi" → "dişçiyi aramamı", "annemi arama" → "annemi aramayı": metin ya da sözcük kökleri tutuyor mu?</summary>
    /// <summary>Hatırlatmanın metninde değil zamanında geçen sözler ("bu akşamki", "yarınki").</summary>
    private static readonly HashSet<string> TimeWords = new(StringComparer.Ordinal)
    {
        "bu", "şu", "bugün", "bugünkü", "yarın", "yarınki", "dün", "dünkü", "sabah", "sabahki", "öğlen", "öğlenki", "öğleki",
        "akşam", "akşamki", "gece", "geceki", "haftaki", "haftaya", "saat", "için"
    };

    private static bool Mentions(string text, string needle)
    {
        var lower = text.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        return lower.Contains(needle, StringComparison.Ordinal)
            || MemoryRecall.Words(needle).All(word => MemoryRecall.Words(lower).Any(other => MemoryRecall.Same(word, other)));
    }

    /// <summary>Metninde aranan ifade (ya da sözcük kökleri: "dişçi" → "dişçiyi aramamı") geçen hatırlatmaları siler.</summary>
    public int Cancel(string query)
    {
        var needle = query.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        // "Bu akşamki Mert hatırlatması": zaman sözleri metinde geçmez, aranmaz; o zaman yalnızca en yakın eşleşen silinir.
        var tokens = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var content = tokens.Where(token => !TimeWords.Contains(token)).ToList();
        var timed = content.Count > 0 && content.Count < tokens.Length;
        if (timed)
        {
            needle = string.Join(' ', content);
        }

        int removed;
        lock (_gate)
        {
            if (timed)
            {
                var first = _items.Where(item => Mentions(item.Text, needle)).MinBy(item => item.Due);
                removed = first is not null && _items.Remove(first) ? 1 : 0;
            }
            else
            {
                removed = needle.Length == 0
                    ? 0
                    : _items.RemoveAll(item => Mentions(item.Text, needle));
            }

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
    public static string Announcement(Reminder reminder) => Announcement(reminder, TurkishGrammar.ToYou(reminder.Text));

    private static string Announcement(Reminder reminder, string text) =>
        reminder.Trigger is not null
            ? TurkishGrammar.IsAccusative(text)
                ? $"{(reminder.Trigger == "donus" ? "Hoş geldin! " : "")}{TurkishGrammar.Capitalize(text)} unutma."
                : $"{(reminder.Trigger == "donus" ? "Hoş geldin! " : "")}Hatırlatma: {text}."
            : reminder.Text == QuickIntents.AlarmText
            ? $"Günaydın! Saat {reminder.Due.ToString("HH:mm", CultureInfo.InvariantCulture)}, uyanma vakti."
            : reminder.Text.Length == 0
            ? $"{TurkishGrammar.Capitalize(TurkishGrammar.Duration((reminder.Due - reminder.Created).TotalMinutes))} doldu."
            : TurkishGrammar.IsAccusative(text)
                ? $"{TurkishGrammar.Capitalize(text)} unutma, zamanı geldi."
                : $"Hatırlatma: {text}.";

    private void Save()
    {
        UndoLog.Touching(this, _file);
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_items, Options));
    }
}

public sealed record Note(DateTime Time, string Text);

/// <summary>Sesli notlar: D:\Friday\data\notes.md, her satır bir not. Dosya Not Defteri'yle de okunup düzenlenebilir.</summary>
public sealed class NoteStore(string file) : IRestorableStore
{
    private readonly object _gate = new();

    public event EventHandler? Changed;

    public string File => file;

    /// <inheritdoc/>
    public string Title => "notlar";

    /// <inheritdoc/>
    public void Restore(string file, string? content)
    {
        lock (_gate)
        {
            UndoLog.WriteBack(file, content);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

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
            UndoLog.Touching(this, file);
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
