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

    /// <summary>Haftalık hedef ("haftada 3 kez"); 0 ise her gün.</summary>
    public int PerWeek { get; set; }

    public bool IsWeekly => PerWeek > 0;

    public bool IsDoneOn(DateOnly day) => Done.Contains(day);
}

/// <summary>
/// Alışkanlık takipçisi: "alışkanlık ekle: kitap okumak", "bugün spor yaptım", "alışkanlıklarım nasıl gidiyor".
/// Her alışkanlık için yapılan günler tutulur; seri (üst üste gün), son 7 gün ve son 30 günün oranı hesaplanır.
/// D:\Friday\data\habit-book.json'da durur, bilgisayardan çıkmaz.
/// </summary>
public sealed class HabitBook : IRestorableStore
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

    /// <inheritdoc/>
    public string Title => "alışkanlıklar";

    /// <inheritdoc/>
    public void Restore(string file, string? content)
    {
        lock (_gate)
        {
            _habits.Clear();
            _habits.AddRange(content is null ? [] : JsonSerializer.Deserialize<List<DailyHabit>>(content, Options) ?? []);
            UndoLog.WriteBack(file, content);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

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

    /// <summary>Yeni alışkanlık; aynı adda varsa var olan döner (haftalık hedef söylendiyse güncellenir).</summary>
    public DailyHabit Add(string name, DateOnly today, int perWeek = 0)
    {
        var clean = TurkishGrammar.Capitalize(name.Trim().TrimEnd('.', '!'));
        lock (_gate)
        {
            if (_habits.FirstOrDefault(habit => Key(habit.Name) == Key(clean)) is { } existing)
            {
                if (perWeek > 0 && existing.PerWeek != perWeek)
                {
                    existing.PerWeek = perWeek;
                    Save();
                }

                return existing;
            }

            var habit = new DailyHabit { Name = clean, Created = today, PerWeek = Math.Clamp(perWeek, 0, 7) };
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

    /// <summary>
    /// Günlük alışkanlıkta bugüne (bugün yapılmadıysa düne) kadar üst üste yapılan gün sayısı; haftalıkta hedefin
    /// tutturulduğu ardışık hafta sayısı (bu hafta henüz tutmadıysa geçen haftadan geriye).
    /// </summary>
    public static int Streak(DailyHabit habit, DateOnly today)
    {
        if (habit.IsWeekly)
        {
            var monday = WeekStart(today);
            var weeks = WeekCount(habit, today) >= habit.PerWeek ? 1 : 0;
            for (var start = monday.AddDays(-7); start >= habit.Created.AddDays(-6); start = start.AddDays(-7))
            {
                if (habit.Done.Count(day => day >= start && day < start.AddDays(7)) < habit.PerWeek)
                {
                    break;
                }

                weeks++;
            }

            return weeks;
        }

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

    /// <summary>Bugün henüz yapılmayanlar; haftalık alışkanlık bu haftanın hedefi tuttuysa sayılmaz.</summary>
    public IReadOnlyList<DailyHabit> Pending(DateOnly today) =>
        [.. Habits.Where(habit => !habit.IsDoneOn(today) && (!habit.IsWeekly || WeekCount(habit, today) < habit.PerWeek))];

    /// <summary>Bu hafta (pazartesiden bugüne) yapıldığı gün sayısı.</summary>
    public static int WeekCount(DailyHabit habit, DateOnly today)
    {
        var monday = WeekStart(today);
        return habit.Done.Count(day => day >= monday && day <= today);
    }

    /// <summary>Haftalık hedefe yetişmek için bu hafta artık her gün yapılması gerekiyor mu?</summary>
    public static bool WeekAtRisk(DailyHabit habit, DateOnly today) =>
        habit.IsWeekly && habit.PerWeek - WeekCount(habit, today) >= 7 - (today.DayNumber - WeekStart(today).DayNumber);

    private static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>"spor, haftada 3" / "haftada üç kez yüzmek" → ("spor", 3); hedef söylenmediyse 0.</summary>
    public static (string Name, int PerWeek) ParseTarget(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[,\s]*\bhaftada\s+(?<n>\d|bir|iki|üç|dört|beş|altı|yedi)\s*(?:kez|kere|defa|gün)?\b[,\s]*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return (text.Trim(), 0);
        }

        string[] words = ["sıfır", "bir", "iki", "üç", "dört", "beş", "altı", "yedi"];
        var value = match.Groups["n"].Value.ToLower(Turkish);
        var count = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : Array.IndexOf(words, value);
        var name = (text[..match.Index] + " " + text[(match.Index + match.Length)..]).Trim().Trim(',').Trim();
        return (name, Math.Clamp(count, 0, 7));
    }

    /// <summary>"Kitap okumak: 5 gündür üst üste, bu hafta 6/7."</summary>
    public static string Describe(DailyHabit habit, DateOnly today)
    {
        if (habit.IsWeekly)
        {
            var weeks = Streak(habit, today);
            return $"{habit.Name}: bu hafta {WeekCount(habit, today)}/{habit.PerWeek}" + (weeks > 1 ? $", {weeks} haftadır hedefte" : "");
        }

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

        // Çok sözcüklü alışkanlıkta yalnızca fiilin tutması yetmez: "Kitap okumak" varken "haberleri okudum" kitabı
        // işaretlememeli. Türkçede fiil sondadır; ondan önceki sözcüklerden (işin adı) biri de geçmeli.
        static int Score(List<string> name, List<string> said)
        {
            var matched = name.Where(word => said.Any(other => Same(word, other))).ToList();
            return name.Count > 1 && matched.Count > 0 && matched.All(word => word == name[^1]) ? 0 : matched.Count;
        }

        return _habits
            .Select(habit => (habit, score: Score(Words(habit.Name), said)))
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .Select(item => item.habit)
            .FirstOrDefault();
    }

    private void Save()
    {
        UndoLog.Touching(this, _file);
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_habits, Options));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
