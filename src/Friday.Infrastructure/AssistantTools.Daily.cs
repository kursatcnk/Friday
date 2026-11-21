using System.Globalization;
using System.Runtime.InteropServices;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Yapılacaklar, ortam sesleri, pano geçmişi, uygulama kapatma, kur çevirme ve alışkanlık önerileri.</summary>
public sealed partial class AssistantTools
{
    private AmbientSound? _ambient;
    private ClipboardHistory? _clipboard;

    /// <summary>Yapılacaklar listesi (App verir).</summary>
    public TodoStore? Todos { get; set; }

    /// <summary>Alışkanlık kaydı (App verir).</summary>
    public HabitTracker? Habits { get; set; }

    public AmbientSound Ambient => _ambient ??= new AmbientSound();

    public ClipboardHistory Clipboard => _clipboard ??= new ClipboardHistory();

    /// <summary>Friday konuşurken ortam sesi kısılır; ortam sesi hiç açılmadıysa bir şey yapılmaz.</summary>
    public void DuckAmbient(bool ducked) => _ambient?.Duck(ducked);

    // ---------- Yapılacaklar ----------

    /// <returns>Cevap; "faturayı ödedim" listede yoksa null (soru dil modeline gider).</returns>
    private QuickReply? TodoCommand(string action, string text)
    {
        if (Todos is not { } todos)
        {
            return new QuickReply("Yapılacaklar listesi hazır değil.");
        }

        var now = DateTime.Now;
        switch (Simplify(action))
        {
            case "ekle":
                var before = todos.Open.Count;
                var (task, due, important) = TodoDue.Extract(text, now);
                var item = todos.Add(task, now, due, important);
                ledger.Record(DataFlow.Local, "Yapılacaklar", "Bir iş eklendi");
                var deadline = item.Due is { } until ? $" Son gün {DueSpoken(until, DateOnly.FromDateTime(now))}." : "";
                if (ScheduleConflicts.ForDeadline(item, todos.Open) is { } busy)
                {
                    deadline += " " + busy;
                }
                return new QuickReply(todos.Open.Count == before
                    ? $"\"{item.Text}\" zaten listende.{deadline}"
                    : $"Yapılacaklara ekledim: {item.Text}.{deadline}" + (item.Important ? " Önemli olarak işaretledim." : ""));
            case "tamamla":
                if (todos.Find(text) is not { } match)
                {
                    // "Meditasyonu tamamladım": listede böyle bir iş yoksa takip edilen bir alışkanlık olabilir.
                    return HabitBook is { } book ? MarkHabit(book, text, DateOnly.FromDateTime(now)) : null;
                }

                todos.Complete(match.Id, now);
                var left = todos.Open.Count;
                return new QuickReply($"Harika, \"{match.Text}\" tamamlandı." + (left == 0 ? " Liste bitti, tebrikler!" : $" {left} iş kaldı."));
            case "temizle":
                var removed = todos.ClearDone();
                return new QuickReply(removed == 0 ? "Temizlenecek tamamlanmış iş yok." : $"{removed} tamamlanmış işi listeden kaldırdım.");
            default:
                var open = todos.Open;
                var today = DateOnly.FromDateTime(now);
                string Spoken(TodoItem todo) => todo.Due is { } until ? $"{todo.Text} ({DueSpoken(until, today)})" : todo.Text;
                ShowListCard("Yapılacaklar", "", open.Select(todo => new CardRow((todo.Important ? "★ " : "") + todo.Text, TodoDue.Label(todo.Due, today))));
                return new QuickReply(open.Count switch
                {
                    0 => "Yapılacaklar listen boş.",
                    1 => $"Tek bir iş var: {Spoken(open[0])}.",
                    _ => $"{open.Count} iş var: {TurkishGrammar.Join([.. open.Take(5).Select(Spoken)])}" + (open.Count > 5 ? " ve diğerleri." : ".")
                });
        }
    }

    /// <summary>Son tarihin okunuşu: "bugün", "yarın", "cuma", "25 Kasım", "2 gün önce geçti".</summary>
    private static string DueSpoken(DateOnly due, DateOnly today) => (due.DayNumber - today.DayNumber) switch
    {
        < 0 and var late => $"{-late} gün önce geçti",
        0 => "bugün",
        1 => "yarın",
        <= 6 => due.ToString("dddd", Turkish).ToLower(Turkish),
        _ => due.ToString("d MMMM", Turkish)
    };

    // ---------- Ortam sesleri ----------

