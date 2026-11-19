using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Her yerde arama, kaçırılan bildirimler, kelime çevirisi, ekrandaki yazı ve panodakini kaydetme.</summary>
public sealed partial class AssistantTools
{
    /// <summary>Friday'in kendiliğinden söyledikleri (zaman, metin); FridayAssistant verir.</summary>
    public Func<IReadOnlyList<(DateTime At, string Text)>>? Announcements { get; set; }

    // ---------- Her yerde arama ----------

    private QuickReply SearchEverywhere(string query)
    {
        var words = query.Split([' ', ',', '\'', '’'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Simplify).Where(word => word.Length >= 2).Select(word => word.Length > 5 ? word[..5] : word).Distinct().ToList();
        if (words.Count == 0)
        {
            return new QuickReply("Ne arayacağımı anlayamadım.");
        }

        bool Hit(string text) => Simplify(text) is var simple && words.All(word => simple.Contains(word, StringComparison.Ordinal));
        var now = DateTime.Now;
        var hits = new List<(string Source, DateTime When, string Text)>();
        hits.AddRange(memory.Items.Where(item => Hit(item.Text)).Select(item => ("Hafıza", item.Created.LocalDateTime, item.Text)));
        hits.AddRange(notes.All.Where(note => Hit(note.Text)).Select(note => ("Not", note.Time, note.Text)));
        if (Journal is { } journal)
        {
            hits.AddRange(journal.Between(now.AddYears(-1), now).Where(entry => Hit(entry.Text)).Select(entry => ("Günlük", entry.When, entry.Text)));
        }

        if (Todos is { } todos)
        {
            hits.AddRange(todos.Visible(now).Where(item => Hit(item.Text)).Select(item => (item.IsDone ? "Biten iş" : "İş", item.Created, item.Text)));
        }

        if (Lists is { } lists)
        {
            foreach (var name in lists.Names)
            {
                hits.AddRange(lists.Items(name).Where(item => Hit(item) || Hit(name)).Select(item => ($"{TurkishGrammar.Capitalize(name)} listesi", now, item)));
            }
        }

        if (Expenses is { } expenses)
        {
            hits.AddRange(expenses.Items.Where(item => Hit($"{item.Note} {item.Category}")).Select(item => ("Harcama", item.When, $"{(item.Note.Length > 0 ? item.Note : item.Category)} · {ExpenseBook.Money(item.Amount)}")));
            hits.AddRange(expenses.Bills.Where(bill => Hit(bill.Name)).Select(bill => ("Sabit gider", now, $"{bill.Name} · ayın {bill.Day}'i · {ExpenseBook.Money(bill.Amount)}")));
        }

        if (HabitBook is { } habits)
        {
            hits.AddRange(habits.Habits.Where(habit => Hit(habit.Name)).Select(habit => ("Alışkanlık", habit.Created.ToDateTime(TimeOnly.MinValue), habit.Name)));
        }

        hits.AddRange(Settings.Snippets.Where(pair => Hit(pair.Key) || Hit(pair.Value)).Select(pair => ("Kısaltma", now, $"{pair.Key}: {pair.Value}")));
        hits.AddRange(history.Search(query, 5).Select(hit => ("Sohbet", hit.Chat.Updated.LocalDateTime, hit.Snippet)));

        ledger.Record(DataFlow.Local, "Arama", $"Friday'in kayıtlarında arandı ({hits.Count} sonuç)");
        if (hits.Count == 0)
        {
            return new QuickReply($"\"{query}\" hiçbir kaydında geçmiyor: notlar, günlük, işler, listeler, hafıza, harcamalar ve sohbetlere baktım.");
        }

        var ordered = hits.OrderByDescending(hit => hit.When).ToList();
        ShowListCard($"\"{query}\" · {hits.Count} kayıt", "", ordered.Take(MaxCardRows + 2).Select(hit => new CardRow(hit.Source, Short(hit.Text, 60))));
        var counts = TurkishGrammar.Join([.. ordered.GroupBy(hit => hit.Source).Select(group => $"{SourcePlace(group.Key)} {group.Count()}")]);
        var newest = ordered[0];
        return new QuickReply($"\"{query}\" için {hits.Count} kayıt buldum: {counts}. En yenisi {newest.Source.ToLower(Turkish)}: {Short(newest.Text, 120)}");
    }

    private static string SourcePlace(string source) => source switch
    {
        "Hafıza" => "hafızada",
        "Not" => "notlarda",
        "Günlük" => "günlükte",
        "İş" or "Biten iş" => "işlerde",
        "Harcama" => "harcamalarda",
        "Sabit gider" => "sabit giderlerde",
        "Alışkanlık" => "alışkanlıklarda",
        "Kısaltma" => "kısaltmalarda",
        "Sohbet" => "sohbetlerde",
        _ => source.ToLower(Turkish).Replace(" listesi", " listesinde", StringComparison.Ordinal)
    };

    private static string Short(string text, int length)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > length ? flat[..length].TrimEnd() + "…" : flat;
    }

    // ---------- Kaçırılan bildirimler ----------

