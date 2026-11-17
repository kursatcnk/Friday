using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

public sealed record Reminder(string Id, DateTime Due, string Text, DateTime Created);

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

    public Reminder Add(DateTime due, string text, DateTime now)
    {
        var reminder = new Reminder(Guid.NewGuid().ToString("N")[..8], due, text.Trim(), now);
        lock (_gate)
        {
            _items.Add(reminder);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return reminder;
    }

    /// <summary>Zamanı gelenleri listeden çıkarıp döndürür (her biri bir kez söylenir).</summary>
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

            _items = _items.Except(due).ToList();
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
        reminder.Text.Length == 0
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
