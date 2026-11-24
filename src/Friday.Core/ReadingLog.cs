using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Kullanıcının bilgisayarında okuduğu bir belge.</summary>
public sealed class ReadEntry
{
    /// <summary>Yolun özeti; metin dosyasının adı.</summary>
    public string Id { get; set; } = "";

    public string Path { get; set; } = "";

    public string Name { get; set; } = "";

    public DateTime FirstSeen { get; set; }

    public DateTime LastSeen { get; set; }

    /// <summary>Belgenin önde kaldığı toplam süre (saniye).</summary>
    public int Seconds { get; set; }

    /// <summary>Açıldığı günler (en yenileri).</summary>
    public List<DateOnly> Days { get; set; } = [];

    /// <summary>Saklanan metnin okunduğu andaki dosya değişme zamanı ve boyutu.</summary>
    public DateTime Modified { get; set; }

    public long Size { get; set; }

    /// <summary>Belge Türkçe değil (ör. İngilizce bir sözleşme).</summary>
    public bool Foreign { get; set; }

    /// <summary>Türkçe, 1-2 cümlelik özet (dil modeli boşta yazar).</summary>
    public string Gist { get; set; } = "";

    /// <summary>Türkçe ve belgenin kendi dilinde anahtar sözcükler: "kira" sorusu İngilizce "lease" belgesini bulur.</summary>
    public string Keywords { get; set; } = "";

    public int DigestTries { get; set; }

    /// <summary>Belgenin içeriği en son ne zaman değişti (önceki sürümün metni saklanır).</summary>
    public DateTime? Changed { get; set; }

    public double Minutes => Seconds / 60.0;
}

/// <summary>
/// Okuduklarım: kullanıcının açıp en az yarım dakika baktığı belgeler (pencere başlığından bulunan dosyalar). Her belgenin
/// metni yerelde saklanır (data\reading), dil modeli boştayken Türkçe bir özet ve iki dilli anahtar sözcükler yazar. Böylece
/// "geçen hafta okuduğum sözleşmede depozito ne kadardı", "kirayla ilgili hangi belgelere baktım", "bunun öncekinden farkı
/// ne" soruları cevaplanır. Ekran görüntüsü tutulmaz; yalnızca kullanıcının zaten açtığı dosyalar okunur.
/// </summary>
public sealed partial class ReadingLog
{
    /// <summary>Bir belge ancak bu kadar önde kalınca okunmuş sayılır (göz atıp geçilenler girmez).</summary>
    public const int MinSeconds = 30;

    /// <summary>Saklanan metnin üst sınırı (karakter).</summary>
    public const int TextLimit = 80_000;

    private const int MaxEntries = 400;
    private const int MaxDays = 30;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    // Soruda belgeyi değil okuma eylemini, zamanı ya da soruyu anlatan sözcükler aramada sayılmaz.
    private static readonly string[] Filler =
    [
        "okudu", "okuduğ", "okumuş", "açtığ", "açtım", "açmış", "baktı", "baktığ", "bakmış", "incele", "belge", "dosya", "geçen", "hafta",
        "haftaki", "dün", "bugün", "önceki", "kadar", "kaçtı", "neydi", "nelerdi", "yazıyor", "yazıyordu", "söyle", "hatırla", "mıydı",
        "miydi", "pazartesi", "salı", "çarşamba", "perşembe", "cuma", "cumartesi", "pazar", "ayki", "içinde", "orada", "şeyi", "neler",
        "hangi", "hangisi", "tekrar", "yeniden", "göster", "getir", "bulur", "açar", "mısın", "musun", "olan", "dosyayı", "belgeyi", "belgede",
        "belgedeki", "pdf", "pdfte", "pdfteki", "sabah", "akşam", "öğlen", "gün", "önce"
    ];

    private readonly string _file;
    private readonly string _textFolder;
    private readonly object _gate = new();
    private readonly List<ReadEntry> _entries;
    private DateTime _saved = DateTime.MinValue;
    private bool _dirty;

