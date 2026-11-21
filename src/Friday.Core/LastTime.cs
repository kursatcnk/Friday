namespace Friday.Core;

/// <summary>
/// "En son ne zaman koşuya gittim?": kullanıcının kayıtlarında (alışkanlık işaretleri, harcamalar, biten işler,
/// günlük, notlar) konuyla eşleşen en yeni iz. Eşleşme Türkçe eklere dayanıklıdır ("koşuya" ↔ "koşu", "marketten" ↔
/// "market"); "gittim", "yaptım" gibi fiiller aranmaz.
/// </summary>
public static class LastTime
{
    /// <summary>Konunun sayılmayan sözcükleri: geçmiş zaman fiilleri ve dolgular (sadeleşmiş).</summary>
    private static readonly HashSet<string> Verbs = new(StringComparer.Ordinal)
    {
        "gittim", "gitmistim", "yaptim", "yapmistim", "aldim", "almistim", "aradim", "aramistim", "gordum", "gormustum",
        "konustum", "yedim", "ictim", "odedim", "bitirdim", "okudum", "izledim", "attim", "kostum", "yuruttum", "yurudum",
        "ettim", "oldu", "oldum", "geldim", "ugradim", "harcadim", "kullandim", "bastim", "yazdim", "dinledim",
        "zaman", "son", "kez", "defa"
    };

    public sealed record Trace(string Source, string Text, DateTime When);

    /// <summary>Konuyla eşleşen en yeni kayıt; yoksa null.</summary>
    public static Trace? Find(string subject, IEnumerable<Trace> traces)
    {
        var asked = MemoryRecall.Words(subject).Where(word => !Verbs.Contains(word)).ToList();
        if (asked.Count == 0)
        {
            return null;
        }

        // Sözcüklerin en az yarısı tutmalı ("market alışverişi" → "Migros (Market)"); tutanlar arasından en yenisi.
        var needed = Math.Max(1, (asked.Count + 1) / 2);
        return traces
            .Where(trace => MemoryRecall.Words(trace.Text) is var words && asked.Count(word => words.Any(other => MemoryRecall.Same(word, other))) >= needed)
            .OrderByDescending(trace => trace.When)
            .FirstOrDefault();
    }

    /// <summary>"En son 3 gün önce, Salı günü (18 Kasım): koşu alışkanlığını işaretlemiştin."</summary>
    public static string Tell(string subject, Trace? trace, DateTime now)
    {
        if (trace is null)
        {
            return $"\"{subject.Trim()}\" ile ilgili hiçbir kaydımda bir iz yok: alışkanlıklara, harcamalara, biten işlere, günlüğe ve notlara baktım.";
        }

        var today = DateOnly.FromDateTime(now);
        var day = DateOnly.FromDateTime(trace.When);
        var back = today.DayNumber - day.DayNumber;
        var when = back switch
        {
            0 => "Bugün",
            1 => "Dün",
            < 7 => $"{back} gün önce, {DayStory.Label(day, today)}",
            < 60 => $"{back} gün önce, {DayStory.Label(day, today)}",
            _ => DayStory.Label(day, today)
        };
        var what = trace.Source switch
        {
            "Alışkanlık" => $"\"{trace.Text.ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))}\" alışkanlığını işaretlemiştin",
            "Harcama" => $"harcama kaydın var: {trace.Text}",
            "İş" => $"\"{trace.Text}\" işini bitirmiştin",
            "Günlük" => $"günlüğüne yazmıştın: {Short(trace.Text)}",
            "Not" => $"not almıştın: {Short(trace.Text)}",
            _ => Short(trace.Text)
        };
        return $"En son {Lower(when)}: {what}.";
    }

    private static string Lower(string text) => text.Length > 0 && char.IsUpper(text[0]) && !char.IsDigit(text[0])
        ? char.ToLower(text[0], System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + text[1..]
        : text;

    private static string Short(string text)
    {
        var plain = text.Trim().TrimEnd('.');
        return plain.Length > 90 ? plain[..90].TrimEnd() + "…" : plain;
    }
}
