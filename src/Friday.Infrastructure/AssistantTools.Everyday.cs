using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Kalıcı hatırlatmalar, sesli notlar ve gün sayacı.</summary>
public sealed partial class AssistantTools
{
    private static readonly TimeSpan ReminderTick = TimeSpan.FromSeconds(10);

    private readonly object _clockGate = new();
    private Timer? _reminderClock;

    /// <summary>
    /// Hatırlatmaları izlemeye başlar. Friday kapalıyken zamanı geçmiş olanlar bir kerede, "sen yokken" diye söylenir.
    /// </summary>
    public void StartReminders()
    {
        var missed = reminders.TakeDue(DateTime.Now);
        if (missed.Count > 0)
        {
            var list = string.Join(", ", missed.Select(item =>
                $"{(item.Text.Length > 0 ? item.Text : "bir zamanlayıcı")} ({item.Due.ToString("HH:mm", CultureInfo.InvariantCulture)})"));
            TimerElapsed?.Invoke(this, missed.Count == 1
                ? $"Sen yokken bir hatırlatma vardı: {list}."
                : $"Sen yokken {missed.Count} hatırlatma vardı: {list}.");
        }

        EnsureReminderClock();
    }

    /// <summary>Saat yalnızca bekleyen bir hatırlatma varken çalışır; yoksa durur ve işlemciye hiç dokunmaz.</summary>
    private void EnsureReminderClock()
    {
        lock (_clockGate)
        {
            if (reminders.Upcoming.Any(item => item.Trigger is null) && _reminderClock is null)
            {
                _reminderClock = new Timer(_ => CheckReminders(), null, ReminderTick, ReminderTick);
            }
        }
    }

    private void CheckReminders()
    {
        foreach (var reminder in reminders.TakeDue(DateTime.Now))
        {
            RememberFired(reminder);
            TimerElapsed?.Invoke(this, ReminderStore.Announcement(reminder) + (MomentNote(reminder.Text) is { } related ? " " + related : ""));
        }

        lock (_clockGate)
        {
            if (reminders.Upcoming.Count == 0)
            {
                _reminderClock?.Dispose();
                _reminderClock = null;
            }
        }
    }

    private string ReminderCommand(string action, string when, string text, string repeat = "")
    {
        var now = DateTime.Now;
        switch (Simplify(action))
        {
            case "listele":
                var upcoming = reminders.Upcoming;
                ShowListCard("Hatırlatmalar", "\uE823", upcoming.Select(item =>
                    new CardRow(item.Repeat is { } repeating ? RepeatRule.Describe(item.Due, repeating) : TurkishTime.Describe(item.Due, now),
                        item.Text.Length > 0 ? TurkishGrammar.Capitalize(item.Text) : "Zamanlayıcı")));
                return upcoming.Count == 0
                    ? "Kurulu bir hatırlatma yok."
                    : $"{upcoming.Count} hatırlatma var: " + string.Join("; ", upcoming.Take(6).Select(item =>
                        $"{(item.Repeat is { } rule ? RepeatRule.Describe(item.Due, rule) : TurkishTime.Describe(item.Due, now))} {(item.Text.Length > 0 ? TurkishGrammar.ToYou(item.Text) : "zamanlayıcı")}")) + ".";
            case "sil":
                var removed = reminders.Cancel(text);
                return removed == 0 ? $"'{text}' diye bir hatırlatma bulamadım." : "Tamam, hatırlatmayı sildim.";
            case "tasi" or "ertele" or "degistir":
                // "dişçi hatırlatmasını yarın 3'e al": söylenmeyen gün ya da saat eskisinden alınır (bkz. TurkishTime.Shift).
                if (reminders.Move(text, item => TurkishTime.Shift(item.Due, when, now) is { } moved && moved > now ? moved : null) is not { } move)
                {
                    return text.Trim().Length == 0
                        ? "Taşınacak bir hatırlatma ya da yeni zamanı bulamadım."
                        : $"'{text}' diye bir hatırlatma bulamadım ya da yeni zamanı anlayamadım.";
                }

                EnsureReminderClock();
                ledger.Record(DataFlow.Local, "Hatırlatma", $"{TurkishTime.Describe(move.New.Due, now)} zamanına taşındı");
                var movedSubject = TurkishGrammar.ToYou(move.New.Text.Trim());
                return $"Tamam, {TurkishTime.Describe(move.New.Due, now)} {(movedSubject.Length == 0 ? "haber veririm" : $"{movedSubject} hatırlatırım")} (önce {TurkishTime.Describe(move.Old.Due, now)} idi).";
            default:
                if (TurkishTime.Parse(when, now) is not { } due || due <= now)
                {
                    return "Ne zaman hatırlatacağımı anlayamadım; ör. 'yarın 9'da' ya da 'akşam 8'de' de.";
                }

                var rule = RepeatRule.IsKnown(repeat) ? repeat : null;
                var added = reminders.Add(rule is null || RepeatRule.Interval(rule) is not null ? due : RepeatRule.Start(due, when, rule, now), text, now, rule);
                EnsureReminderClock();
                ledger.Record(DataFlow.Local, "Hatırlatma", rule is null ? $"{TurkishTime.Describe(due, now)} için kuruldu" : $"{RepeatRule.Describe(added.Due, rule)} için kuruldu");
                // Kullanıcının "aramamı" dediği şey Friday'in ağzından "aramanı" olur.
                var subject = TurkishGrammar.ToYou(text.Trim());
                if (rule is not null)
                {
                    return $"Tamam, {RepeatRule.Describe(added.Due, rule)} {(subject.Length == 0 ? "haber veririm" : $"{subject} hatırlatırım")}.";
                }

                // Aynı saate yakın başka bir hatırlatma varsa kurarken söylenir.
                var clash = ScheduleConflicts.ForReminder(added.Due, added.Id, reminders.Upcoming) is { } conflict ? " " + conflict : "";
                return (subject.Length == 0
                    ? $"Tamam, {TurkishTime.Describe(due, now)} haber veririm."
                    : $"Tamam, {TurkishTime.Describe(due, now)} {subject} hatırlatırım.") + clash;
        }
    }

