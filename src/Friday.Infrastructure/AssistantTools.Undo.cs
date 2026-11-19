using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>"Geri al", "geri getir", "son değişiklikler": Friday'in kayıtlarda yaptığı değişikliklerin sesli kontrolü.</summary>
public sealed partial class AssistantTools
{
    /// <summary>Friday'in kayıt değişiklikleri; her istek FridayAssistant'ta <see cref="UndoLog.Begin"/> ile açılır.</summary>
    public UndoLog Changes { get; } = new();

    private QuickReply UndoCommand(string action) => new(action switch
    {
        "geri_getir" => RedoLast(),
        "liste" => RecentChanges(),
        _ => UndoLast()
    });

    private string UndoLast()
    {
        if (Changes.Undo() is not { } outcome)
        {
            return "Son altı saatte kayıtlarında yaptığım bir değişiklik yok. Açtığım uygulama, ses ya da pencere gibi işleri geri alamıyorum.";
        }

        if (outcome.Restored.Count == 0)
        {
            return $"{Quote(outcome.Entry.Request)} isteğinden sonra {TurkishGrammar.Join(outcome.Skipped)} başka bir yoldan değişmiş; sonraki işini bozmamak için dokunmadım.";
        }

        return $"{Quote(outcome.Entry.Request)} dediğinde yaptığımı geri aldım; {TurkishGrammar.Join(outcome.Restored)} eski haline döndü."
            + Skipped(outcome);
    }

    private string RedoLast()
    {
        if (Changes.Redo() is not { } outcome)
        {
            return "Geri getirilecek bir şey yok.";
        }

        if (outcome.Restored.Count == 0)
        {
            return $"{TurkishGrammar.Capitalize(TurkishGrammar.Join(outcome.Skipped))} bu arada değişmiş; {Quote(outcome.Entry.Request)} isteğini geri getirmedim.";
        }

        return $"{Quote(outcome.Entry.Request)} isteğini geri getirdim; {TurkishGrammar.Join(outcome.Restored)} yine güncel." + Skipped(outcome);
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
            .Select(entry => $"{entry.At.ToString("HH:mm", CultureInfo.InvariantCulture)}'de {Quote(entry.Request)} ({TurkishGrammar.Join(entry.Titles)})");
        var more = entries.Count > 3 ? $" Toplam {entries.Count} değişiklik var." : "";
        return $"Son değişikliklerim: {string.Join("; ", lines)}.{more} Sonuncuyu geri almak için \"geri al\" de.";
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
