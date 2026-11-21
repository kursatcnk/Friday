using System.Globalization;

namespace Friday.Core;

/// <summary>"Nerede kalmıştık?" için kayıtlardaki son izler.</summary>
public sealed record ResumeFacts(DateTime Now)
{
    public (string Task, DateTime At)? Focus { get; init; }

    /// <summary>Son moladan önce öndeki pencere (ritim koçu).</summary>
    public string? BeforeBreak { get; init; }

    /// <summary>Son bir saatte en çok kullanılan uygulamalar (bu oturumda); boşsa dünkü en çoklar kullanılır.</summary>
    public IReadOnlyList<(string App, int Seconds)> RecentApps { get; init; } = [];

    /// <summary>Bugün (kayıtlı) en çok kullanılanlar; Friday yeniden açıldıysa son bir saatin yerine.</summary>
    public IReadOnlyList<(string App, int Seconds)> TodayApps { get; init; } = [];

    public IReadOnlyList<(string App, int Seconds)> YesterdayApps { get; init; } = [];

    public (string Title, DateTime At)? LastChat { get; init; }

    /// <summary>Bugün teslim ya da gecikmiş açık işler.</summary>
    public IReadOnlyList<string> Due { get; init; } = [];

    public (string Text, DateTime At)? NewestTodo { get; init; }

    public (string Text, DateTime At)? LastNote { get; init; }
}

/// <summary>
/// "Nerede kalmıştık?": aradan sonra dönen kullanıcıya en son neyle uğraştığını birkaç cümleyle hatırlatır: odaklandığı
/// iş, son bir saatte açık olan uygulamalar, benimle son konuşmasının konusu, bugün teslim olan işler, son notu. Yalnızca
/// yakın zamanın izleri sayılır (odak 12 saat, sohbet, iş ve not bir gün).
/// </summary>
public static class ResumeStory
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Tell(ResumeFacts facts)
    {
        var now = facts.Now;
        var sentences = new List<string>();
        if (facts.BeforeBreak is { Length: > 0 } window)
        {
            sentences.Add($"Son molandan önce {window} açıktı.");
        }

        if (facts.Focus is { } focus && now - focus.At < TimeSpan.FromHours(12))
        {
            sentences.Add($"En son \"{focus.Task.ToLower(Turkish)}\" işine odaklanıyordun.");
        }

        var apps = facts.RecentApps.Where(app => app.Seconds >= 120).Take(2).Select(app => app.App).ToList();
        if (apps.Count > 0)
        {
            sentences.Add($"Son bir saatte en çok {TurkishGrammar.Join(apps)} açıktı.");
        }
        else if (facts.TodayApps.Where(app => app.Seconds >= 300).Take(2).Select(app => app.App).ToList() is { Count: > 0 } today)
        {
            sentences.Add($"Bugün en çok {TurkishGrammar.Join(today)} kullandın.");
        }
        else if (facts.YesterdayApps.Where(app => app.Seconds >= 600).Take(2).Select(app => app.App).ToList() is { Count: > 0 } yesterday)
        {
            sentences.Add($"Dün en çok {TurkishGrammar.Join(yesterday)} kullanmıştın.");
        }

        if (facts.LastChat is { } chat && now - chat.At < TimeSpan.FromDays(1) && chat.Title.Trim().Length > 0)
        {
            sentences.Add($"Benimle en son \"{Short(chat.Title)}\" üzerine konuştun.");
        }

        if (facts.Due.Count > 0)
        {
            sentences.Add(facts.Due.Count == 1
                ? $"Bugün teslim olan bir işin var: {facts.Due[0].ToLower(Turkish)}."
                : $"Bugün teslim olan {facts.Due.Count} işin var: {string.Join(", ", facts.Due.Take(3).Select(item => item.ToLower(Turkish)))}.");
        }
        else if (facts.NewestTodo is { } todo && now - todo.At < TimeSpan.FromDays(1))
        {
            sentences.Add($"En son eklediğin iş: {todo.Text.ToLower(Turkish)}.");
        }

        if (facts.LastNote is { } note && now - note.At < TimeSpan.FromDays(1) && sentences.Count < 4)
        {
            sentences.Add($"Son notun: {Short(note.Text)}.");
        }

        return sentences.Count == 0
            ? "Kaldığın yeri gösterecek yakın bir kaydım yok: son bir günde odaklandığın bir iş, benimle bir konuşma ya da ekran kaydı görünmüyor."
            : string.Join(' ', sentences.Take(4));
    }

    private static string Short(string text)
    {
        var plain = text.Trim().TrimEnd('.');
        return plain.Length > 80 ? plain[..80].TrimEnd() + "…" : plain;
    }
}
