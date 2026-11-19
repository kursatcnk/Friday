using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Metin kısaltmaları ve gün sonu günlük sorusu (serbest cevap bekleyen soru).</summary>
public sealed partial class AssistantTools
{
    private static readonly TimeSpan AnswerLife = TimeSpan.FromMinutes(3);

    private Func<string, QuickReply>? _awaitingAnswer;

    /// <summary>Soru "hayır", "pas" gibi cevapları kendisi değerlendirir (kelime pratiği).</summary>
    private bool _awaitingOwnsDecline;
    private DateTime _awaitingAt;
    private DateOnly _journalAsked;

    /// <summary>Bir soru serbest bir cevap bekliyor mu ("Günün nasıl geçti?"); bir sonraki söz ona gider.</summary>
    public bool AwaitsAnswer => _awaitingAnswer is not null && DateTime.Now - _awaitingAt < AnswerLife;

    // ---------- Kısaltmalar ----------

    /// <returns>Cevap; "X'i yaz" için öyle bir kısaltma yoksa null (soru dil modeline gider).</returns>
    private QuickReply? SnippetCommand(string action, string name, string text)
    {
        var key = name.Trim().ToLower(Turkish);
        switch (Simplify(action))
        {
            case "ekle" or "panodan":
                var value = Simplify(action) == "panodan" ? ClipboardText.Get().Trim() : text.Trim();
                if (key.Length < 2 || value.Length == 0)
                {
                    return new QuickReply(Simplify(action) == "panodan" ? "Panoda kaydedilecek bir metin yok." : "Kısaltmanın adını ve metnini anlayamadım; ör. \"imza kısaltması: Saygılarımla\".");
                }

                Settings.Snippets[key] = value;
                SaveSettings?.Invoke(Settings);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                ledger.Record(DataFlow.Local, "Kısaltma", $"\"{key}\" kaydedildi ({value.Length} karakter)");
                return new QuickReply($"Tamam, \"{key}\" kısaltmasını kaydettim. \"{TurkishGrammar.Capitalize(key)} yaz\" ya da \"{Possessive(key)} yaz\" deyince açık pencereye yazarım.");
            case "sil":
                if (FindSnippet(key) is not { } found)
                {
                    return new QuickReply($"\"{name}\" diye bir kısaltma yok.");
                }

                Settings.Snippets.Remove(found);
                SaveSettings?.Invoke(Settings);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                return new QuickReply($"\"{found}\" kısaltmasını sildim.");
            case "kullan":
                if (FindSnippet(key) is not { } snippet)
                {
                    return null;
                }

                TypeText(Settings.Snippets[snippet]);
                return new QuickReply($"{TurkishGrammar.Capitalize(snippet)} yazıldı.", Silent: true);
            default:
                if (Settings.Snippets.Count == 0)
                {
                    return new QuickReply("Henüz kısaltma yok. \"İmza kısaltması: Saygılarımla, Ad Soyad\" de ya da bir metni kopyalayıp \"panodakini adres olarak kaydet\" de.");
                }

                ShowListCard("Kısaltmalar", "", Settings.Snippets.Take(MaxCardRows).Select(pair => new CardRow(pair.Key, Preview(pair.Value))));
                return new QuickReply($"{Settings.Snippets.Count} kısaltman var: {TurkishGrammar.Join([.. Settings.Snippets.Keys.Take(6)])}.");
        }
    }

    /// <summary>"imzamı" → "imza", "adresimi" → "adres": söylenen ad ekleriyle de bulunur.</summary>
    private string? FindSnippet(string spoken)
    {
        var wanted = Simplify(spoken);
        if (wanted.Length == 0)
        {
            return null;
        }

        return Settings.Snippets.Keys.FirstOrDefault(key => Simplify(key) == wanted)
            ?? Settings.Snippets.Keys
                .Where(key => Simplify(key).Length >= 3 && wanted.StartsWith(Simplify(key), StringComparison.Ordinal) && wanted.Length - Simplify(key).Length <= 3)
                .OrderByDescending(key => key.Length)
                .FirstOrDefault();
    }

