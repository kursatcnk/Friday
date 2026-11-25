using System.Text.RegularExpressions;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Ajanda sayfasının ekleme kutusu: yazılan cümle hatırlatma ya da son günlü iş olur.</summary>
public sealed partial class AssistantTools
{
    /// <summary>
    /// "yarın 9'da dişçi", "her pazartesi 8'de spor" → hatırlatma (saat söylenmiş); "cuma sunumu bitir" → cuma son günlü
    /// iş; "faturayı öde" → iş. Açık bir komut ("yarın 9'da dişçiyi hatırlat") olduğu gibi çalışır. Ne yapıldığını anlatan
    /// cümle döner; dil modeline gidilmez.
    /// </summary>
    public async Task<string> AddToAgendaAsync(string text, CancellationToken cancellationToken)
    {
        var said = text.Trim();
        if (said.Length == 0)
        {
            return "";
        }

        var command = QuickIntents.Match(said);
        if (command is not { Tool: "hatirlatici" or "zamanlayici" or "kosullu_hatirlatici" or "yapilacak" or "yapilacaklar" } && ClockTime().IsMatch(said.ToLower(Turkish)))
        {
            command = QuickIntents.Match(said + " hatırlat") is { Tool: "hatirlatici" or "kosullu_hatirlatici" } reminder ? reminder : null;
        }

        if (command is { Tool: "hatirlatici" or "zamanlayici" or "kosullu_hatirlatici" or "yapilacak" or "yapilacaklar" }
            && await RunQuickAsync(command, cancellationToken).ConfigureAwait(false) is { } reply)
        {
            return reply.Text;
        }

        if (Todos is not { } todos)
        {
            return "Yapılacaklar listesi kapalı.";
        }

        var (task, due, important) = TodoDue.Extract(said, DateTime.Now);
        todos.Add(TurkishGrammar.Capitalize(task), DateTime.Now, due, important);
        return due is { } day
            ? $"\"{TurkishGrammar.Capitalize(task)}\" işi eklendi; son günü {TodoDue.Label(day, DateOnly.FromDateTime(DateTime.Now))}."
            : $"\"{TurkishGrammar.Capitalize(task)}\" yapılacaklara eklendi.";
    }

    /// <summary>Saat söylenmiş mi: "9'da", "14:30", "sabah", "akşam 7".</summary>
    [GeneratedRegex(@"\d{1,2}[:.]\d{2}|\d{1,2}['’]?(?:da|de|ta|te)\b|\b(?:sabah|öğlen|öğleden sonra|akşam|gece)\b|\bsaat\b|\bher (?:gün|sabah|akşam|pazartesi|salı|çarşamba|perşembe|cuma|cumartesi|pazar)")]
    private static partial Regex ClockTime();
}
