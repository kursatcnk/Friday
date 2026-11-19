using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed partial class AssistantTools
{
    private (string Text, DateTime At)? _lastFired;

    /// <summary>Listeler (data\\lists.json); asistan kurulurken verilir.</summary>
    public ListStore? Lists { get; set; }

    /// <summary>Saati gelen hatırlatma söylendi: "10 dakika ertele" için akılda tutulur.</summary>
    private void RememberFired(Reminder reminder)
    {
        if (reminder.Text.Length > 0 && reminder.Text != QuickIntents.AlarmText)
        {
            _lastFired = (reminder.Text, DateTime.Now);
        }
    }

    private string TriggeredReminder(string condition, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Neyi hatırlatayım?";
        }

        var trigger = Simplify(condition) is "donus" or "geridonus" or "donunce" ? "donus" : condition.Trim();
        if (trigger != "donus" && !trigger.StartsWith("uygulama:", StringComparison.Ordinal))
        {
            trigger = "uygulama:" + trigger;
        }

        var reminder = reminders.AddTriggered(trigger, text, DateTime.Now);
        ledger.Record(DataFlow.Local, "Hatırlatma", $"{reminder.TriggerText}: {text}");
        return trigger == "donus"
            ? $"Tamam, bilgisayara döndüğünde {text} hatırlatırım."
            : $"Tamam, {TurkishGrammar.Capitalize(trigger["uygulama:".Length..])} açıldığında {text} hatırlatırım.";
    }

    /// <summary>"Ertele": az önce söylenen hatırlatma varsa onu, yoksa mola önerisini erteler.</summary>
    private QuickReply Snooze(string minutes)
    {
        var given = int.TryParse(minutes, out var value) && value > 0 ? Math.Min(value, 240) : (int?)null;
        var amount = given ?? 10;
        if (_lastFired is { } last && DateTime.Now - last.At < TimeSpan.FromMinutes(15))
        {
            _lastFired = null;
            ReminderCommand("kur", $"{amount} dakika sonra", last.Text);
            return new QuickReply($"Tamam, {amount} dakika sonra tekrar hatırlatırım.");
        }

        return RhythmCommand("ertele", (given ?? 15).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Son 7 gün: ekran süresi ve en çok kullanılanlar, işler, sohbetler, yaklaşan hatırlatmalar.</summary>
    private string WeeklyReview()
    {
        var now = DateTime.Now;
        var parts = new List<string>();
        if (Activity is { } activity)
        {
            var days = Enumerable.Range(0, 7).Select(offset => activity.Day(now.AddDays(-offset))).ToList();
            var total = days.Sum(day => day.Sum(item => item.Seconds));
            if (total > 0)
            {
                var active = Math.Max(1, days.Count(day => day.Count > 0));
                var top = days.SelectMany(day => day).GroupBy(item => item.App)
                    .Select(group => (App: group.Key, Seconds: group.Sum(item => item.Seconds)))
                    .OrderByDescending(item => item.Seconds).Take(3);
                parts.Add($"Son 7 günde {ScreenTimeLog.Spoken(total)} bilgisayar başındaydın, kullandığın günlerde ortalama {ScreenTimeLog.Spoken(total / active)}. "
                          + $"En çok: {string.Join(", ", top.Select(item => $"{item.App} ({ScreenTimeLog.Spoken(item.Seconds)})"))}.");
            }
        }

        if (Todos is { } todos)
        {
            var done = todos.DoneSince(now.AddDays(-7));
            var open = todos.Open.Count;
            if (done + open > 0)
            {
                parts.Add($"{done} iş tamamladın{(open > 0 ? $", {open} iş bekliyor" : "")}.");
            }
        }

        if (Journal is { } journal && JournalStore.MoodAverage(journal.Between(now.Date.AddDays(-6), now)) is { } mood)
        {
            parts.Add(string.Create(CultureInfo.GetCultureInfo("tr-TR"), $"Ruh hali ortalaman 10 üzerinden {mood:0.#}."));
        }

        LifeWeek(parts, now);

        var chats = history.List().Count(chat => chat.Updated >= now.AddDays(-7));
        if (chats > 0)
        {
            parts.Add($"{chats} sohbetimiz oldu.");
        }

        var upcoming = reminders.Upcoming.Count(item => item.Trigger is null && item.Due <= now.AddDays(7));
        if (upcoming > 0)
        {
            parts.Add($"Önümüzdeki 7 günde {upcoming} hatırlatma var.");
        }

        return parts.Count == 0 ? "Bu hafta için henüz yeterli kayıt yok." : string.Join(" ", parts);
    }

    private string ListCommand(string action, string name, string items)
    {
        if (Lists is not { } lists)
        {
            return "Listeler şu an kullanılamıyor.";
        }

        var list = string.IsNullOrWhiteSpace(name) ? "alışveriş" : name.Trim().ToLower(Turkish);
        if (Simplify(list) is "yapilacaklar" or "yapilacak" or "yapilacakis")
        {
            return TodoCommand(action == "ekle" ? "ekle" : "listele", items)?.Text ?? "Yapılacaklar listende böyle bir iş yok.";
        }

        var title = TurkishGrammar.Capitalize(list);
        switch (Simplify(action))
        {
            case "ekle":
                var added = lists.Add(list, items);
                return added.Count == 0
                    ? $"{title} listesinde zaten var."
                    : $"{title} listesine ekledim: {string.Join(", ", added)}. Listede {lists.Items(list).Count} öğe var.";
            case "cikar" or "sil":
                return lists.Remove(list, items) is { } removed
                    ? $"{TurkishGrammar.Capitalize(removed)} {list} listesinden çıktı."
                    : $"{title} listesinde \"{items}\" yok.";
            case "temizle":
                var count = lists.Clear(list);
                return count == 0 ? $"{title} listesi zaten boş." : $"{title} listesi temizlendi ({count} öğe).";
            case "listeler":
                return lists.Names.Count == 0 ? "Henüz bir listen yok. \"Market listesine süt ekle\" diyebilirsin." : $"Listelerin: {string.Join(", ", lists.Names)}.";
            default:
                var all = lists.Items(list);
                return all.Count == 0 ? $"{title} listen boş." : $"{title} listende {all.Count} öğe var: {string.Join(", ", all)}.";
        }
    }
}
