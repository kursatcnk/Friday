using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Gün planı, bir işe odaklanma ve bağlantı kontrolü.</summary>
public sealed partial class AssistantTools
{
    private string? _focusTask;
    private DateTime _focusAsked;

    /// <summary>Odak bitti ve "işi bitirdin mi?" sorusu az önce soruldu; cevap için mikrofon açılır.</summary>
    public bool FocusQuestionPending => HasPendingConfirmation && DateTime.Now - _focusAsked < TimeSpan.FromSeconds(30);

    // ---------- Gün planı ----------

    private async Task<QuickReply> DayPlanAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        // Plan, kullanıcının günü genelde bitirdiği saatten bir saat önce biter (öğrenilmediyse 22.00).
        var usualEnd = Math.Clamp((int)DayEnd(now).TotalHours - 1, 17, 24);
        var dayEnd = now.Hour >= usualEnd - 1 ? Math.Min(24, now.Hour + 2) : usualEnd;
        var fixedItems = new List<FixedItem>();
        if (Calendar.IsConfigured)
        {
            try
            {
                foreach (var item in await Calendar.EventsAsync(now, now.Date.AddDays(1), cancellationToken).ConfigureAwait(false))
                {
                    if (!item.AllDay)
                    {
                        fixedItems.Add(new FixedItem(item.Start, item.End, item.Title));
                    }
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // Takvime ulaşılamazsa plan takvimsiz kurulur.
            }
        }

        foreach (var reminder in reminders.Upcoming.Where(item => item.Trigger is null && item.Due.Date == now.Date && item.Due > now))
        {
            fixedItems.Add(new FixedItem(reminder.Due, reminder.Due.AddMinutes(15), TurkishGrammar.Capitalize(reminder.Text.Length > 0 ? reminder.Text : "Hatırlatma")));
        }

        var todos = Todos?.Open.Select(item => item.Text).ToList() ?? [];
        var habits = HabitBook?.Pending(today).Select(habit => habit.Name).ToList() ?? [];
        var plan = DayPlanner.Plan(now, fixedItems, todos, habits, dayEnd);
        if (plan.Count == 0 || plan.All(block => block.Kind is PlanKind.Meal or PlanKind.Break))
        {
            return new QuickReply(todos.Count == 0 && habits.Count == 0
                ? "Planlanacak bir iş yok: yapılacaklar listen ve bugünkü alışkanlıkların boş. \"Yapılacaklara ekle: …\" diyerek başlayabilirsin."
                : "Bugün için plana sığacak kadar zaman kalmamış; yarın sabah yeniden sorarsan günü baştan planlarım.");
        }

        ShowListCard("Bugünün planı", "", plan.Where(block => block.Kind != PlanKind.Break).Take(MaxCardRows + 2)
            .Select(block => new CardRow($"{block.Start:HH:mm}–{block.End:HH:mm}", block.Kind == PlanKind.Focus ? $"Odak: {block.Title}" : block.Title)));
        var focus = plan.Where(block => block.Kind is PlanKind.Focus or PlanKind.Errand or PlanKind.Habit).ToList();
        if (focus.Count > 0)
        {
            Ask("Gün planı", _ =>
            {
                foreach (var block in focus)
                {
                    reminders.Add(block.Start, block.Kind is PlanKind.Focus or PlanKind.Errand ? $"plana göre şimdi {block.Title.ToLower(Turkish)} zamanı" : $"{block.Title.ToLower(Turkish)} zamanı", DateTime.Now);
                }

                EnsureReminderClock();
                ledger.Record(DataFlow.Local, "Gün planı", $"{focus.Count} blok hatırlatmalara eklendi");
                return Task.FromResult(new QuickReply($"{focus.Count} bloğun başında hatırlatırım."));
            });
        }

        ledger.Record(DataFlow.Local, "Gün planı", $"{plan.Count} bloklu plan hazırlandı");
        var free = TimeSpan.FromTicks(Math.Max(0, (now.Date.AddHours(dayEnd) - now).Ticks)) - TimeSpan.FromTicks(plan.Sum(block => (block.End - block.Start).Ticks));
        return new QuickReply($"Günün planı: {DayPlanner.Describe(plan)}." + (free > TimeSpan.FromMinutes(45) ? $" Arada {TurkishGrammar.Duration(Math.Round(free.TotalMinutes / 15) * 15)} boş vaktin de var." : "")
            + (focus.Count > 0 ? " Blokların başında hatırlatayım mı?" : ""));
    }

