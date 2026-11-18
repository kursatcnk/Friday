using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

/// <summary>Bir kullanım: neyin (ör. "rutin:çalışma modu"), hangi adla, ne zaman yapıldığı.</summary>
public sealed record HabitEvent(string Key, string Label, DateTime Time);

/// <summary>Friday'in önerebileceği bir alışkanlık.</summary>
public sealed record HabitSuggestion(string Key, string Label, int Days);

/// <summary>
/// Alışkanlık öğrenme: rutinlerin ve açılan uygulamaların saatleri (son 30 gün) D:\Friday\data\habits.json'da tutulur.
/// Son iki haftada en az üç ayrı günde aynı saatlerde (±30 dk, hafta içi ile hafta sonu ayrı) yapılan ve bugün henüz
/// yapılmamış bir şey o saat gelince önerilir; aynı öneri günde bir kez yapılır. Yalnızca bilgisayarda kalır.
/// </summary>
public sealed class HabitTracker
{
    private const int MinimumDays = 3;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _file;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateOnly> _suggested = new(StringComparer.OrdinalIgnoreCase);
    private List<HabitEvent> _events;

    public HabitTracker(string file)
    {
        _file = file;
        try
        {
            _events = File.Exists(file) ? JsonSerializer.Deserialize<List<HabitEvent>>(File.ReadAllText(file), Options) ?? [] : [];
        }
        catch (JsonException)
        {
            _events = [];
        }
    }

    public void Record(string key, string label, DateTime time)
    {
        lock (_gate)
        {
            _events.Add(new HabitEvent(key, label, time));
            _events.RemoveAll(item => time - item.Time > TimeSpan.FromDays(30));
            Save();
        }
    }

    /// <summary>Şu an önerilecek alışkanlık; yoksa null. Önerilen, bugün bir daha önerilmez.</summary>
    public HabitSuggestion? Suggest(DateTime now)
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(now);
            var weekend = IsWeekend(now);
            var candidate = _events
                .Where(item => now - item.Time <= TimeSpan.FromDays(14))
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Where(group => !group.Any(item => DateOnly.FromDateTime(item.Time) == today))
                .Where(group => !_suggested.TryGetValue(group.Key, out var day) || day != today)
                .Select(group => new HabitSuggestion(group.Key, group.Last().Label, group
                    .Where(item => IsWeekend(item.Time) == weekend && Near(item.Time.TimeOfDay, now.TimeOfDay))
                    .Select(item => DateOnly.FromDateTime(item.Time))
                    .Distinct()
                    .Count()))
                .Where(suggestion => suggestion.Days >= MinimumDays)
                .OrderByDescending(suggestion => suggestion.Days)
                .FirstOrDefault();
            if (candidate is not null)
            {
                _suggested[candidate.Key] = today;
            }

            return candidate;
        }
    }

    private static bool Near(TimeSpan first, TimeSpan second) => (first - second).Duration() <= Window;

    private static bool IsWeekend(DateTime time) => time.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_events, Options));
        }
        catch (IOException)
        {
            // Alışkanlık kaydı yazılamazsa öneriler yalnızca bu oturumda çalışır.
        }
    }
}
