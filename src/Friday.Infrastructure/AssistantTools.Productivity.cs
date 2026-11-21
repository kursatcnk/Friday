using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>"Ne zaman daha verimliyim?" (bkz. <see cref="Productivity"/>).</summary>
public sealed partial class AssistantTools
{
    private QuickReply ProductivityReport()
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var rhythm = Rhythm?.History(28) ?? [];
        var days = new List<ProductiveDay>();
        for (var back = 28; back >= 1; back--)
        {
            var day = today.AddDays(-back);
            var start = day.ToDateTime(TimeOnly.MinValue);
            var record = rhythm.FirstOrDefault(item => item.Date == day);
            var apps = Tracker?.Day(start) ?? [];
            var active = record?.ActiveMinutes is > 0 ? record.ActiveMinutes : apps.Sum(app => app.Seconds) / 60.0;
            var distracted = apps.Where(app => ScreenTimeLog.IsDistraction(app.App)).Sum(app => app.Seconds) / 60.0;
            days.Add(new ProductiveDay(day, record?.FocusMinutes ?? 0, active, distracted));
        }

        ledger.Record(DataFlow.Local, "Verimlilik", "Son 4 haftanın ritim ve ekran süresi kayıtlarından çıkarım yapıldı");
        return new QuickReply(Productivity.Analyze(days, today));
    }
}