    // ---------- Bir işe odaklanma ----------

    private QuickReply FocusOnTask(string task, string minutesText)
    {
        if (Rhythm is not { Enabled: true } coach)
        {
            return new QuickReply("Ritim koçu kapalı; Ayarlar'dan açabilirsin.");
        }

        var minutes = double.TryParse(minutesText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? Math.Min(value, 180) : 25;
        var todo = Todos?.Find(task);
        _focusTask = todo?.Text ?? TurkishGrammar.Capitalize(task.Trim());
        _focusAt = DateTime.Now;
        SaveFocus();
        coach.StartFocus(minutes, _focusTask);
        ledger.Record(DataFlow.Local, "Odak", $"{minutes:0} dk · {_focusTask}");
        // İşe başlarken onunla ilgili not ya da bilgi varsa hatırlatılır ("Dünkü notunda: müşteri mavi tonları istedi.").
        return new QuickReply($"Tamam, {TurkishGrammar.Duration(minutes)} boyunca yalnızca {_focusTask.ToLower(Turkish)}. Bitince soracağım"
            + (todo is null ? "." : "; bitirdiysen listeden düşerim.") + (MomentNote(_focusTask) is { } related ? " " + related : ""));
    }

    /// <summary>Panelden bir işe odak ("Raporu bitir", 50 dk): sesli komuttaki gibi biter ve "bitirdin mi?" diye sorulur.</summary>
    public string StartTaskFocus(string task, double minutes) =>
        FocusOnTask(task, minutes.ToString(CultureInfo.InvariantCulture)).Text;

    /// <summary>
    /// Odak bitince söylenecek soru ("Faturayı öde işini bitirdin mi?"); "evet" denirse iş yapılacaklardan tamamlanır.
    /// Odak bir işe bağlı değilse null.
    /// </summary>
    public string? FocusTaskQuestion()
    {
        if (Interlocked.Exchange(ref _focusTask, null) is not { } task)
        {
            return null;
        }

        var todo = Todos?.Open.FirstOrDefault(item => item.Text == task);
        _focusAsked = DateTime.Now;
        Ask("Odak", _ =>
        {
            if (todo is not null && Todos is { } todos)
            {
                todos.Complete(todo.Id, DateTime.Now);
                var left = todos.Open.Count;
                return Task.FromResult(new QuickReply($"Harika, \"{todo.Text}\" tamamlandı." + (left == 0 ? " Liste bitti!" : $" {left} iş kaldı.")));
            }

            return Task.FromResult(new QuickReply("Harika, eline sağlık!"));
        });
        return $"{task} işini bitirdin mi?";
    }

    // ---------- Bağlantı kontrolü ----------

    private async Task<QuickReply> NetworkCommandAsync(CancellationToken cancellationToken)
    {
        ledger.Record(DataFlow.Outbound, "Bağlantı kontrolü", "Modeme ve 1.1.1.1'e ping, cloudflare.com ad çözme");
        var probe = await NetworkCheck.ProbeAsync(cancellationToken).ConfigureAwait(false);
        static string Ms(double? value) => value is { } ms ? $"{Math.Round(ms)} ms" : "ulaşılamadı";
        ShowCard(new AnswerCard("Bağlantı", "")
        {
            Big = probe.InternetMs is { } internet ? $"{Math.Round(internet)} ms" : "—",
            BigCaption = probe.Adapter.Length > 0 ? probe.Adapter : "bağlantı yok",
            Rows = [new CardRow("Modem", Ms(probe.GatewayMs)), new CardRow("İnternet (1.1.1.1)", Ms(probe.InternetMs)), new CardRow("Paket kaybı", $"%{Math.Round(probe.InternetLoss * 100)}"), new CardRow("Ad çözme", Ms(probe.DnsMs))]
        });
        return new QuickReply(NetworkDiagnosis.Describe(probe));
    }
}