    private QuickReply AmbientCommand(string action, string kind, string minutesText)
    {
        if (Simplify(action) == "kapat")
        {
            if (Ambient.Playing is null)
            {
                return new QuickReply("Çalan bir ortam sesi yok.");
            }

            Ambient.FadeOut();
            return new QuickReply("Ortam sesini kapattım.", Silent: true);
        }

        var minutes = double.TryParse(minutesText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : 60;
        Ambient.Volume = Settings.AmbientVolume;
        Ambient.Start(kind, TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 480)));
        ledger.Record(DataFlow.Local, "Ortam sesi", $"{RoutineStep.AmbientName(kind)} · {minutes:0} dk · bilgisayarda üretiliyor");
        return new QuickReply($"{RoutineStep.AmbientName(kind)} açık; {TurkishGrammar.Duration(minutes)} sonra kendiliğinden kapanır.");
    }

    // ---------- Pano geçmişi ----------

    private QuickReply ClipboardHistoryCommand(string action, string indexText)
    {
        if (!Clipboard.IsRunning)
        {
            return new QuickReply("Pano geçmişi kapalı. Ayarlar › Verilerin bölümünden açabilirsin; kopyaladıkların yalnızca bellekte tutulur.");
        }

        var items = Clipboard.Items;
        if (items.Count == 0)
        {
            return new QuickReply("Pano geçmişin şu an boş.");
        }

        static string Preview(string text)
        {
            var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return flat.Length > 60 ? flat[..60] + "…" : flat;
        }

        if (Simplify(action) == "listele")
        {
            ShowListCard("Pano geçmişi", "", items.Take(6).Select((text, index) => new CardRow(Preview(text), $"{index + 1}")));
            return new QuickReply($"Son kopyaladıkların: {string.Join("; ", items.Take(3).Select((text, index) => $"{index + 1}) {Preview(text)}"))}. Birini yapıştırmamı istersen, ör. 2. kopyaladığımı yapıştır de.");
        }

        var position = int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 1;
        if (position < 1 || position > items.Count)
        {
            return new QuickReply($"Pano geçmişinde {items.Count} kayıt var.");
        }

        ClipboardText.Set(items[position - 1]);
        if (ActiveWindow is { } window)
        {
            Desktop.BringToFront(window.Handle);
        }

        Keys.Chord(Keys.Control, 0x56);
        ledger.Record(DataFlow.Local, "Pano geçmişi", $"{position}. kayıt yapıştırıldı");
        return new QuickReply("Yapıştırdım.", Silent: true);
    }

    // ---------- Uygulama kapatma ----------

    /// <returns>Cevap; öyle bir pencere yoksa null (soru dil modeline gider).</returns>
    private QuickReply? CloseAppCommand(string name)
    {
        var wanted = Simplify(name);
        var window = wanted is "pencere" or "bupencere" or "bu"
            ? ActiveWindow
            : Desktop.FindWindow(name);
        if (window is null || window.ProcessId == Environment.ProcessId || NotApps.Contains(wanted))
        {
            return null;
        }

        // Kapatma isteği gönderilir; uygulama kaydedilmemiş iş varsa kendisi sorar.
        NativeMethods.PostMessageW(window.Handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
        ledger.Record(DataFlow.Local, "Uygulama", $"{window.Process} kapatıldı");
        return new QuickReply($"{window.Process} kapatıldı.");
    }

    // ---------- Kur çevirme ----------

    private async Task<QuickReply> ConvertCurrencyAsync(string amountText, string from, string to)
    {
        var (rates, source) = await GetRatesAsync().ConfigureAwait(false);
        double Rate(string code) => code == "TRY" ? 1 : rates.GetValueOrDefault(code);
        if (!double.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || Rate(from) == 0 || Rate(to) == 0)
        {
            return new QuickReply("Bu kurun bilgisini bulamadım.");
        }

        var result = amount * Rate(from) / Rate(to);
        static string Name(string code) => code == "TRY" ? "lira" : CurrencyLabel(code);
        var formatted = result.ToString(result >= 100 ? "N0" : "N2", Turkish);
        ShowCard(new AnswerCard("Döviz çevirme", "")
        {
            Big = $"{formatted} {(to == "TRY" ? "₺" : Name(to))}",
            BigCaption = $"{amount.ToString("N0", Turkish)} {Name(from)}",
            Rows = [new CardRow("Kaynak", source)]
        });
        return new QuickReply($"{amount.ToString("N0", Turkish)} {Name(from)} yaklaşık {formatted} {Name(to)} eder.");
    }

    // ---------- Alışkanlıklar ----------

    /// <summary>Bir rutin ya da uygulama kullanıldı; alışkanlık kaydına yazılır.</summary>
    private void RecordHabit(string key, string label) => Habits?.Record(key, label, DateTime.Now);

    /// <summary>
    /// Şu an önerilecek bir alışkanlık varsa sorusunu döndürür ve "evet" denince yapılacak işi bekletir:
    /// "Genelde bu saatlerde çalışma modu rutinini çalıştırıyorsun. Çalıştırayım mı?"
    /// </summary>
    public string? HabitSuggestion()
    {
        if (!Settings.HabitSuggestions || Habits is not { } habits || HasPendingConfirmation || Rhythm?.Snapshot() is { IsAway: true })
        {
            return null;
        }

        if (habits.Suggest(DateTime.Now) is not { } suggestion)
        {
            return null;
        }

        var parts = suggestion.Key.Split(':', 2);
        var target = parts.Length == 2 ? parts[1] : suggestion.Label;
        if (parts[0] == "rutin")
        {
            Ask("Alışkanlık", async token => await RoutineReplyAsync(target, token).ConfigureAwait(false) ?? new QuickReply("Rutini çalıştıramadım."));
            return $"Genelde bu saatlerde {suggestion.Label} rutinini çalıştırıyorsun. Çalıştırayım mı?";
        }

        Ask("Alışkanlık", _ => Task.FromResult(FindApp(target) is { } app ? new QuickReply(Open(app)) : new QuickReply($"{target} bulunamadı.")));
        return $"Genelde bu saatlerde {suggestion.Label} uygulamasını açıyorsun. Açayım mı?";
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