    public ReadingLog(string file)
    {
        _file = file;
        _textFolder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file) ?? ".", "reading");
        _entries = Load(file);
    }

    /// <summary>Kayıtlı belgeler, en son okunan önce.</summary>
    public IReadOnlyList<ReadEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.OrderByDescending(entry => entry.LastSeen)];
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public static string IdFor(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..16].ToLowerInvariant();

    public ReadEntry? Get(string path)
    {
        var id = IdFor(path);
        lock (_gate)
        {
            return _entries.FirstOrDefault(entry => entry.Id == id);
        }
    }

    /// <summary>Adı bu olan en son okunan belge (pencere başlığında yalnızca ad görünür).</summary>
    public ReadEntry? ByName(string name)
    {
        lock (_gate)
        {
            return _entries.Where(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.LastSeen).FirstOrDefault();
        }
    }

    /// <summary>Belgede <paramref name="seconds"/> daha geçirildi; yoksa eklenir. (Kayıt, yeni mi)</summary>
    public (ReadEntry Entry, bool Added) Touch(string path, DateTime now, int seconds)
    {
        var id = IdFor(path);
        lock (_gate)
        {
            var added = false;
            if (_entries.FirstOrDefault(entry => entry.Id == id) is not { } entry)
            {
                entry = new ReadEntry { Id = id, Path = path, Name = System.IO.Path.GetFileName(path), FirstSeen = now };
                _entries.Add(entry);
                added = true;
                Trim();
            }

            entry.LastSeen = now;
            entry.Seconds += seconds;
            var today = DateOnly.FromDateTime(now);
            if (!entry.Days.Contains(today))
            {
                entry.Days.Add(today);
                if (entry.Days.Count > MaxDays)
                {
                    entry.Days.RemoveAt(0);
                }
            }

            _dirty = true;
            return (entry, added);
        }
    }

    /// <summary>Saklanan metin dosyanın şu anki hâli değilse true (yeni belge ya da değişmiş dosya).</summary>
    public bool NeedsText(ReadEntry entry, DateTime modified, long size)
    {
        lock (_gate)
        {
            return entry.Modified != modified || entry.Size != size || !File.Exists(TextFile(entry.Id));
        }
    }

    /// <summary>
    /// Belgenin metnini saklar. İçerik değiştiyse eski metin önceki sürüm olarak tutulur ve özet yeniden yazılır. Metin
    /// değiştiyse true.
    /// </summary>
    public bool StoreText(ReadEntry entry, string text, DateTime modified, long size, bool foreign)
    {
        text = text.Length > TextLimit ? text[..TextLimit] : text;
        Directory.CreateDirectory(_textFolder);
        var current = TextFile(entry.Id);
        var changed = false;
        lock (_gate)
        {
            if (File.Exists(current))
            {
                var old = File.ReadAllText(current);
                if (Squash(old) != Squash(text))
                {
                    File.WriteAllText(PreviousFile(entry.Id), old);
                    entry.Changed = DateTime.Now;
                    entry.Gist = "";
                    entry.Keywords = "";
                    entry.DigestTries = 0;
                    changed = true;
                }
            }
            else
            {
                changed = true;
            }

            File.WriteAllText(current, text);
            entry.Modified = modified;
            entry.Size = size;
            entry.Foreign = foreign;
            _dirty = true;
        }

        Save();
        return changed;
    }

    public string? Text(ReadEntry entry) => ReadIfExists(TextFile(entry.Id));

    /// <summary>Aynı dosyanın içeriği değişmeden önceki metni; yoksa null.</summary>
    public string? PreviousText(ReadEntry entry) => ReadIfExists(PreviousFile(entry.Id));

    public void SetDigest(ReadEntry entry, string gist, string keywords)
    {
        lock (_gate)
        {
            entry.Gist = gist.Trim();
            entry.Keywords = keywords.Trim();
            entry.DigestTries++;
            _dirty = true;
        }

        Save();
    }

    /// <summary>Henüz özeti yazılmamış, metni saklanmış en son belge.</summary>
    public ReadEntry? NextToDigest()
    {
        lock (_gate)
        {
            return _entries.Where(entry => entry.Gist.Length == 0 && entry.DigestTries < 3 && File.Exists(TextFile(entry.Id)))
                .OrderByDescending(entry => entry.LastSeen).FirstOrDefault();
        }
    }

    /// <summary>Belgeyi (ve sakladığı metni) unutur.</summary>
    public bool Forget(ReadEntry entry)
    {
        lock (_gate)
        {
            if (!_entries.Remove(entry))
            {
                return false;
            }

            DeleteTexts(entry.Id);
            _dirty = true;
        }

        Save();
        return true;
    }

    /// <summary>Hepsini unutur; unutulan belge sayısı.</summary>
    public int Clear()
    {
        int count;
        lock (_gate)
        {
            count = _entries.Count;
            foreach (var entry in _entries)
            {
                DeleteTexts(entry.Id);
            }

            _entries.Clear();
            _dirty = true;
        }

        Save();
        return count;
    }

    /// <summary>Bu günler arasında açılmış belgeler, en son okunan önce.</summary>
    public IReadOnlyList<ReadEntry> InPeriod(DateOnly from, DateOnly to)
    {
        lock (_gate)
        {
            return [.. _entries.Where(entry => entry.Days.Any(day => day >= from && day <= to)).OrderByDescending(entry => entry.LastSeen)];
        }
    }

    /// <summary>
    /// Soruya uyan belgeler (en iyisi önce): adı, özeti ve anahtar sözcükleri aranır. Zaman söylendiyse ("geçen hafta") o
    /// aralıkta açılmış olanlar. Eşleşen sözcük yoksa boş.
    /// </summary>
    public IReadOnlyList<(ReadEntry Entry, double Score)> Search(string question, (DateOnly From, DateOnly To)? period)
    {
        var words = QueryWords(question);
        lock (_gate)
        {
            var pool = period is { } range ? _entries.Where(entry => entry.Days.Any(day => day >= range.From && day <= range.To)) : _entries;
            if (words.Count == 0)
            {
                return [];
            }

            var scored = new List<(ReadEntry, double)>();
            foreach (var entry in pool)
            {
                var name = MemoryRecall.Words(System.IO.Path.GetFileNameWithoutExtension(entry.Name).Replace('_', ' ').Replace('-', ' '));
                var keys = MemoryRecall.Words(entry.Keywords + " " + entry.Gist);
                double score = 0;
                foreach (var word in words)
                {
                    if (name.Any(other => MemoryRecall.Same(word, other)))
                    {
                        score += 2;
                    }
                    else if (keys.Any(other => MemoryRecall.Same(word, other)))
                    {
                        score += 1;
                    }
                }

                if (score > 0)
                {
                    // Eşitlikte en son okunan ve üzerinde daha uzun durulan öne geçer.
                    score += Math.Min(0.5, entry.Minutes / 120) + (entry.LastSeen.Ticks / (double)DateTime.MaxValue.Ticks * 0.01);
                    scored.Add((entry, score));
                }
            }

            return [.. scored.OrderByDescending(item => item.Item2)];
        }
    }

    /// <summary>Soru sözcükleri: okuma eylemi, zaman ve soru dolguları atılmış.</summary>
    public static List<string> QueryWords(string question)
    {
        var filler = MemoryRecall.Words(string.Join(' ', Filler));
        return [.. MemoryRecall.Words(question).Where(word => !filler.Any(other => MemoryRecall.Same(word, other) && word.Length <= other.Length + 3))];
    }

    /// <summary>
    /// Aynı belgenin başka adla kaydedilmiş önceki sürümü ("teklif_v2.pdf" için "teklif_v1.pdf", "rapor (1).docx" için
    /// "rapor.docx"); yoksa null.
    /// </summary>
    public ReadEntry? EarlierVersion(ReadEntry entry)
    {
        var key = BaseName(entry.Name);
        if (key.Length < 3)
        {
            return null;
        }

        lock (_gate)
        {
            return _entries.Where(other => other.Id != entry.Id && BaseName(other.Name) == key && other.FirstSeen < entry.FirstSeen)
                .OrderByDescending(other => other.LastSeen).FirstOrDefault();
        }
    }

    /// <summary>Sürüm işaretlerinden arınmış ad: "Teklif_v2 (1) FINAL.pdf" → "teklif".</summary>
    public static string BaseName(string name)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(name).ToLower(Turkish);
        stem = VersionMarks().Replace(stem, " ");
        stem = NonWord().Replace(stem, " ");
        stem = TrailingNumber().Replace(stem.Trim(), "");
        return string.Join(' ', stem.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Sözdeki zaman aralığı: "bugün", "dün", "bu hafta", "geçen hafta", "bu ay", "geçen ay", "son 3 gün", "pazartesi",
    /// "18 kasım"; yoksa null.
    /// </summary>
    public static (DateOnly From, DateOnly To)? Period(string text, DateOnly today)
    {
        var said = text.ToLower(Turkish);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        if (said.Contains("geçen hafta", StringComparison.Ordinal) && !WeekdayAfterLastWeek().IsMatch(said))
        {
            return (monday.AddDays(-7), monday.AddDays(-1));
        }

        if (said.Contains("bu hafta", StringComparison.Ordinal))
        {
            return (monday, today);
        }

        if (said.Contains("geçen ay", StringComparison.Ordinal))
        {
            var first = new DateOnly(today.Year, today.Month, 1);
            return (first.AddMonths(-1), first.AddDays(-1));
        }

        if (said.Contains("bu ay", StringComparison.Ordinal))
        {
            return (new DateOnly(today.Year, today.Month, 1), today);
        }

        if (LastDays().Match(said) is { Success: true } last && int.TryParse(last.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count is > 0 and <= 365)
        {
            return (today.AddDays(1 - count), today);
        }

        if (DayPhrase().Match(said) is { Success: true } phrase && DayStory.Parse(phrase.Value, today) is { } day)
        {
            return (day, day);
        }

        return null;
    }

    /// <summary>Değişiklik varsa diske yazar (en sık beş dakikada bir; <paramref name="force"/> ile hemen).</summary>
    public void Save(bool force = true)
    {
        string json;
        lock (_gate)
        {
            if (!_dirty || (!force && DateTime.Now - _saved < TimeSpan.FromMinutes(5)))
            {
                return;
            }

            json = JsonSerializer.Serialize(_entries, Json);
            _dirty = false;
            _saved = DateTime.Now;
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_file) ?? ".");
            File.WriteAllText(_file, json);
        }
        catch (IOException)
        {
            lock (_gate)
            {
                _dirty = true;
            }
        }
    }

    private void Trim()
    {
        while (_entries.Count > MaxEntries)
        {
            var oldest = _entries.MinBy(entry => entry.LastSeen)!;
            _entries.Remove(oldest);
            DeleteTexts(oldest.Id);
        }
    }

    private string TextFile(string id) => System.IO.Path.Combine(_textFolder, id + ".txt");

    private string PreviousFile(string id) => System.IO.Path.Combine(_textFolder, id + ".prev.txt");

    private void DeleteTexts(string id)
    {
        foreach (var file in new[] { TextFile(id), PreviousFile(id) })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Açık bir dosya bir dahaki unutmada silinir.
            }
        }
    }

    private static string? ReadIfExists(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string Squash(string text) => NonWord().Replace(text, "");

    private static List<ReadEntry> Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<List<ReadEntry>>(File.ReadAllText(file), Json) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"\((?:\d+|kopya|copy)\)|(?<![a-zçğıöşü])(?:v|ver|versiyon|sürüm|rev|r)[\s_.-]?\d+(?:[._]\d+)*|(?<![a-zçğıöşü])(?:f[iı]nal|son|güncel|guncel|draft|taslak|rev[iı]sed|updated|new|yen[iı]|copy|kopya|ed[iı]ted|düzeltilmiş)(?![a-zçğıöşü])|\d{4}[-_.]?\d{2}[-_.]?\d{2}|\d{1,2}[-_.]\d{1,2}[-_.]\d{2,4}")]
    private static partial Regex VersionMarks();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"(?:^| )\d{1,2}$")]
    private static partial Regex TrailingNumber();

    [GeneratedRegex(@"son (?<n>\d+) gün")]
    private static partial Regex LastDays();

    [GeneratedRegex(@"geçen hafta (?:pazartesi|salı|çarşamba|perşembe|cuma|cumartesi|pazar)")]
    private static partial Regex WeekdayAfterLastWeek();

    [GeneratedRegex(@"(?:önceki gün|evvelsi gün|dün|bugün|\d+ gün önce|(?:geçen (?:hafta )?)?(?:pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar)|\d{1,2} (?:ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık))")]
    private static partial Regex DayPhrase();
}
