using System.Globalization;

namespace Friday.Core;

/// <summary>Biten bir odak oturumu: ne zaman başladı, kaç dakika odaklanıldı (duraklatılan kısım hariç), hangi iş için (yoksa boş).</summary>
public sealed record FocusSession(DateTime Start, double Minutes, string Task);

/// <summary>
/// Odak oturumlarının işlere göre dökümü: "bu hafta rapora ne kadar çalıştım", "bugün neye odaklandım". Yalnızca kullanıcının
/// odak başlatırken söylediği iş adı tutulur; pencere ya da içerik tutulmaz.
/// </summary>
public static class FocusLog
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>İşlere göre toplam dakika ve oturum sayısı, en çok olan başta; işi belirtilmeyenler en sonda boş adla.</summary>
    public static IReadOnlyList<(string Task, double Minutes, int Count)> ByTask(IEnumerable<FocusSession> sessions) =>
        [.. sessions.GroupBy(session => session.Task.Trim().ToLower(Turkish))
            .Select(group => (Task: group.OrderByDescending(item => item.Start).First().Task.Trim(), Minutes: group.Sum(item => item.Minutes), Count: group.Count()))
            .OrderBy(entry => entry.Task.Length == 0)
            .ThenByDescending(entry => entry.Minutes)];

    /// <summary>Sorulan işe uyan oturumlar ("rapora" → "Raporu bitir", "rapor"); sözcükler kök eşleşmesiyle karşılaştırılır.</summary>
    public static IReadOnlyList<FocusSession> Matching(IEnumerable<FocusSession> sessions, string query)
    {
        var asked = Words(query);
        return asked.Count == 0
            ? []
            : [.. sessions.Where(session => Words(session.Task) is { Count: > 0 } words
                && asked.Count(word => words.Any(other => MemoryRecall.Same(word, other))) * 2 >= asked.Count)];
    }

    /// <summary>"Bu hafta rapora 2 sa 5 dk odaklandın, 5 oturumda." ya da işlere göre döküm.</summary>
    /// <param name="period">Cümle başı: "Bugün", "Dün", "Bu hafta"…</param>
    public static string Describe(IReadOnlyList<FocusSession> sessions, string query, string period)
    {
        if (query.Trim().Length > 0)
        {
            var found = Matching(sessions, query);
            if (found.Count == 0)
            {
                return $"{period} \"{query.Trim()}\" için bir odak oturumu yok. Odağı başlatırken işi söylersen (\"rapora 50 dakika odaklan\") burada sayarım.";
            }

            var total = found.Sum(session => session.Minutes);
            return $"{period} \"{found.OrderByDescending(session => session.Start).First().Task}\" için {RhythmText.Compact(total)} odaklandın" + (found.Count > 1 ? $", {found.Count} oturumda." : ".");
        }

        if (sessions.Count == 0)
        {
            return $"{period} hiç odak oturumu yok.";
        }

        var groups = ByTask(sessions);
        var all = RhythmText.Compact(sessions.Sum(session => session.Minutes));
        var named = groups.Where(group => group.Task.Length > 0).Take(4).Select(group => $"{group.Task.ToLower(Turkish)} {RhythmText.Compact(group.Minutes)}").ToList();
        var unnamed = groups.FirstOrDefault(group => group.Task.Length == 0);
        if (unnamed.Count > 0)
        {
            named.Add($"iş belirtmeden {RhythmText.Compact(unnamed.Minutes)}");
        }

        return $"{period} {sessions.Count} oturumda toplam {all} odaklandın"
            + (groups.Any(group => group.Task.Length > 0) ? ": " + string.Join(", ", named) + "." : ".");
    }

    private static List<string> Words(string text) =>
        [.. text.ToLower(Turkish).Split([' ', ',', '.', '\'', '’', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2 && word is not ("için" or "ile" or "işi" or "işine" or "işini" or "üzerine"))];
}
