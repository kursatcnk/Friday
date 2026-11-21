using System.Globalization;
using System.Text.Json;

namespace Friday.Core;

/// <summary>
/// Kullanıcının bilgisayar başında olduğu saatler: gün gün, yarım saatlik dilimlerde etkin saniye. Gün 05:00'te başlar;
/// gece 01:30'daki çalışma önceki güne sayılır. Yalnızca sayılar tutulur (uygulama ya da başlık yok), son 28 gün kalır.
/// Buradan kullanıcının günü genelde ne zaman bitirdiği öğrenilir: akşam hatırlatmaları sabit saatler yerine buna göre
/// zamanlanır (erken bırakan biri gün sonu sorusunu kaçırmaz, gece çalışan biri işinin ortasında duymaz).
/// </summary>
public sealed class ActiveHours
{
    public const int SlotMinutes = 30;
    public const int Slots = 24 * 60 / SlotMinutes;
    public const int DayStartHour = 5;

    /// <summary>Öğrenilmiş bir şey yokken günün sonu: 23.00 (akşam pencereleri eski sabit saatlerle aynı kalır).</summary>
    public static readonly TimeSpan DefaultEnd = TimeSpan.FromHours(23);

    /// <summary>Öğrenilen gün sonu bu aralığa sıkıştırılır: 18.00 ile gece 02.00.</summary>
    public static readonly TimeSpan EarliestEnd = TimeSpan.FromHours(18);

    public static readonly TimeSpan LatestEnd = TimeSpan.FromHours(26);

    private const int KeepDays = 28;
    private const int MinDays = 5;
    private const int MinSameKind = 3;
    private const int SlotThreshold = 5 * 60;
    private const int DayThreshold = 60 * 60;

    private readonly Dictionary<string, int[]> _days;

    public ActiveHours()
        : this(new Dictionary<string, int[]>(StringComparer.Ordinal))
    {
    }

    private ActiveHours(Dictionary<string, int[]> days) => _days = days;

    /// <summary>Anın ait olduğu gün (05:00'te başlar).</summary>
    public static DateOnly DayOf(DateTime at) => DateOnly.FromDateTime(at.AddHours(-DayStartHour));

    /// <summary>Anın günün içindeki dilimi (0 = 05.00–05.30).</summary>
    public static int SlotOf(DateTime at) => (int)(at.AddHours(-DayStartHour).TimeOfDay.TotalMinutes / SlotMinutes);

    public void Add(DateTime at, int seconds)
    {
        var key = DayOf(at).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!_days.TryGetValue(key, out var slots))
        {
            slots = new int[Slots];
            _days[key] = slots;
            Trim(DayOf(at));
        }

        var slot = SlotOf(at);
        slots[slot] = Math.Min(SlotMinutes * 60, slots[slot] + seconds);
    }

    /// <summary>
    /// Günün genelde bittiği saat (gece yarısını geçebilir: 25:00 = 01.00), son günlerin ortancası. Hafta içi ve hafta
    /// sonu ayrı bakılır; yeterli gün yoksa null.
    /// </summary>
    public TimeSpan? TypicalEnd(DateTime now) => Typical(now, last: true);

    /// <summary>Günün genelde başladığı saat; yeterli gün yoksa null.</summary>
    public TimeSpan? TypicalStart(DateTime now) => Typical(now, last: false);

    /// <summary>O gün bilgisayar başında olunan ilk ve son an (05.00'te başlayan gün; 25:30 = gece 01.30); kayıt yoksa null.</summary>
    public (TimeSpan Start, TimeSpan End)? Span(DateOnly day)
    {
        if (!_days.TryGetValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var slots))
        {
            return null;
        }

        var first = Array.FindIndex(slots, seconds => seconds >= 60);
        var last = Array.FindLastIndex(slots, seconds => seconds >= 60);
        return first < 0
            ? null
            : (TimeSpan.FromHours(DayStartHour) + TimeSpan.FromMinutes(first * SlotMinutes), TimeSpan.FromHours(DayStartHour) + TimeSpan.FromMinutes((last + 1) * SlotMinutes));
    }

    /// <summary>Akşam hatırlatmalarının dayandığı gün sonu: öğrenilen (18.00–02.00 arasına sıkıştırılmış) ya da 23.00.</summary>
    public TimeSpan DayEnd(DateTime now)
    {
        var end = TypicalEnd(now) ?? DefaultEnd;
        return end < EarliestEnd ? EarliestEnd : end > LatestEnd ? LatestEnd : end;
    }

    /// <summary>
    /// Şimdi, gün sonundan <paramref name="fromBefore"/> öncesi ile <paramref name="toBefore"/> öncesi arasında mı?
    /// Akşam pencereleri 16.00'dan erken başlamaz; gece yarısını geçen günler doğru hesaplanır.
    /// </summary>
    public static bool InEvening(DateTime now, TimeSpan dayEnd, TimeSpan fromBefore, TimeSpan toBefore)
    {
        var minute = Minutes(now.TimeOfDay);
        var end = dayEnd.TotalMinutes - (DayStartHour * 60);
        var from = Math.Max(end - fromBefore.TotalMinutes, (16 - DayStartHour) * 60);
        var to = end - toBefore.TotalMinutes;
        return minute >= from && minute < to;
    }

    public string ToJson() => JsonSerializer.Serialize(_days);

    public static ActiveHours FromJson(string json)
    {
        try
        {
            var days = JsonSerializer.Deserialize<Dictionary<string, int[]>>(json) ?? [];
            return new ActiveHours(new Dictionary<string, int[]>(days.Where(pair => pair.Value.Length == Slots), StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return new ActiveHours();
        }
    }

    /// <summary>Saatin günün başından (05.00) bu yana kaç dakika olduğu.</summary>
    private static double Minutes(TimeSpan time) => ((time.TotalMinutes - (DayStartHour * 60)) % (24 * 60) + (24 * 60)) % (24 * 60);

    private static bool Weekend(DateOnly day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private TimeSpan? Typical(DateTime now, bool last)
    {
        var today = DayOf(now);
        var days = _days
            .Select(pair => (Day: DateOnly.ParseExact(pair.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture), Slots: pair.Value))
            .Where(item => item.Day < today && item.Slots.Sum() >= DayThreshold)
            .ToList();
        if (days.Count < MinDays)
        {
            return null;
        }

        var same = days.Where(item => Weekend(item.Day) == Weekend(today)).ToList();
        var pool = same.Count >= MinSameKind ? same : days;
        var marks = pool
            .Select(item => last ? Array.FindLastIndex(item.Slots, seconds => seconds >= SlotThreshold) + 1 : Array.FindIndex(item.Slots, seconds => seconds >= SlotThreshold))
            .Where(slot => slot >= 0 && (last ? slot > 0 : slot < Slots))
            .Order()
            .ToList();
        if (marks.Count == 0)
        {
            return null;
        }

        var median = marks.Count % 2 == 1 ? marks[marks.Count / 2] : (marks[(marks.Count / 2) - 1] + marks[marks.Count / 2]) / 2.0;
        return TimeSpan.FromHours(DayStartHour) + TimeSpan.FromMinutes(Math.Round(median) * SlotMinutes);
    }

    private void Trim(DateOnly today)
    {
        var oldest = today.AddDays(-KeepDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var key in _days.Keys.Where(key => string.CompareOrdinal(key, oldest) < 0).ToList())
        {
            _days.Remove(key);
        }
    }
}
