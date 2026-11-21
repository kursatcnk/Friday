using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Doğru anda hafıza (bkz. <see cref="MomentRecall"/>): hatırlatma çalınca ya da bir işe odaklanınca ilgili tek satır.</summary>
public sealed partial class AssistantTools
{
    /// <summary>Konuyla gerçekten ilgili bir kayıt varsa söylenecek tek cümle; yoksa null.</summary>
    public string? MomentNote(string subject)
    {
        if (subject.Trim().Length < 3)
        {
            return null;
        }

        var now = DateTime.Now;
        var records = new List<PersonalRecord>();
        records.AddRange(memory.Items.Select(item => new PersonalRecord("Hafıza", item.Text, item.Created.LocalDateTime)));
        records.AddRange(notes.All.Where(note => now - note.Time < TimeSpan.FromDays(120)).Select(note => new PersonalRecord("Not", note.Text, note.Time)));
        if (Journal is { } journal)
        {
            records.AddRange(journal.Between(now.AddDays(-60), now).Select(entry => new PersonalRecord("Günlük", entry.Text, entry.When)));
        }

        if (Todos is { } todos)
        {
            records.AddRange(todos.Open.Select(item => new PersonalRecord("İş", item.Text, item.Created)));
        }

        if (MomentRecall.Related(subject, records) is not { } hit)
        {
            return null;
        }

        ledger.Record(DataFlow.Local, "Hafıza", "Hatırlatmaya ilgili bir kayıt eklendi");
        var text = Short(hit.Text.TrimEnd('.'), 150);
        return hit.Source switch
        {
            "Not" => $"{When(hit.When, now, "notunda")}: {text}.",
            "Günlük" => $"{When(hit.When, now, "günlüğünde")}: {text}.",
            "İş" => $"Bununla ilgili açık bir işin de var: {text}.",
            _ => $"Hafızamda: {TurkishGrammar.Capitalize(ThirdPerson().Replace(text, ""))}."
        };
    }

    /// <summary>Hafızaya "Kullanıcı Göktuğ'a … belirtti" diye yazılmış bilgiler kullanıcıya okunurken "Kullanıcı" sözü atılır.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^Kullanıcı(?:nın)?\s+")]
    private static partial System.Text.RegularExpressions.Regex ThirdPerson();

    /// <summary>"Dünkü notunda", "3 gün önceki notunda", "12 Kasım tarihli notunda".</summary>
    private static string When(DateTime when, DateTime now, string place)
    {
        var days = (now.Date - when.Date).Days;
        return days switch
        {
            0 => $"Bugünkü {place}",
            1 => $"Dünkü {place}",
            < 7 => $"{days} gün önceki {place}",
            _ => $"{when.ToString("d MMMM", CultureInfo.GetCultureInfo("tr-TR"))} tarihli {place}"
        };
    }
}
