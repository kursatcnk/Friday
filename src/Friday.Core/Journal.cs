using System.Globalization;

namespace Friday.Core;

/// <summary>
/// Sesli günlük: "günlüğe yaz: bugün sunum çok iyi geçti". Ay ay Markdown dosyalarında durur
/// (data\\journal\\2026-11.md); Not Defteri'yle de okunup düzenlenebilir.
/// </summary>
public sealed class JournalStore(string folder)
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly object _gate = new();

    public void Add(DateTime when, string text)
    {
        var clean = text.Trim();
        if (clean.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            Directory.CreateDirectory(folder);
            System.IO.File.AppendAllText(PathFor(when), $"- {when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} · {TurkishGrammar.Capitalize(clean)}\n");
        }
    }

    /// <summary>Tarih aralığındaki yazılar, eskiden yeniye.</summary>
    public IReadOnlyList<(DateTime When, string Text)> Between(DateTime from, DateTime to)
    {
        var entries = new List<(DateTime, string)>();
        lock (_gate)
        {
            for (var month = new DateTime(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
            {
                if (!System.IO.File.Exists(PathFor(month)))
                {
                    continue;
                }

                foreach (var line in System.IO.File.ReadLines(PathFor(month)))
                {
                    if (line.Length > 21 && line.StartsWith("- ", StringComparison.Ordinal)
                        && DateTime.TryParseExact(line[2..18], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
                        && when >= from && when <= to)
                    {
                        entries.Add((when, line[21..].Trim()));
                    }
                }
            }
        }

        return entries;
    }

    /// <summary>"Bugün: 09:30 sunum iyi geçti; 18:10 …"</summary>
    public static string Describe(IReadOnlyList<(DateTime When, string Text)> entries, DateTime now)
    {
        if (entries.Count == 0)
        {
            return "Günlükte bu aralıkta bir yazı yok.";
        }

        return string.Join(" ", entries.GroupBy(entry => entry.When.Date).Select(day =>
            $"{(day.Key == now.Date ? "Bugün" : day.Key == now.Date.AddDays(-1) ? "Dün" : day.Key.ToString("d MMMM dddd", Turkish))}: "
            + string.Join("; ", day.Select(entry => $"{entry.When:HH:mm} {entry.Text.TrimEnd('.')}")) + "."));
    }

    private string PathFor(DateTime when) => Path.Combine(folder, when.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".md");
}
