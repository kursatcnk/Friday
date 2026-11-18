using System.Globalization;
using System.Text;

namespace Friday.Core;

/// <summary>Takvimdeki bir etkinlik (yerel saatle).</summary>
public sealed record CalendarEvent(string Title, DateTime Start, DateTime End, bool AllDay, string? Location);

/// <summary>
/// iCalendar (.ics) okuyucu: Google Takvim ya da Outlook'un "gizli iCal adresi"nden gelen etkinlikleri yerel saate
/// çevirir. Tekrarlayan etkinlikler (günlük, haftalık, aylık, yıllık; INTERVAL, COUNT, UNTIL, BYDAY) ve iptal edilen
/// tekrarlar (EXDATE) desteklenir. Dış kütüphane kullanılmaz.
/// </summary>
public static class Calendar
{
    private const int MaxOccurrences = 2000;

    /// <summary>[from, to) aralığına düşen etkinlikler, başlangıca göre sıralı.</summary>
    public static IReadOnlyList<CalendarEvent> Parse(string ics, DateTime from, DateTime to)
    {
        var events = new List<CalendarEvent>();
        Dictionary<string, (string Parameters, string Value)>? current = null;
        var exceptions = new List<DateTime>();
        foreach (var line in Unfold(ics))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
                exceptions = [];
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null)
                {
                    Expand(current, exceptions, from, to, events);
                }

