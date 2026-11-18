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
