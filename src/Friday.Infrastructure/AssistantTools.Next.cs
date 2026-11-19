using System.Globalization;
using System.Text.RegularExpressions;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>"Şimdi ne yapayım?" ve fişten harcama.</summary>
public sealed partial class AssistantTools
{
    /// <summary>
    /// Kayıtlara bakıp tek bir öneri seçer: geciken iş, bugün teslim, uzun süredir mola verilmemesi, plandaki şu anki blok,
    /// bugün ödenecek fatura, serisi bozulacak alışkanlık, bekleyen kelimeler. Dil modeli kullanılmaz.
    /// </summary>
    private QuickReply NextAction()
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var ideas = new List<(int Score, string Text)>();
        var open = Todos?.Open ?? [];
        if (open.FirstOrDefault(item => item.Due < today) is { } late)
        {
            ideas.Add((100, $"\"{late.Text}\" gecikti; önce onu bitirmeye ne dersin? \"{late.Text.ToLower(Turkish)} işine 25 dakika odaklan\" demen yeter."));
        }

        if (open.FirstOrDefault(item => item.Due == today) is { } dueToday)
        {
            ideas.Add((90, $"\"{dueToday.Text}\" işinin son günü bugün; şimdi ona başlamak iyi olur."));
        }

        if (Rhythm?.Snapshot() is { } rhythm && rhythm.FocusEnd is null && rhythm.StretchStart is { } start && now - start >= TimeSpan.FromMinutes(50))
        {
            ideas.Add((85, $"{TurkishGrammar.Duration(Math.Round((now - start).TotalMinutes))} boyunca aralıksız ekran başındaydın; önce 5 dakikalık bir mola ver, sonra daha iyi odaklanırsın."));
        }

        if (Expenses?.DueOn(today) is { Count: > 0 } bills)
        {
            ideas.Add((75, $"Bugün {bills[0].Name.ToLower(Turkish)} ödeme günü ({ExpenseBook.Spoken(bills[0].Amount)}); bitirip aklından çıkar."));
        }

        var habits = HabitBook?.Pending(today).Select(habit => (habit.Name, Streak: HabitBook.Streak(habit, today))).OrderByDescending(item => item.Streak).ToList() ?? [];
        if (habits.Count > 0 && now.Hour >= 17)
        {
            var habit = habits[0];
            ideas.Add((habit.Streak >= 2 ? 72 : 55, habit.Streak >= 2
                ? $"{habit.Name} bugün henüz yapılmadı; {habit.Streak} günlük serin var, şimdi tam zamanı."
                : $"Bugün {habit.Name.ToLower(Turkish)} için hâlâ vakit var."));
        }

        var block = DayPlanner.Plan(now, [], open.Select(item => item.Text), habits.Select(item => item.Name)).FirstOrDefault(item => item.Kind is PlanKind.Focus or PlanKind.Habit);
        if (block is not null && open.Count > 0)
        {
            ideas.Add((60, $"\"{block.Title}\" ile başla; 45 dakikalık bir odak bloğu yeter. Hazırsan \"{block.Title.ToLower(Turkish)} işine odaklan\" de."));
        }

        if (Vocabulary is { Started: true } deck && deck.Stats(today).Waiting is > 0 and var waiting)
        {
            ideas.Add((30, $"Beş dakikan varsa {waiting} kelime seni bekliyor; \"kelime çalışalım\" de."));
        }

        var ordered = ideas.OrderByDescending(idea => idea.Score).ToList();
        ledger.Record(DataFlow.Local, "Öneri", $"{ordered.Count} seçenek tartıldı");
        if (ordered.Count == 0)
        {
            return new QuickReply(now.Hour >= 22 || now.Hour < 5
                ? "Bekleyen bir şey yok; bence dinlenme vakti. İyi geceler!"
                : "Acil bir şey görünmüyor: listen boş, ödeme yok. Yeni bir şey öğrenmek ya da kısa bir yürüyüş için güzel bir an.");
        }

        return new QuickReply(ordered[0].Text + (ordered.Count > 1 && ordered[1].Score >= 55 ? $" Ondan sonra: {Lowered(ordered[1].Text)}" : ""));
    }

    private static string Lowered(string text) => text.Length == 0 ? text : char.ToLower(text[0], Turkish) + text[1..];

    // ---------- Fişten harcama ----------

    /// <summary>Panodaki fiş ya da fatura görselinden toplam tutar ve yer okunup harcama olarak eklenir.</summary>
    private async Task<QuickReply> ReceiptAsync(CancellationToken cancellationToken)
    {
        if (await Task.Run(ClipboardImage.GetJpeg, cancellationToken).ConfigureAwait(false) is not { } jpeg)
        {
            return new QuickReply("Panoda bir fiş görseli yok. Win+Shift+S ile fişin fotoğrafından ya da e-faturadan bir alan seç, sonra yeniden söyle.");
        }

        ledger.Record(DataFlow.Local, "Fiş", "Görsel bellekte işlendi, kaydedilmedi, yerel görme modeline verildi");
        var answer = await chat.CompleteAsync(
            Settings.VisionModel,
            "Bu bir alışveriş fişi ya da fatura. Ödenen genel TOPLAM tutarı (TL) ve mağaza ya da firma adını tam olarak şu biçimde yaz: TUTAR=123,45;YER=Mağaza adı. Toplam okunmuyorsa TUTAR=yok yaz. Başka hiçbir şey yazma.",
            "Fişteki toplamı ve yeri yaz.",
            [jpeg],
            "0",
            cancellationToken).ConfigureAwait(false);
        if (ParseReceipt(answer) is not { } receipt)
        {
            return new QuickReply("Fişteki toplamı okuyamadım. Tutarı söylersen ben eklerim, ör. \"markete 450 lira harcadım\".");
        }

        return ExpenseCommand("ekle", receipt.Amount.ToString("0.##", CultureInfo.InvariantCulture), receipt.Place, "", "");
    }

    /// <summary>"TUTAR=1.234,50;YER=Migros" → (1234.5, "Migros"); okunamazsa null.</summary>
    internal static (decimal Amount, string Place)? ParseReceipt(string answer)
    {
        var match = ReceiptLine().Match(answer);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups["a"].Value.Trim();
        // Türkçe biçim: binlik nokta, ondalık virgül; virgül yoksa son nokta ondalık sayılır ("123.45").
        var normalized = raw.Contains(',', StringComparison.Ordinal)
            ? raw.Replace(".", "", StringComparison.Ordinal).Replace(',', '.')
            : raw.Count(letter => letter == '.') == 1 && raw.Length - raw.IndexOf('.', StringComparison.Ordinal) == 3 ? raw : raw.Replace(".", "", StringComparison.Ordinal);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount > 0
            ? (amount, match.Groups["p"].Value.Trim().Trim('"', '.'))
            : null;
    }

    [GeneratedRegex(@"TUTAR\s*=\s*(?<a>[\d.,]+)\s*(?:TL|₺)?\s*;\s*YER\s*=\s*(?<p>[^\r\n;]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ReceiptLine();
}