                current = null;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var head = line[..colon];
            var value = line[(colon + 1)..];
            var semicolon = head.IndexOf(';', StringComparison.Ordinal);
            var name = semicolon < 0 ? head : head[..semicolon];
            var parameters = semicolon < 0 ? "" : head[(semicolon + 1)..];
            if (name.Equals("EXDATE", StringComparison.OrdinalIgnoreCase))
            {
                exceptions.AddRange(value.Split(',').Select(part => ParseTime(parameters, part)).OfType<DateTime>());
            }
            else
            {
                current[name] = (parameters, value);
            }
        }

        return [.. events.OrderBy(item => item.Start)];
    }

    private static void Expand(Dictionary<string, (string Parameters, string Value)> fields, List<DateTime> exceptions,
        DateTime from, DateTime to, List<CalendarEvent> events)
    {
        if (!fields.TryGetValue("DTSTART", out var startField) || ParseTime(startField.Parameters, startField.Value) is not { } start)
        {
            return;
        }

        if (fields.TryGetValue("STATUS", out var status) && status.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var allDay = startField.Parameters.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) || startField.Value.Trim().Length == 8;
        var end = fields.TryGetValue("DTEND", out var endField) && ParseTime(endField.Parameters, endField.Value) is { } parsedEnd
            ? parsedEnd
            : allDay ? start.AddDays(1) : start.AddHours(1);
        var length = end - start;
        var title = Unescape(fields.TryGetValue("SUMMARY", out var summary) ? summary.Value : "Etkinlik");
        var location = fields.TryGetValue("LOCATION", out var place) && place.Value.Trim().Length > 0 ? Unescape(place.Value) : null;

        void Add(DateTime occurrence)
        {
            if (occurrence + length > from && occurrence < to && !exceptions.Contains(occurrence))
            {
                events.Add(new CalendarEvent(title, occurrence, occurrence + length, allDay, location));
            }
        }

        if (!fields.TryGetValue("RRULE", out var ruleField))
        {
            Add(start);
            return;
        }

        var rule = ruleField.Value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].ToUpperInvariant(), pair => pair[1], StringComparer.OrdinalIgnoreCase);
        var frequency = rule.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        var interval = int.TryParse(rule.GetValueOrDefault("INTERVAL"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var step) ? Math.Max(1, step) : 1;
        var count = int.TryParse(rule.GetValueOrDefault("COUNT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ? total : int.MaxValue;
        var until = rule.TryGetValue("UNTIL", out var untilText) ? ParseTime("", untilText) : null;
        var days = rule.TryGetValue("BYDAY", out var byDay)
            ? byDay.Split(',').Select(day => DayOf(day.Trim()[^2..])).OfType<DayOfWeek>().ToHashSet()
            : [];

        // Yıllardır süren bir tekrarda baştan saymak yerine aralığın hemen öncesine atlanır (COUNT yoksa sayım gerekmez).
        var first = 0;
        if (count == int.MaxValue && from > start)
        {
            var gap = from - start;
            first = Math.Max(0, frequency switch
            {
                "DAILY" => (int)(gap.TotalDays / interval),
                "WEEKLY" => (int)(gap.TotalDays / (7 * interval)),
                "MONTHLY" => ((from.Year - start.Year) * 12 + from.Month - start.Month) / interval,
                "YEARLY" => (from.Year - start.Year) / interval,
                _ => 0
            } - 1);
        }

        var produced = 0;
        for (var index = first; index < first + MaxOccurrences && produced < count; index++)
        {
            var periodStart = frequency switch
            {
                "DAILY" => start.AddDays(index * interval),
                "WEEKLY" => start.AddDays(7 * index * interval),
                "MONTHLY" => start.AddMonths(index * interval),
                "YEARLY" => start.AddYears(index * interval),
                _ => DateTime.MaxValue
            };
            if (periodStart == DateTime.MaxValue || periodStart >= to.AddDays(7) || (until is { } last && periodStart > last.AddDays(7)))
            {
                break;
            }

            // Haftalık kuralda BYDAY varsa haftanın o günlerine yayılır.
            var monday = periodStart.Date.AddDays(-(((int)periodStart.DayOfWeek + 6) % 7));
            var candidates = frequency == "WEEKLY" && days.Count > 0
                ? Enumerable.Range(0, 7).Select(offset => monday.AddDays(offset))
                    .Where(day => days.Contains(day.DayOfWeek))
                    .Select(day => day.Date + start.TimeOfDay)
                : [periodStart];
            foreach (var occurrence in candidates.Where(candidate => candidate >= start).OrderBy(candidate => candidate))
            {
                if ((until is { } limit && occurrence > limit) || produced >= count)
                {
                    break;
                }

                produced++;
                Add(occurrence);
            }
        }
    }

    private static DayOfWeek? DayOf(string code) => code.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday,
        _ => null
    };

    /// <summary>"20261118T090000Z" (UTC), "TZID=Europe/Istanbul:20261118T090000" ya da "VALUE=DATE:20261118" → yerel saat.</summary>
    public static DateTime? ParseTime(string parameters, string value)
    {
        var text = value.Trim();
        if (text.Length == 8 && DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        var utc = text.EndsWith('Z');
        if (!DateTime.TryParseExact(text.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return null;
        }

        if (utc)
        {
            return DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime();
        }

        var zoneId = parameters.Split(';').Select(part => part.Split('=', 2)).FirstOrDefault(pair => pair[0].Equals("TZID", StringComparison.OrdinalIgnoreCase))?[1].Trim('"');
        if (zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone) && zone.Id != TimeZoneInfo.Local.Id)
        {
            return TimeZoneInfo.ConvertTime(time, zone, TimeZoneInfo.Local);
        }

        return time;
    }

    /// <summary>"Bugün 10:00 Haftalık toplantı", "yarın tüm gün Doğum günü".</summary>
    public static string Describe(CalendarEvent item, DateTime now) =>
        item.AllDay
            ? $"{DayName(item.Start.Date, now)} tüm gün"
            : TurkishTime.Describe(item.Start, now);

    private static string DayName(DateTime day, DateTime now) => (day - now.Date).Days switch
    {
        0 => "bugün",
        1 => "yarın",
        _ => day.ToString("dddd", CultureInfo.GetCultureInfo("tr-TR"))
    };

    /// <summary>75 karakteri aşan satırlar boşlukla başlayan satırlarla devam eder (RFC 5545).</summary>
    private static IEnumerable<string> Unfold(string ics)
    {
        var current = new StringBuilder();
        foreach (var raw in ics.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                current.Append(raw.AsSpan(1));
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }

            current.Clear().Append(raw.TrimEnd('\r'));
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static string Unescape(string value) => value
        .Replace("\\n", " ", StringComparison.OrdinalIgnoreCase)
        .Replace("\\,", ",", StringComparison.Ordinal)
        .Replace("\\;", ";", StringComparison.Ordinal)
        .Replace("\\\\", "\\", StringComparison.Ordinal)
        .Trim();
}