    private QuickReply MissedAnnouncements()
    {
        var recent = (Announcements?.Invoke() ?? []).Where(item => DateTime.Now - item.At < TimeSpan.FromHours(6)).ToList();
        if (recent.Count == 0)
        {
            return new QuickReply("Son saatlerde kendiliğimden bir şey söylemedim.");
        }

        ShowListCard("Son bildirimler", "", recent.TakeLast(MaxCardRows + 2).Reverse().Select(item => new CardRow(item.At.ToString("HH:mm", CultureInfo.InvariantCulture), Short(item.Text, 70))));
        var last = recent.TakeLast(3).Select(item => $"{item.At.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.TimeSuffix(item.At)}: {Short(item.Text, 140).TrimEnd('.')}").ToList();
        return new QuickReply((recent.Count > 3 ? $"Son 6 saatte {recent.Count} şey söyledim; son üçü: " : "Söylediklerim: ") + string.Join(". ", last) + ".");
    }

    // ---------- Kelime çevirisi ----------

    private async Task<QuickReply> VocabularyTranslateAsync(string action, string word, CancellationToken cancellationToken)
    {
        if (Vocabulary is not { } deck)
        {
            return new QuickReply("Kelime pratiği hazır değil.");
        }

        var previous = "";
        if (Simplify(action) == "secili")
        {
            previous = ClipboardText.Get();
            word = await CopySelectionAsync(cancellationToken).ConfigureAwait(false);
            Restore(previous);
        }

        word = word.Trim().Trim('"', '\'', '“', '”', '.', ',');
        if (word.Length == 0 || word.Length > 60 || word.Contains('\n', StringComparison.Ordinal))
        {
            return new QuickReply(word.Length == 0 ? "Seçili bir kelime bulamadım. Önce kelimeyi seç, sonra söyle." : "Seçim bir kelime ya da kısa bir ifade olmalı.");
        }

        var meaning = await chat.CompleteAsync(
            Settings.OllamaModel,
            "Sana İngilizce bir kelime ya da kısa bir ifade verilecek. Türkçe karşılıklarını, en yaygını başta, en fazla üç tane, virgülle ayırarak yaz. Başka hiçbir şey yazma; açıklama, tırnak, nokta yok.",
            word,
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        var back = meaning.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()?.Trim('"', '\'', '.', '*', ' ') ?? "";
        if (back.Length == 0 || back.Length > 80)
        {
            return new QuickReply($"\"{word}\" için bir anlam bulamadım. \"Kelime ekle: {word} = anlamı\" diyebilirsin.");
        }

        var card = deck.Add(word, back, DateOnly.FromDateTime(DateTime.Now));
        ledger.Record(DataFlow.Local, "Kelime", $"\"{card.Front}\" desteye eklendi (anlam yerel modelden)");
        return new QuickReply($"Desteye ekledim: {card.Front}, {card.Back}. Sıradaki çalışmada sorarım.");
    }

    // ---------- Ekrandaki yazı ve panodan kaydetme ----------

    private async Task<QuickReply> ScreenTextAsync(CancellationToken cancellationToken)
    {
        var screen = ScreenCapture.Capture(false);
        ledger.Record(DataFlow.Local, "Ekran görüntüsü", $"{screen.Title} · yazısı çıkarıldı, görüntü kaydedilmedi");
        return await ReadImageTextAsync(screen.Jpeg, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Görseldeki yazıyı yerel görme modeliyle aynen çıkarıp panoya koyar.</summary>
    private async Task<QuickReply> ReadImageTextAsync(byte[] jpeg, CancellationToken cancellationToken)
    {
        var answer = await chat.CompleteAsync(
            Settings.VisionModel,
            "Görseldeki bütün yazıyı olduğu gibi, satır düzenini koruyarak çıkar. Yorum, açıklama ya da başlık ekleme; yalnızca yazıyı ver. Yazı yoksa 'Yazı yok' de.",
            "Görseldeki yazıyı çıkar.",
            [jpeg],
            "0",
            cancellationToken).ConfigureAwait(false);
        var text = answer.Trim();
        if (text.Length == 0 || text.StartsWith("Yazı yok", StringComparison.OrdinalIgnoreCase))
        {
            return new QuickReply("Görselde okunacak bir yazı bulamadım.");
        }

        ClipboardText.Set(text);
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new QuickReply($"Yazıyı ({words.Length} sözcük) panoya koydum; istediğin yere yapıştırabilirsin. Başı şöyle: {string.Join(' ', words.Take(12))}" + (words.Length > 12 ? "…" : "."));
    }

    private QuickReply? SaveClipboard(string action, string listName)
    {
        var text = ClipboardText.Get().Trim();
        if (text.Length == 0)
        {
            return new QuickReply("Panoda kaydedilecek bir metin yok.");
        }

        switch (Simplify(action))
        {
            case "is":
                var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                return TodoCommand("ekle", line.Length > 140 ? line[..140] : line);
            case "liste":
                return new QuickReply(ListCommand("ekle", listName, text.Length > 200 ? text[..200] : text));
            default:
                notes.Add(text.Length > 4000 ? text[..4000] : text, DateTime.Now);
                ledger.Record(DataFlow.Local, "Not", $"Panodaki metin nota eklendi ({text.Length} karakter)");
                return new QuickReply($"Panodaki metni notlarına ekledim ({text.Length} karakter).");
        }
    }
}