    private string NoteCommand(string action, string text)
    {
        if (Simplify(action) == "ekle")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "Neyi not alayım?";
            }

            notes.Add(text, DateTime.Now);
            ledger.Record(DataFlow.Local, "Not", "Bir not eklendi");
            return "Not aldım.";
        }

        var all = notes.All;
        if (all.Count == 0)
        {
            return "Henüz bir notun yok.";
        }

        var recent = all.TakeLast(5).Reverse().ToList();
        ShowListCard("Notlar", "\uE70B", recent.Select(note =>
            new CardRow(note.Text, note.Time.Date == DateTime.Today ? note.Time.ToString("HH:mm", Turkish) : note.Time.ToString("d MMM", Turkish))));
        return recent.Count == 1
            ? $"Son notun: {recent[0].Text}."
            : $"Son {recent.Count} notun: " + string.Join("; ", recent.Select(note => note.Text)) + ".";
    }

    private string CountdownAnswer(string target) =>
        DayCountdown.Answer(target, memory.Items, DateOnly.FromDateTime(DateTime.Now))
        ?? $"'{target}' için bir tarih bilmiyorum. Hafızama tarihini söylersen sayarım.";

    public void Dispose()
    {
        lock (_clockGate)
        {
            _reminderClock?.Dispose();
            _reminderClock = null;
        }

        _watches?.Dispose();
        _ambient?.Dispose();
        _clipboard?.Dispose();
        _calendar?.Dispose();
        _background.Cancel();
        _background.Dispose();
    }
}

/// <summary>Windows'un ana ses seviyesi (yüzde): "sesi yüzde 30 yap", "ses seviyesi kaç".</summary>
internal static class SystemVolume
{
    /// <param name="percent">0–100 ayarlar; negatifse yalnızca okunur.</param>
    public static string Apply(int percent)
    {
        using var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        using var speaker = devices.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
        var volume = speaker.AudioEndpointVolume;
        if (percent < 0)
        {
            var current = (int)Math.Round(volume.MasterVolumeLevelScalar * 100);
            return volume.Mute ? $"Ses kapalı (seviye yüzde {current})." : $"Ses yüzde {current}.";
        }

        volume.Mute = false;
        volume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f;
        return $"Ses yüzde {Math.Clamp(percent, 0, 100)} oldu.";
    }
}
