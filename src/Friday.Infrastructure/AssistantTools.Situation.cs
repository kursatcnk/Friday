using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Dil modeline her soruda verilen kısa durum satırı.</summary>
public sealed partial class AssistantTools
{
    /// <summary>Görüşmedeki uygulama (FridayAssistant verir); yoksa null.</summary>
    public Func<string?>? MeetingApp { get; set; }

    /// <summary>
    /// "[Durum: saat 15:42 perşembe · 3 açık iş (bugün son gün: raporu bitir) · sıradaki hatırlatma 17:00 doktor · odakta, 16:10'a
    /// kadar]". Model saat için araç çağırmadan, "akşama kadar ne yetiştiririm", "bugün yoğun muyum" gibi soruları gerçek
    /// duruma göre cevaplar. Yalnızca o soruya eklenir, geçmişe yazılmaz.
    /// </summary>
    public string Situation(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var parts = new List<string> { $"saat {now.ToString("HH:mm", CultureInfo.InvariantCulture)} {now.ToString("dddd", Turkish)}" };
        if (Todos?.Open is { Count: > 0 } open)
        {
            var late = open.Where(item => item.Due < today).Select(item => item.Text).Take(2).ToList();
            var due = open.Where(item => item.Due == today).Select(item => item.Text).Take(2).ToList();
            var notes = new List<string>();
            if (late.Count > 0)
            {
                notes.Add($"gecikmiş: {string.Join(", ", late)}");
            }

            if (due.Count > 0)
            {
                notes.Add($"bugün son gün: {string.Join(", ", due)}");
            }

            parts.Add($"{open.Count} açık iş" + (notes.Count > 0 ? $" ({string.Join("; ", notes)})" : ""));
        }

        if (reminders.Upcoming.Where(item => item.Trigger is null && item.Due > now && item.Due - now < TimeSpan.FromHours(24)).OrderBy(item => item.Due).FirstOrDefault() is { } next)
        {
            parts.Add($"sıradaki hatırlatma {next.Due.ToString(next.Due.Date == now.Date ? "HH:mm" : "ddd HH:mm", Turkish)} {(next.Text.Length > 0 ? next.Text : "")}".TrimEnd());
        }

        if (Rhythm?.Snapshot() is { FocusEnd: { } focusEnd } && focusEnd > now)
        {
            parts.Add($"odakta, {focusEnd.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.TimeSuffix(focusEnd)} kadar");
        }

        if (MeetingApp?.Invoke() is { } call)
        {
            parts.Add($"{call} görüşmesinde");
        }

        if (HabitBook?.Pending(today) is { Count: > 0 } habits)
        {
            parts.Add($"bugün yapılmamış alışkanlık: {string.Join(", ", habits.Take(3).Select(habit => habit.Name))}");
        }

        return $"\n[Durum: {string.Join(" · ", parts)}]";
    }
}