    /// <summary>"imza" → "imzamı", "adres" → "adresimi" (sesli söyleme örneği).</summary>
    private static string Possessive(string name)
    {
        var last = name.LastOrDefault(letter => "aeıioöuü".Contains(letter, StringComparison.Ordinal));
        var vowel = last switch { 'a' or 'ı' => 'ı', 'e' or 'i' => 'i', 'o' or 'u' => 'u', _ => 'ü' };
        return "aeıioöuü".Contains(name[^1], StringComparison.Ordinal) ? $"{name}m{vowel}" : $"{name}{vowel}m{vowel}";
    }

    private static string Preview(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > 40 ? flat[..40] + "…" : flat;
    }

    // ---------- Gün sonu günlük sorusu ----------

    /// <summary>
    /// Akşam bir kez sorulur: "Günün nasıl geçti? Bir iki cümleyle anlatırsan günlüğüne yazarım." Cevap günlüğe
    /// yazılır; "şimdi değil" denirse geçilir. Bugün sorulduysa ya da cevap bekleyen başka bir soru varsa null.
    /// </summary>
    public string? JournalQuestion(DateTime now, bool force = false)
    {
        var today = DateOnly.FromDateTime(now);
        if (Journal is null || HasPendingConfirmation || AwaitsAnswer || (!force && (_journalAsked == today || !Settings.JournalPrompt)))
        {
            return null;
        }

        if (!force && Journal.Between(now.Date, now).Any(entry => entry.Text.StartsWith("Gün sonu:", StringComparison.Ordinal)))
        {
            return null;
        }

        _journalAsked = today;
        _awaitingOwnsDecline = false;
        _awaitingAnswer = AnswerJournal;
        _awaitingAt = DateTime.Now;
        var question = "Günün nasıl geçti? Bir iki cümleyle anlatırsan günlüğüne yazarım.";
        if (HabitBook is { } book && book.Pending(today) is { Count: > 0 and <= 3 } pending)
        {
            question += $" Bir de: bugün {TurkishGrammar.Join([.. pending.Select(habit => habit.Name.ToLower(Turkish))])} işaretlenmedi; yaptıysan onu da söyle.";
        }

        return question;
    }

    /// <summary>Serbest cevap bekleyen soruya gelen söz.</summary>
    private QuickReply AnswerPending(string text)
    {
        var handler = _awaitingAnswer;
        _awaitingAnswer = null;
        if (handler is null)
        {
            return new QuickReply("Bir şey sormamıştım.");
        }

        return !_awaitingOwnsDecline && (QuickIntents.IsNo(text) || QuickIntents.IsDecline(text))
            ? new QuickReply("Peki, başka zaman.")
            : handler(text);
    }

    private QuickReply AnswerJournal(string text)
    {
        var now = DateTime.Now;
        var clean = text.Trim();
        Journal?.Add(now, "Gün sonu: " + clean);
        ledger.Record(DataFlow.Local, "Günlük", "Gün sonu cevabı yazıldı");

        // Cevapta alışkanlık geçiyorsa ("spor da yaptım") işaretlenir.
        var marked = new List<string>();
        if (HabitBook is { } book)
        {
            var today = DateOnly.FromDateTime(now);
            foreach (var habit in book.Pending(today))
            {
                if (book.Find(clean) is { } match && match.Name == habit.Name && book.Mark(habit.Name, today))
                {
                    marked.Add(habit.Name.ToLower(Turkish));
                }
            }
        }

        var address = Settings.AddressAs.Trim();
        return new QuickReply("Günlüğüne yazdım." + (marked.Count > 0 ? $" {TurkishGrammar.Capitalize(TurkishGrammar.Join(marked))} da işaretlendi." : "")
            + (now.Hour >= 21 || now.Hour < 4 ? " İyi geceler" : " Teşekkürler") + (address.Length > 0 ? $", {address}." : "."));
    }
}
