using System.Globalization;

namespace Friday.Core;

/// <summary>
/// Yeni bir şey takvime girerken çakışmayı fark etmek: aynı saate yakın başka bir hatırlatma varsa ya da son tarih
/// zaten yoğun bir güne düşüyorsa kurarken bir cümleyle söylenir. Kullanıcı sonradan iki şeyin aynı anda çaldığını
/// görüp şaşırmasın.
/// </summary>
public static class ScheduleConflicts
{
    /// <summary>Bu kadar yakın hatırlatmalar çakışma sayılır.</summary>
    public static readonly TimeSpan Near = TimeSpan.FromMinutes(20);

    /// <summary>Son tarihi aynı güne düşen bu kadar başka iş varsa gün yoğun sayılır.</summary>
    public const int BusyDay = 2;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Yeni hatırlatmaya yakın başka bir hatırlatma varsa söylenecek cümle; yoksa null.</summary>
    public static string? ForReminder(DateTime due, string id, IEnumerable<Reminder> upcoming)
    {
        var near = upcoming
            .Where(other => other.Id != id && other.Trigger is null && (other.Due - due).Duration() <= Near)
            .OrderBy(other => (other.Due - due).Duration())
            .FirstOrDefault();
        if (near is null)
        {
            return null;
        }

        var what = near.Text.Trim().Length > 0 ? $"\"{near.Text.Trim()}\" hatırlatman" : "bir zamanlayıcın";
        return near.Due == due
            ? $"Aynı saatte {what} da var."
            : $"Bu arada {near.Due.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.TimeSuffix(near.Due)} {what} da var.";
    }

    /// <summary>Son tarih yoğun bir güne düşüyorsa söylenecek cümle; yoksa null.</summary>
    public static string? ForDeadline(TodoItem added, IEnumerable<TodoItem> open)
    {
        if (added.Due is not { } due)
        {
            return null;
        }

        var same = open.Where(item => item.Id != added.Id && !item.IsDone && item.Due == due).ToList();
        if (same.Count < BusyDay)
        {
            return null;
        }

        var names = string.Join(", ", same.Take(3).Select(item => item.Text.ToLower(Turkish)));
        return $"O gün {same.Count} işin daha teslim ({names}{(same.Count > 3 ? "…" : "")}); istersen birini öne alalım.";
    }
}
