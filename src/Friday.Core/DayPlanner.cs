using System.Globalization;

namespace Friday.Core;

public enum PlanKind
{
    /// <summary>Takvimdeki etkinlik (taşınmaz).</summary>
    Event,

    /// <summary>Yapılacaklardan bir iş için odak bloğu.</summary>
    Focus,

    /// <summary>Odak bloklarının arasındaki kısa mola.</summary>
    Break,

    /// <summary>Bugün henüz yapılmamış bir alışkanlık.</summary>
    Habit,

    /// <summary>Öğle ya da akşam yemeği.</summary>
    Meal
}

/// <summary>Plandaki bir blok.</summary>
public sealed record PlanBlock(DateTime Start, DateTime End, string Title, PlanKind Kind);

/// <summary>Planlanacak sabit etkinlik (takvim) ya da saatli hatırlatma.</summary>
public sealed record FixedItem(DateTime Start, DateTime End, string Title);

/// <summary>
/// Gün planı: takvimdeki etkinlikler yerinde kalır; aradaki boşluklara bekleyen işler 45 dakikalık odak blokları olarak
/// (her ikisinden sonra 15, diğerlerinde 10 dakika mola), bugün yapılmamış alışkanlıklar 30 dakikalık bloklar olarak
/// yerleşir. Öğle (12.30) ve akşam (19.00) yemeğine yer bırakılır. Dil modeli kullanılmaz: anında hazırlanır, uydurmaz.
/// </summary>
public static class DayPlanner
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static IReadOnlyList<PlanBlock> Plan(DateTime now, IEnumerable<FixedItem> fixedItems, IEnumerable<string> todos, IEnumerable<string> habits,
        int dayEndHour = 22, int focusMinutes = 45, int maxFocus = 5)
    {
        var start = RoundUp(now, 5);
        var end = now.Date.AddHours(dayEndHour);
        if (end - start < TimeSpan.FromMinutes(20))
        {
            return [];
        }

        var blocks = fixedItems
            .Where(item => item.End > start && item.Start < end)
            .Select(item => new PlanBlock(item.Start < start ? start : item.Start, item.End > end ? end : item.End, item.Title, PlanKind.Event))
            .ToList();

        // Yemekler: o saatte etkinlik yoksa ve saat henüz geçmediyse.
        foreach (var (hour, minute, title) in new[] { (12, 30, "Öğle yemeği"), (19, 0, "Akşam yemeği") })
        {
            var mealStart = now.Date.AddHours(hour).AddMinutes(minute);
            var meal = new PlanBlock(mealStart, mealStart.AddHours(1), title, PlanKind.Meal);
            if (mealStart >= start && meal.End <= end && !blocks.Any(block => Overlaps(block, meal)))
            {
                blocks.Add(meal);
            }
        }

        var cursor = start;
        var focusCount = 0;
        foreach (var todo in todos.Where(text => text.Trim().Length > 0).Take(maxFocus))
        {
            if (Place(blocks, ref cursor, end, TimeSpan.FromMinutes(focusMinutes), todo.Trim(), PlanKind.Focus) is null)
            {
                break;
            }

            focusCount++;
            var rest = focusCount % 2 == 0 ? 15 : 10;
            Place(blocks, ref cursor, end, TimeSpan.FromMinutes(rest), "Mola", PlanKind.Break, mustFollow: true);
        }

        // Alışkanlıklar günün ikinci yarısına (17.00 sonrası, ya da şimdiden sonra) konur.
        var habitCursor = cursor > now.Date.AddHours(17) ? cursor : (now.Date.AddHours(17) > start ? now.Date.AddHours(17) : cursor);
        foreach (var habit in habits.Where(text => text.Trim().Length > 0))
        {
            if (Place(blocks, ref habitCursor, end, TimeSpan.FromMinutes(30), habit.Trim(), PlanKind.Habit) is null)
            {
                break;
            }
        }

        // Sonda kalan mola anlamsız; kaldırılır.
        var ordered = blocks.OrderBy(block => block.Start).ToList();
        while (ordered.Count > 0 && ordered[^1].Kind == PlanKind.Break)
        {
            ordered.RemoveAt(ordered.Count - 1);
        }

        return ordered;
    }

    /// <summary>"14:05 Faturayı öde, 14:50 mola, 15:00 Toplantı…" biçiminde kısa okunuş (ilk birkaç blok).</summary>
    public static string Describe(IReadOnlyList<PlanBlock> blocks, int take = 5)
    {
        var important = blocks.Where(block => block.Kind != PlanKind.Break).Take(take)
            .Select(block => $"{block.Start.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.TimeSuffix(block.Start)} {Spoken(block)}")
            .ToList();
        return TurkishGrammar.Join(important);
    }

    private static string Spoken(PlanBlock block) => block.Kind switch
    {
        PlanKind.Focus => $"{block.Title.ToLower(Turkish)} için odak",
        PlanKind.Habit => block.Title.ToLower(Turkish),
        _ => block.Title.ToLower(Turkish)
    };

    private static PlanBlock? Place(List<PlanBlock> blocks, ref DateTime cursor, DateTime end, TimeSpan length, string title, PlanKind kind, bool mustFollow = false)
    {
        var candidate = cursor;
        while (candidate + length <= end)
        {
            var block = new PlanBlock(candidate, candidate + length, title, kind);
            var clash = blocks.Where(other => Overlaps(other, block)).OrderBy(other => other.End).LastOrDefault();
            if (clash is null)
            {
                blocks.Add(block);
                cursor = block.End;
                return block;
            }

            if (mustFollow)
            {
                // Mola bir etkinliğe denk geliyorsa gerek yok; etkinlik zaten ara verdirir.
                return null;
            }

            candidate = clash.End;
        }

        return null;
    }

    private static bool Overlaps(PlanBlock first, PlanBlock second) => first.Start < second.End && second.Start < first.End;

    private static DateTime RoundUp(DateTime time, int minutes)
    {
        var step = TimeSpan.FromMinutes(minutes).Ticks;
        var ticks = (time.Ticks + step - 1) / step * step;
        return new DateTime(ticks, time.Kind);
    }
}
