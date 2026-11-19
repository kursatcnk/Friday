namespace Friday.Core;

/// <summary>Günlük odak hedefi: ilerleme, seri (üst üste hedefin tutturulduğu günler) ve söylenecek cümle.</summary>
public static class RhythmGoal
{
    /// <summary>
    /// Seri: dünden geriye doğru hedefin tutturulduğu ardışık günler, bugün de tutturulduysa +1. Bugün henüz bitmediği
    /// için tutturulmamış olması seriyi bozmaz. Geçmiş eskiden yeniye, son eleman bugün.
    /// </summary>
    public static int Streak(IReadOnlyList<RhythmDay> history, double goalMinutes)
    {
        if (history.Count == 0 || goalMinutes <= 0)
        {
            return 0;
        }

        var streak = history[^1].FocusMinutes >= goalMinutes ? 1 : 0;
        for (var index = history.Count - 2; index >= 0 && history[index].FocusMinutes >= goalMinutes; index--)
        {
            streak++;
        }

        return streak;
    }

    /// <summary>"Bugün 45 dakika odaklandın; 2 saatlik hedefe 1 saat 15 dakika kaldı. Seri: 3 gün."</summary>
    public static string Describe(double focusMinutes, double goalMinutes, int streak)
    {
        var goal = TurkishGrammar.Duration(goalMinutes);
        var done = TurkishGrammar.Duration(Math.Max(1, Math.Round(focusMinutes)));
        var text = focusMinutes >= goalMinutes
            ? $"Bugünkü {goal}lik odak hedefini tamamladın ({done})."
            : focusMinutes < 1
                ? $"Bugün henüz odaklanmadın; günlük hedefin {goal}."
                : $"Bugün {done} odaklandın; {goal}lik hedefe {TurkishGrammar.Duration(Math.Round(goalMinutes - focusMinutes))} kaldı.";
        return streak > 1 ? $"{text} {streak} gündür üst üste hedefini tutturuyorsun." : text;
    }
}
