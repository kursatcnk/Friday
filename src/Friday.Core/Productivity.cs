using System.Globalization;

namespace Friday.Core;

/// <summary>Bir günün verimlilik izleri: odak ve bilgisayar başı dakikaları, dikkat dağıtan sitelerde geçen süre.</summary>
public sealed record ProductiveDay(DateOnly Day, double FocusMinutes, double ActiveMinutes, double DistractedMinutes);

/// <summary>
/// "Ne zaman daha verimliyim?": son haftaların kayıtlarından kişisel bir çıkarım. Hangi gün en çok, hangisinde en az
/// odaklanıldığı (haftanın günlerine göre ortalama), dikkat dağıtan sitelerde geçen sürenin bu hafta geçen haftaya göre
/// değişimi. Yeterli veri yoksa uydurmaz, ne kadar daha gerektiğini söyler.
/// </summary>
public static class Productivity
{
    public const int MinDays = 7;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Analyze(IReadOnlyList<ProductiveDay> days, DateOnly today)
    {
        var used = days.Where(day => day.Day < today && day.ActiveMinutes >= 30).ToList();
        if (used.Count < MinDays)
        {
            return $"Verimliliğini çıkarmak için en az {MinDays} günlük kayıt gerekiyor; şu an {used.Count} gün var. Birkaç gün sonra tekrar sor.";
        }

        var sentences = new List<string>();
        var byWeekday = used.GroupBy(day => day.Day.DayOfWeek)
            .Select(group => (Day: group.Key, Focus: group.Average(day => day.FocusMinutes), Count: group.Count()))
            .ToList();
        if (byWeekday.Count >= 3 && byWeekday.Max(item => item.Focus) >= 15)
        {
            var best = byWeekday.MaxBy(item => item.Focus);
            var worst = byWeekday.MinBy(item => item.Focus);
            sentences.Add($"Son {used.Count} günde en çok {Name(best.Day)} günleri odaklanıyorsun (ortalama {TurkishGrammar.Duration(best.Focus)}), "
                + $"en az {Name(worst.Day)} ({(worst.Focus < 1 ? "neredeyse hiç" : TurkishGrammar.Duration(worst.Focus))}).");
        }
        else
        {
            var average = used.Average(day => day.ActiveMinutes);
            sentences.Add($"Son {used.Count} günde günde ortalama {TurkishGrammar.Duration(average)} bilgisayar başındasın; odak oturumu neredeyse hiç başlatmamışsın.");
        }

        var thisWeek = used.Where(day => today.DayNumber - day.Day.DayNumber <= 7).ToList();
        var lastWeek = used.Where(day => today.DayNumber - day.Day.DayNumber is > 7 and <= 14).ToList();
        if (thisWeek.Count >= 3 && lastWeek.Count >= 3)
        {
            var now = thisWeek.Average(day => day.DistractedMinutes);
            var before = lastWeek.Average(day => day.DistractedMinutes);
            if (before >= 10 && Math.Abs(now - before) / before >= 0.2)
            {
                var change = (int)Math.Round(Math.Abs(now - before) / before * 100);
                sentences.Add(now < before
                    ? $"Dikkat dağıtan sitelerde geçen süren bu hafta geçen haftaya göre yüzde {change} azaldı; günde ortalama {TurkishGrammar.Duration(now)}."
                    : $"Dikkat dağıtan sitelerde geçen süren bu hafta geçen haftaya göre yüzde {change} arttı; günde ortalama {TurkishGrammar.Duration(now)}.");
            }

            var focusNow = thisWeek.Average(day => day.FocusMinutes);
            var focusBefore = lastWeek.Average(day => day.FocusMinutes);
            if (focusBefore >= 10 && Math.Abs(focusNow - focusBefore) / focusBefore >= 0.2)
            {
                sentences.Add(focusNow > focusBefore
                    ? $"Odak süren de arttı: günde {TurkishGrammar.Duration(focusBefore)} yerine {TurkishGrammar.Duration(focusNow)}."
                    : $"Odak süren düştü: günde {TurkishGrammar.Duration(focusBefore)} yerine {TurkishGrammar.Duration(focusNow)}.");
            }
        }

        return string.Join(' ', sentences);
    }

    private static string Name(DayOfWeek day) => CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetDayName(day).ToLower(Turkish);
}
