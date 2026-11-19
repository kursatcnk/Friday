using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Sabit giderler, uygulama süre sınırları ve görüşme sonrası not.</summary>
public sealed partial class AssistantTools
{
    private readonly Dictionary<string, DateOnly> _limitWarned = new(StringComparer.OrdinalIgnoreCase);

    // ---------- Sabit giderler ----------

    /// <returns>Cevap; "X'i ödedim" kayıtlı bir gidere ya da işe uymuyorsa null (soru dil modeline gider).</returns>
    private QuickReply? BillCommand(string action, string name, string amountText, string dayText)
    {
        if (Expenses is not { } book)
        {
            return new QuickReply("Harcama kaydı hazır değil.");
        }

        var now = DateTime.Now;
        switch (Simplify(action))
        {
            case "ekle":
                if (!decimal.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount <= 0
                    || !int.TryParse(dayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
                {
                    return new QuickReply("Tutarı ve ayın kaçında ödendiğini anlayamadım; ör. \"kira her ayın 5'inde 15 bin lira\".");
                }

                var bill = book.AddBill(ExpenseBook.CleanNote(name), amount, day);
                ledger.Record(DataFlow.Local, "Sabit gider", $"{bill.Name} · {ExpenseBook.Money(bill.Amount)} · ayın {bill.Day}'i");
                return new QuickReply($"Tamam, {bill.Name.ToLower(Turkish)}: her ayın {bill.Day}'{DaySuffix(bill.Day)} {ExpenseBook.Spoken(bill.Amount)}. O gün sabah hatırlatırım; ödeyince \"{bill.Name.ToLower(Turkish)} ödedim\" de, harcamalara işlerim.");
            case "sil":
                return new QuickReply(book.RemoveBill(name) ? $"{TurkishGrammar.Capitalize(name)} sabit giderlerden çıkarıldı." : $"\"{name}\" diye bir sabit gider yok.");
            case "odendi":
                if (book.FindBill(name) is { } due)
                {
                    if (book.PayBill(due.Name, now) is not { } paid)
                    {
                        return new QuickReply($"{due.Name} bu ay zaten ödendi olarak işlenmişti.");
                    }

                    ledger.Record(DataFlow.Local, "Sabit gider", $"{due.Name} ödendi");
                    return new QuickReply($"{due.Name} ödendi; {ExpenseBook.Spoken(paid.Amount)} harcamalara işlendi.");
                }

                // Kayıtlı bir gider değilse yapılacaklardaki iş olabilir ("faturayı öde").
                return TodoCommand("tamamla", name);
            default:
                var bills = book.Bills;
                if (bills.Count == 0)
                {
                    return new QuickReply("Kayıtlı sabit gider yok. \"Kira her ayın 5'inde 15 bin lira\" ya da \"Netflix aboneliğim ayda 230 lira, her ayın 12'si\" diyebilirsin.");
                }

                var today = DateOnly.FromDateTime(now);
                ShowListCard($"Sabit giderler · aylık {ExpenseBook.Money(bills.Sum(bill => bill.Amount))}", "",
                    bills.Take(MaxCardRows + 2).Select(bill => new CardRow($"{bill.Day}. gün · {bill.Name}", ExpenseBook.Money(bill.Amount) + (bill.IsPaid(today) ? " ✓" : ""))));
                var next = bills.Where(bill => !bill.IsPaid(today) && bill.DueIn(today.Year, today.Month) >= today).OrderBy(bill => bill.Day).FirstOrDefault();
                return new QuickReply($"{bills.Count} sabit giderin var, ayda toplam {ExpenseBook.Spoken(bills.Sum(bill => bill.Amount))}."
                    + (next is null ? " Bu ayın hepsi ödendi ya da günü geçti." : $" Sıradaki: {next.Name.ToLower(Turkish)}, {SpecialDays.When(next.DueIn(today.Year, today.Month), today)}, {ExpenseBook.Spoken(next.Amount)}."));
        }
    }

    /// <summary>Sabah karşılamasına bugün ve yarın ödenecek sabit giderler.</summary>
    private void BillsMorning(List<string> parts, DateTime now)
    {
        if (Expenses is not { } book)
        {
            return;
        }

        var today = DateOnly.FromDateTime(now);
        var dueToday = book.DueOn(today);
        if (dueToday.Count > 0)
        {
            parts.Add($"Bugün ödeme günü: {TurkishGrammar.Join([.. dueToday.Select(bill => $"{bill.Name.ToLower(Turkish)} {ExpenseBook.Spoken(bill.Amount)}")])}. Ödeyince söyle, harcamalara işlerim.");
        }

        var dueTomorrow = book.DueOn(today.AddDays(1));
        if (dueTomorrow.Count > 0)
        {
            parts.Add($"Yarın {TurkishGrammar.Join([.. dueTomorrow.Select(bill => bill.Name.ToLower(Turkish))])} ödenecek.");
        }
    }

    private static string DaySuffix(int day) => (day % 10) switch
    {
        1 or 5 or 8 => "i",
        2 or 7 => "si",
        3 or 4 => "ü",
        6 => "sı",
        9 => "u",
        _ => day switch { 10 or 30 => "u", 20 => "si", _ => "i" }
    };

    // ---------- Uygulama süre sınırları ----------

    private QuickReply LimitCommand(string action, string name, string minutesText)
    {
        var today = DateTime.Now;
        switch (Simplify(action))
        {
            case "ekle":
                if (!int.TryParse(minutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes <= 0)
                {
                    return new QuickReply("Süreyi anlayamadım; ör. \"YouTube'a günde 1 saat sınır koy\".");
                }

                var label = LimitLabel(name);
                Settings.AppLimits[label] = Math.Min(minutes, 24 * 60);
                SaveSettings?.Invoke(Settings);
                ledger.Record(DataFlow.Local, "Süre sınırı", $"{label}: günde {minutes} dk");
                var used = UsedMinutes(label, today);
                return new QuickReply($"Tamam, {label} için günde {TurkishGrammar.Duration(minutes)} sınır koydum; dolunca nazikçe söylerim." + (used > 0 ? $" Bugün şimdiye kadar {TurkishGrammar.Duration(used)} kullandın." : "")
                    + (Activity is null ? " Bunun için Ayarlar'da ekran süresinin açık olması gerekiyor." : ""));
            case "sil":
                var key = Settings.AppLimits.Keys.FirstOrDefault(item => Simplify(item) == Simplify(LimitLabel(name)) || Simplify(name).StartsWith(Simplify(item), StringComparison.Ordinal));
                if (key is null)
                {
                    return new QuickReply($"{TurkishGrammar.Capitalize(name)} için bir sınır yok.");
                }

                Settings.AppLimits.Remove(key);
                SaveSettings?.Invoke(Settings);
                return new QuickReply($"{key} sınırını kaldırdım.");
            default:
                if (Settings.AppLimits.Count == 0)
                {
                    return new QuickReply("Hiç süre sınırın yok. \"YouTube'a günde 1 saat sınır koy\" diyebilirsin.");
                }

                ShowListCard("Süre sınırları", "", Settings.AppLimits.Select(pair => new CardRow(pair.Key, $"{UsedMinutes(pair.Key, today):0} / {pair.Value} dk")));
                return new QuickReply(string.Join(" ", Settings.AppLimits.Select(pair => $"{pair.Key}: bugün {TurkishGrammar.Duration(UsedMinutes(pair.Key, today))}, sınır {TurkishGrammar.Duration(pair.Value)}.")));
        }
    }

    /// <summary>
    /// Sınırı dolan bir uygulama varsa (günde bir kez) söylenecek cümle: "Bugün YouTube'da 1 saati doldurdun; biraz ara
    /// vermeye ne dersin?" Yoksa null.
    /// </summary>
    public string? AppLimitAlert(DateTime now)
    {
        if (Activity is null || Settings.AppLimits.Count == 0)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(now);
        foreach (var (label, limit) in Settings.AppLimits)
        {
            if (_limitWarned.TryGetValue(label, out var warned) && warned == today)
            {
                continue;
            }

            if (UsedMinutes(label, now) >= limit)
            {
                _limitWarned[label] = today;
                ledger.Record(DataFlow.Local, "Süre sınırı", $"{label} günlük sınıra ulaştı");
                return $"Bugün {label} için koyduğun {TurkishGrammar.Duration(limit)} doldu. Biraz ara vermeye ne dersin?";
            }
        }

        return null;
    }

    /// <summary>Ekran süresinde bu adla geçen bugünkü dakikalar ("youtube" → YouTube).</summary>
    private double UsedMinutes(string label, DateTime day)
    {
        if (Activity is not { } activity)
        {
            return 0;
        }

        var wanted = Simplify(label);
        return activity.Day(day).Where(item => Simplify(item.App) == wanted || (wanted.Length >= 4 && Simplify(item.App).Contains(wanted, StringComparison.Ordinal)))
            .Sum(item => item.Seconds) / 60.0;
    }

    /// <summary>Ekran süresinde görünen adla eşleşen yazım: "youtube" → "YouTube"; bilinmiyorsa baş harfi büyük.</summary>
    private string LimitLabel(string spoken)
    {
        var wanted = Simplify(spoken);
        var known = Activity?.Day(DateTime.Now).Concat(Activity.Day(DateTime.Now.AddDays(-1))).Select(item => item.App)
            .FirstOrDefault(app => Simplify(app) == wanted);
        return known ?? TurkishGrammar.Capitalize(spoken.Trim());
    }

    // ---------- Görüşme sonrası not ----------

    /// <summary>Uzun bir görüşme bitti: "Teams görüşmesi bitti. Not almak istediğin bir şey var mı?" Cevap notlara yazılır.</summary>
    public string? MeetingNoteQuestion(string app, DateTime start, DateTime end)
    {
        if (!Settings.MeetingNotes || end - start < TimeSpan.FromMinutes(15) || HasPendingConfirmation || AwaitsAnswer)
        {
            return null;
        }

        _awaitingOwnsDecline = false;
        _awaitingAt = DateTime.Now;
        _awaitingAnswer = text =>
        {
            var clean = text.Trim();
            notes.Add($"{app} görüşmesi ({start:HH:mm}–{end:HH:mm}): {clean}", DateTime.Now);
            ledger.Record(DataFlow.Local, "Not", $"{app} görüşmesinden not alındı");
            return new QuickReply("Notlarına ekledim.");
        };
        return $"{app} görüşmesi bitti. Not almak istediğin bir şey var mı? Söylersen notlarına eklerim, yoksa \"yok\" de.";
    }
}
