using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

/// <summary>Kullanıcının takip ettiği günlük bir alışkanlık ve yapıldığı günler.</summary>
public sealed class DailyHabit
{
    public string Name { get; set; } = "";

    public DateOnly Created { get; set; }

    public List<DateOnly> Done { get; set; } = [];

    public bool IsDoneOn(DateOnly day) => Done.Contains(day);
}

/// <summary>
/// Alışkanlık takipçisi: "alışkanlık ekle: kitap okumak", "bugün spor yaptım", "alışkanlıklarım nasıl gidiyor".
/// Her alışkanlık için yapılan günler tutulur; seri (üst üste gün), son 7 gün ve son 30 günün oranı hesaplanır.
/// D:\Friday\data\habit-book.json'da durur, bilgisayardan çıkmaz.
/// </summary>
public sealed class HabitBook
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Eşleştirmede sayılmayan dolgu sözcükleri.</summary>
    private static readonly HashSet<string> Filler = ["bugun", "her", "gun", "gunluk", "biraz", "bir", "az", "once", "sabah", "aksam", "dakika", "saat", "yapmak", "etmek", "icin"];

    private readonly string _file;
    private readonly object _gate = new();
    private readonly List<DailyHabit> _habits;

    public HabitBook(string file)
    {
        _file = file;
        try
        {
            _habits = File.Exists(file) ? JsonSerializer.Deserialize<List<DailyHabit>>(File.ReadAllText(file), Options) ?? [] : [];
        }
        catch (JsonException)
        {
            _habits = [];
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<DailyHabit> Habits
    {
        get
        {
            lock (_gate)
            {
                return [.. _habits];
            }
        }
    }

    /// <summary>Yeni alışkanlık; aynı adda varsa var olan döner.</summary>
    public DailyHabit Add(string name, DateOnly today)
    {
        var clean = TurkishGrammar.Capitalize(name.Trim().TrimEnd('.', '!'));
        lock (_gate)
        {
            if (_habits.FirstOrDefault(habit => Key(habit.Name) == Key(clean)) is { } existing)
            {
                return existing;
            }

            var habit = new DailyHabit { Name = clean, Created = today };
            _habits.Add(habit);
            Save();
            return habit;
        }
    }

    public bool Remove(string name)
    {
        lock (_gate)
        {
            if (FindLocked(name) is not { } habit)
            {
                return false;
            }

            _habits.Remove(habit);
            Save();
            return true;
        }
    }

    /// <summary>Söylenene en çok benzeyen alışkanlık ("spora gittim" → "Spor"); yoksa null.</summary>
    public DailyHabit? Find(string spoken)
    {
        lock (_gate)
        {
            return FindLocked(spoken);
        }
    }

    /// <summary>Günü işaretler ya da işareti kaldırır; değişiklik olduysa true.</summary>
    public bool Mark(string name, DateOnly day, bool done = true)
    {
        lock (_gate)
        {
            if (FindLocked(name) is not { } habit || habit.IsDoneOn(day) == done)
            {
                return false;
            }

            if (done)
            {
                habit.Done.Add(day);
                habit.Done.Sort();
                // Bir yıldan eski günler tutulmaz.
                habit.Done.RemoveAll(date => day.DayNumber - date.DayNumber > 400);
            }
            else
            {
                habit.Done.Remove(day);
            }

            Save();
            return true;
        }
    }

    /// <summary>Bugüne (bugün yapılmadıysa düne) kadar üst üste yapılan gün sayısı.</summary>
    public static int Streak(DailyHabit habit, DateOnly today)
    {
        var days = habit.Done.ToHashSet();
        var day = days.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (days.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }

    /// <summary>Şimdiye kadarki en uzun seri.</summary>
    public static int Best(DailyHabit habit)
    {
        var best = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in habit.Done.Distinct().Order())
        {
            run = previous is { } last && last.AddDays(1) == day ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = day;
        }

        return best;
    }

    /// <summary>Son 7 gün, eskiden yeniye (sonuncusu bugün).</summary>
    public static IReadOnlyList<bool> Week(DailyHabit habit, DateOnly today) =>
        [.. Enumerable.Range(0, 7).Select(offset => habit.IsDoneOn(today.AddDays(offset - 6)))];

    /// <summary>Son 30 günde (alışkanlık daha yeniyse eklendiğinden beri) yapılma oranı, 0–1.</summary>
    public static double Rate(DailyHabit habit, DateOnly today)
    {
        var start = habit.Created > today.AddDays(-29) ? habit.Created : today.AddDays(-29);
        var span = today.DayNumber - start.DayNumber + 1;
        return span <= 0 ? 0 : Math.Min(1, habit.Done.Count(day => day >= start && day <= today) / (double)span);
    }

    /// <summary>Bugün henüz yapılmayanlar.</summary>
    public IReadOnlyList<DailyHabit> Pending(DateOnly today) => [.. Habits.Where(habit => !habit.IsDoneOn(today))];

    /// <summary>"Kitap okumak: 5 gündür üst üste, bu hafta 6/7."</summary>
    public static string Describe(DailyHabit habit, DateOnly today)
    {
        var streak = Streak(habit, today);
        var week = Week(habit, today).Count(done => done);
        var state = habit.IsDoneOn(today) ? "bugün yapıldı" : "bugün henüz yapılmadı";
        return streak > 1
            ? $"{habit.Name}: {streak} gündür üst üste, bu hafta {week}/7, {state}"
            : $"{habit.Name}: bu hafta {week}/7, {state}";
    }

    /// <summary>Karşılaştırma anahtarı: küçük harf, Türkçe harfler sadeleşmiş, yalnızca harf ve rakam.</summary>
    public static string Key(string text) =>
        new(text.ToLower(Turkish)
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static List<string> Words(string text) =>
        [.. text.Split([' ', ',', '\'', '’', '-'], StringSplitOptions.RemoveEmptyEntries).Select(Key).Where(word => word.Length >= 2 && !Filler.Contains(word))];

    private DailyHabit? FindLocked(string spoken)
    {
        var said = Words(spoken);
        if (said.Count == 0)
        {
            return null;
        }

        // Türkçe ekler sözcüğün sonuna gelir: "spora"/"spor", "okudum"/"okumak", "kitabı"/"kitap" ortak kökle eşleşir.
        static bool Same(string first, string second)
        {
            if (first == second)
            {
                return true;
            }

            var shorter = Math.Min(first.Length, second.Length);
            if (shorter < 3)
            {
                return false;
            }

            var common = 0;
            while (common < shorter && first[common] == second[common])
            {
                common++;
            }

            return common >= Math.Max(3, shorter - 3);
        }

        return _habits
            .Select(habit => (habit, score: Words(habit.Name).Count(word => said.Any(other => Same(word, other)))))
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .Select(item => item.habit)
            .FirstOrDefault();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_habits, Options));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
