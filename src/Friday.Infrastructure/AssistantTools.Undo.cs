using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>"Geri al", "geri getir", "son değişiklikler": Friday'in kayıtlarda yaptığı değişikliklerin sesli kontrolü.</summary>
public sealed partial class AssistantTools
{
    /// <summary>Friday'in kayıt değişiklikleri; her istek FridayAssistant'ta <see cref="UndoLog.Begin"/> ile açılır.</summary>
    public UndoLog Changes { get; } = new();

    /// <summary>Hızlı yolun yanlış anladığı ve artık dil modeline giden sözler.</summary>
    public CorrectionBook? Corrections { get; set; }

    private QuickReply CorrectionCommand(string action)
    {
        if (Corrections is not { } book)
        {
            return new QuickReply("Düzeltme defteri açık değil.");
        }

        if (action == "sifirla")
        {
            var count = book.Clear();
            return new QuickReply(count == 0
                ? "Öğrendiğim bir düzeltme yok."
                : $"{count} düzeltmeyi unuttum; o sözler yine hızlı komut olarak çalışacak.");
        }

        var phrases = book.Phrases;
        return new QuickReply(phrases.Count == 0
            ? "Henüz bir düzeltme öğrenmedim. Bir komutu yanlış anlarsam \"yanlış anladın\" de; geri alır, bir daha o sözü komut sanmam."
            : $"Komut sanmamayı öğrendiğim {phrases.Count} söz var: " + string.Join(", ", phrases.TakeLast(5).Reverse().Select(phrase => $"\"{phrase}\"")) + ".");
    }

    private QuickReply UndoCommand(string action) => new(action switch
    {
        "geri_getir" => RedoLast(),
        "liste" => RecentChanges(),
        _ => UndoLast()
    });


    /// <summary>Panelde Ctrl+Z / Ctrl+Y; sonuç durum satırında gösterilir.</summary>
    public string UndoFromPanel(bool redo) => redo ? RedoLast() : UndoLast();

    private string UndoLast()
    {
        if (Changes.Undo() is not { } outcome)
        {
            return "Son altı saatte kayıtlarında yaptığım bir değişiklik yok. Açtığım uygulama, ses ya da pencere gibi işleri geri alamıyorum.";
        }

        var source = Source(outcome.Entry);
        if (outcome.Restored.Count == 0)
        {
            return $"{source.After} {TurkishGrammar.Join(outcome.Skipped)} başka bir yoldan değişmiş; sonraki işini bozmamak için dokunmadım.";
        }

        return $"{source.Undone} geri aldım; {TurkishGrammar.Join(outcome.Restored)} eski haline döndü." + Skipped(outcome);
    }

    private string RedoLast()
    {
        if (Changes.Redo() is not { } outcome)
        {
            return "Geri getirilecek bir şey yok.";
        }

        var source = Source(outcome.Entry);
        if (outcome.Restored.Count == 0)
        {
            return $"{TurkishGrammar.Capitalize(TurkishGrammar.Join(outcome.Skipped))} bu arada değişmiş; {source.Redone} geri getirmedim.";
        }

        return $"{source.Redone} geri getirdim; {TurkishGrammar.Join(outcome.Restored)} yine güncel." + Skipped(outcome);
    }

    private string RecentChanges()
    {
        var entries = Changes.Entries;
        if (entries.Count == 0)
        {
            return "Son altı saatte kayıtlarında bir değişiklik yapmadım.";
        }

        var lines = entries
            .Reverse()
            .Take(3)
            .Select(entry => $"{entry.At.ToString("HH:mm", CultureInfo.InvariantCulture)} {Source(entry).Listed} ({TurkishGrammar.Join(entry.Titles)})");
        var more = entries.Count > 3 ? $" Toplam {entries.Count} değişiklik var." : "";
        return $"Son değişikliklerim: {string.Join("; ", lines)}.{more} Sonuncuyu geri almak için \"geri al\" de.";
    }

    /// <summary>Değişikliğin nereden geldiğine göre cümle parçaları: sesli istek ya da paneldeki bir tıklama.</summary>
    private static (string Undone, string Redone, string After, string Listed) Source(UndoEntry entry)
    {
        if (entry.Request.StartsWith(UndoLog.PanelPrefix, StringComparison.Ordinal))
        {
            var change = entry.Request[UndoLog.PanelPrefix.Length..];
            return ($"Paneldeki değişikliği ({change})", $"Paneldeki değişikliği ({change})", $"Paneldeki değişiklikten ({change}) sonra", $"panelde {change}");
        }

        var quote = Quote(entry.Request);
        return ($"{quote} dediğinde yaptığımı", $"{quote} isteğini", $"{quote} isteğinden sonra", quote);
    }

    private static string Skipped(UndoOutcome outcome) =>
        outcome.Skipped.Count == 0 ? "" : $" {TurkishGrammar.Capitalize(TurkishGrammar.Join(outcome.Skipped))} bu arada başka bir yoldan değiştiği için ona dokunmadım.";

    /// <summary>İsteğin kısa alıntısı; uzun sorular kırpılır.</summary>
    private static string Quote(string request)
    {
        var plain = string.Join(' ', request.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.', '!', '?');
        return "\"" + (plain.Length > 60 ? plain[..60].TrimEnd() + "…" : plain) + "\"";
    }
}
