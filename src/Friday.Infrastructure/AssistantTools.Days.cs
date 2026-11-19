using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Önemli günler: kutlamalar, bir gün önceden hatırlatma ve "yaklaşan özel günler".</summary>
public sealed partial class AssistantTools
{
    private DateOnly _eveAsked;

    /// <summary>Son sabah karşılaması bir soru sordu mu ("Akşam 7'de hatırlatayım mı?"); cevap için mikrofon açılır.</summary>
    public bool GreetingAsks { get; private set; }

    private QuickReply SpecialDaysCommand()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var days = SpecialDays.Upcoming(memory.Items, today, 60);
        ShowListCard("Önemli günler", "", days.Take(MaxCardRows).Select(day => new CardRow(TurkishGrammar.Capitalize(day.Title), SpecialDays.When(day.Date, today))));
        var personal = days.Where(day => day.Kind is not SpecialKind.Holiday).ToList();
        if (days.Count == 0)
        {
            return new QuickReply("Önümüzdeki iki ayda bildiğim özel bir gün yok. \"Unutma, annemin doğum günü 3 Mart\" dersen hatırlar, günü gelince söylerim.");
        }

        var first = days.Take(3).Select(day => day.Date.DayNumber - today.DayNumber <= 6
            ? $"{SpecialDays.When(day.Date, today)} {day.Title}"
            : $"{day.Title}, {SpecialDays.When(day.Date, today)}").ToList();
        return new QuickReply($"Yaklaşanlar: {TurkishGrammar.Join(first)}." + (personal.Count == 0
            ? " Hafızanda tarihli bir doğum günü yok; \"Unutma, annemin doğum günü 3 Mart\" diyebilirsin."
            : ""));
    }

    /// <summary>Sabah karşılamasına bugünün kutlamaları; yakın birinin doğum günüyse akşam için hatırlatma önerilir.</summary>
    private void SpecialDayGreetings(List<string> parts, DateTime now)
    {
        GreetingAsks = false;
        var today = DateOnly.FromDateTime(now);
        foreach (var day in SpecialDays.Upcoming(memory.Items, today, 0))
        {
            if (SpecialDays.Greeting(day, Settings.UserName) is not { } greeting)
            {
                continue;
            }

            parts.Add(greeting);
            if (day.Kind == SpecialKind.Birthday && now.Hour < 18 && !GreetingAsks)
            {
                var title = day.Title;
                Ask("Özel gün", _ => Task.FromResult(AddDayReminder(now.Date.AddHours(19), $"{title} için aramayı ya da mesaj atmayı")));
                parts.Add("Akşam 7'de hatırlatayım mı?");
                GreetingAsks = true;
            }
        }
    }

    /// <summary>
    /// Akşam, ertesi gün bir doğum günü ya da yıldönümü varsa bir kez sorulur: "Yarın annenin doğum günü. Hediye ya da
    /// mesaj hazır mı? Yarın sabah 10'da hatırlatayım mı?" Sorulacak bir şey yoksa null.
    /// </summary>
    public string? EveReminder(DateTime now)
    {
        var tomorrow = DateOnly.FromDateTime(now).AddDays(1);
        if (_eveAsked == tomorrow || HasPendingConfirmation)
        {
            return null;
        }

        var day = SpecialDays.Upcoming(memory.Items, tomorrow, 0)
            .FirstOrDefault(item => item.Kind is SpecialKind.Birthday or SpecialKind.Anniversary);
        if (day is null)
        {
            return null;
        }

        _eveAsked = tomorrow;
        Ask("Özel gün", _ => Task.FromResult(AddDayReminder(tomorrow.ToDateTime(new TimeOnly(10, 0)), $"bugün {day.Title}")));
        ledger.Record(DataFlow.Local, "Özel gün", $"Yarın: {day.Title}");
        return $"Yarın {day.Title}. Hediye ya da bir mesaj hazır mı? İstersen yarın sabah 10'da hatırlatayım.";
    }

    private QuickReply AddDayReminder(DateTime due, string text)
    {
        var now = DateTime.Now;
        if (due <= now)
        {
            due = now.AddMinutes(30);
        }

        reminders.Add(due, text, now);
        EnsureReminderClock();
        ledger.Record(DataFlow.Local, "Hatırlatma", $"Özel gün için {due:dd.MM HH:mm}'e kuruldu");
        return new QuickReply($"Tamam, {due.ToString(due.Date == now.Date ? "HH:mm" : "d MMMM HH:mm", Turkish)}'{RhythmText.TimeSuffix(due)} hatırlatırım.");
    }
}
