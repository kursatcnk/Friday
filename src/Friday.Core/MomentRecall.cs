namespace Friday.Core;

/// <summary>Kullanıcının bir kaydı: nereden geldiği ("Hafıza", "Not", "Günlük", "İş"), metni ve zamanı.</summary>
public sealed record PersonalRecord(string Source, string Text, DateTime When);

/// <summary>
/// Doğru anda hafıza: bir hatırlatma çaldığında ya da bir işe odaklanıldığında kullanıcının kayıtlarından (hafıza,
/// notlar, günlük, açık işler) o anla gerçekten ilgili tek bir satır bulur. "Ahmet'i ara" çalınca "Ahmet'e bütçe
/// tablosunu göndereceğini söylemiştin" gibi. Gürültü olmasın diye ya bir kişi ya da yer adı (kayıtlarda büyük harfle
/// geçen) ya da birden çok nadir sözcük tutmalıdır; "ara", "gönder" gibi fiiller ve yaygın sözcükler tek başına yetmez.
/// </summary>
public static class MomentRecall
{
    /// <summary>Anın konusunda sayılmayan sözcükler: eylemler ve zaman sözleri, sadeleşmiş biçimde.</summary>
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal)
    {
        "ara", "aramak", "aramayi", "gonder", "gondermek", "hatirla", "hatirlat", "unutma", "bitir", "bak", "oku", "yaz", "sor",
        "ode", "odemek", "git", "gel", "getir", "yap", "yapmak", "kontrol", "et", "ver", "konus", "bul", "hazirla", "toplanti",
        "yarin", "bugun", "sabah", "aksam", "ogle", "gece", "saat", "dakika", "hafta", "zamani", "geldi", "lazim", "gerek", "icin"
    };

    /// <summary>Konuyla en ilgili kayıt; ayırt edici bir eşleşme yoksa null.</summary>
    public static PersonalRecord? Related(string subject, IReadOnlyList<PersonalRecord> records)
    {
        var asked = MemoryRecall.Words(subject).Where(word => !Actions.Contains(word)).ToList();
        if (asked.Count == 0 || records.Count == 0)
        {
            return null;
        }

        // Konunun kendisi (aynı işin kaydı) ilgili sayılmaz.
        var candidates = records
            .Select(record => (Record: record, Words: MemoryRecall.Words(record.Text)))
            .Where(item => !item.Words.All(word => asked.Any(other => MemoryRecall.Same(word, other)) || Actions.Contains(word)))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        // Kayıtlarda büyük harfle geçen adlar: konudaki "ahmet" sözcüğü bunlardan biriyse ayırt edicidir.
        var names = records.Select(record => record.Text).Append(subject).SelectMany(HearingVocabulary.ProperNames)
            .SelectMany(name => MemoryRecall.Words(name))
            .ToHashSet(StringComparer.Ordinal);
        var weights = asked.ToDictionary(word => word, word =>
        {
            var count = candidates.Count(item => item.Words.Any(other => MemoryRecall.Same(word, other)));
            return count == 0 ? 0 : Math.Log(1 + (candidates.Count / (double)count));
        });

        var best = candidates
            .Select(item =>
            {
                var matched = asked.Where(word => item.Words.Any(other => MemoryRecall.Same(word, other))).ToList();
                var named = matched.Any(word => names.Any(name => MemoryRecall.Same(word, name)));
                var score = matched.Sum(word => weights[word]);
                return (item.Record, Score: score, Distinct: named || (matched.Count >= 2 && score >= 2.0));
            })
            .Where(item => item.Distinct)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Record.When)
            .FirstOrDefault();
        return best.Record;
    }
}
