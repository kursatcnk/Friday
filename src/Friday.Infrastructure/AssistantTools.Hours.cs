using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Kullanıcının öğrenilen günlük ritmi (bkz. <see cref="ActiveHours"/>): akşam hatırlatmaları ve gün planı buna göre.</summary>
public sealed partial class AssistantTools
{
    /// <summary>Etkinlik izleyicisi; ekran süresi kaydı kapalıyken de verilir (önceden öğrenilen saatler için).</summary>
    public ActivityTracker? Tracker { get; set; }

    /// <summary>Kullanıcının günü genelde bitirdiği saat; öğrenilmediyse 23.00.</summary>
    public TimeSpan DayEnd(DateTime now) => Tracker?.DayEnd(now) ?? ActiveHours.DefaultEnd;

    private QuickReply ActiveHoursAnswer()
    {
        var now = DateTime.Now;
        if (Tracker?.TypicalDay(now) is not { } day)
        {
            return new QuickReply(Settings.ScreenTime
                ? "Günlük ritmini öğrenmek için en az beş günlük kayıt gerekiyor. O zamana kadar akşam hatırlatmalarını 21.30 ile 23.00 arasında yapıyorum."
                : "Ekran süresi kaydı kapalı olduğu için hangi saatlerde bilgisayar başında olduğunu öğrenemiyorum; akşam hatırlatmalarını 21.30 ile 23.00 arasında yapıyorum.");
        }

        var end = DayEnd(now);
        var kind = ActiveHours.DayOf(now).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "Hafta sonları" : "Hafta içi";
        return new QuickReply($"{kind} günün genelde {Clock(day.Start)} civarı başlıyor, {Clock(day.End)} civarı bitiyor. "
            + $"Gün sonu sorusunu {Clock(end - TimeSpan.FromMinutes(90))} ile {Clock(end)} arasında, alışkanlık hatırlatmasını {Clock(end - TimeSpan.FromHours(3))} civarında yapıyorum.");
    }

    /// <summary>Gece yarısını geçen süreler saate çevrilir: 25:30 → 01:30.</summary>
    private static string Clock(TimeSpan time) =>
        TimeSpan.FromMinutes((((time.TotalMinutes % 1440) + 1440) % 1440)).ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
}
