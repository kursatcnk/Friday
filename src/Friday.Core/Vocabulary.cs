using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

/// <summary>Bir kelime kartı: ön yüz (İngilizce), arka yüz (Türkçe anlamlar), Leitner kutusu ve sıradaki tekrar günü.</summary>
public sealed class WordCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];

    public string Front { get; set; } = "";

    public string Back { get; set; } = "";

    /// <summary>0 = yeni ya da yanlış bilinmiş; 5 = iyice oturmuş.</summary>
    public int Box { get; set; }

    public DateOnly Due { get; set; }

    /// <summary>İlk sorulduğu gün; hiç sorulmadıysa null.</summary>
    public DateOnly? FirstSeen { get; set; }

    public int Right { get; set; }

    public int Wrong { get; set; }

    public bool Starter { get; set; }
}

/// <summary>Destenin durumu: toplam, öğrenilen (kutu 3+), tekrar bekleyen ve henüz görülmemiş kart sayısı.</summary>
/// <param name="Waiting">Bugünkü oturumda sorulacak kart sayısı (tekrarlar + günün kalan yeni kelime hakkı).</param>
public sealed record DeckStats(int Total, int Learned, int DueToday, int Unseen, int Waiting);

/// <summary>
/// Kelime pratiği: Leitner kutularıyla aralıklı tekrar. Doğru bilinen kart bir üst kutuya çıkar ve 1, 3, 7, 14, 30 gün
/// sonra yeniden sorulur; yanlış bilinen ilk kutuya döner. Günde en fazla 8 yeni kelime gelir. Hazır deste 110 sık
/// kullanılan İngilizce kelimedir; kullanıcı kendi kelimelerini ekler. D:\Friday\data\vocabulary.json'da durur.
/// </summary>
public sealed class VocabularyDeck : IRestorableStore
{
    public const int NewPerDay = 8;
    public const int SessionSize = 12;

    private static readonly int[] Intervals = [0, 1, 3, 7, 14, 30];
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly HashSet<string> Filler = ["demek", "demektir", "anlamina", "gelir", "anlami", "bence", "galiba", "sanirim", "yani", "herhalde", "olabilir", "mi", "mu", "o", "bu", "su", "bir"];

    private readonly string _file;
    private readonly object _gate = new();
    private readonly List<WordCard> _cards;

    public VocabularyDeck(string file)
    {
        _file = file;
        try
        {
            _cards = File.Exists(file) ? JsonSerializer.Deserialize<List<WordCard>>(File.ReadAllText(file), Options) ?? [] : [];
        }
        catch (JsonException)
        {
            _cards = [];
        }
    }

    public event EventHandler? Changed;

    /// <inheritdoc/>
    public string Title => "kelime kartları";

    /// <inheritdoc/>
    public void Restore(string file, string? content)
    {
        lock (_gate)
        {
            _cards.Clear();
            _cards.AddRange(content is null ? [] : JsonSerializer.Deserialize<List<WordCard>>(content, Options) ?? []);
            UndoLog.WriteBack(file, content);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<WordCard> Cards
    {
        get
        {
            lock (_gate)
            {
                return [.. _cards];
            }
        }
    }

    /// <summary>Hazır desteyi ekler (daha önce eklenmediyse); eklenen kart sayısı.</summary>
    public int SeedStarter(DateOnly today)
    {
        lock (_gate)
        {
            if (_cards.Any(card => card.Starter))
            {
                return 0;
            }

            var known = _cards.Select(card => Key(card.Front)).ToHashSet();
            var added = 0;
            foreach (var (front, back) in StarterWords.All)
            {
                if (known.Add(Key(front)))
                {
                    _cards.Add(new WordCard { Front = front, Back = back, Due = today, Starter = true });
                    added++;
                }
            }

            Save();
            return added;
        }
    }

    /// <summary>Yeni kart ekler; aynı ön yüz varsa anlamı güncellenir.</summary>
    public WordCard Add(string front, string back, DateOnly today)
    {
        var cleanFront = front.Trim().Trim('"', '\'', '“', '”');
        var cleanBack = back.Trim().TrimEnd('.');
        lock (_gate)
        {
            if (_cards.FirstOrDefault(card => Key(card.Front) == Key(cleanFront)) is { } existing)
            {
                existing.Back = cleanBack;
                Save();
                return existing;
            }

            // Kullanıcının kendi kelimeleri hazır destedeki görülmemiş kelimelerden önce sorulur.
            var card = new WordCard { Front = cleanFront, Back = cleanBack, Due = today };
            var firstStarter = _cards.FindIndex(item => item.Starter && item.FirstSeen is null);
            _cards.Insert(firstStarter < 0 ? _cards.Count : firstStarter, card);
            Save();
            return card;
        }
    }

    public bool Remove(string front)
    {
        lock (_gate)
        {
            if (_cards.RemoveAll(card => Key(card.Front) == Key(front)) == 0)
            {
                return false;
            }

            Save();
            return true;
        }
    }

    /// <summary>Bugünkü oturum: vadesi gelen tekrarlar (en eski önce), sonra günlük sınır kadar yeni kart.</summary>
    public IReadOnlyList<WordCard> Session(DateOnly today, int size = SessionSize)
    {
        lock (_gate)
        {
            var reviews = _cards.Where(card => card.FirstSeen is not null && card.Due <= today).OrderBy(card => card.Due).ThenBy(card => card.Box);
            var newToday = _cards.Count(card => card.FirstSeen == today);
            var fresh = _cards.Where(card => card.FirstSeen is null).Take(Math.Max(0, NewPerDay - newToday));
            return [.. reviews.Concat(fresh).Take(size)];
        }
    }

    /// <summary>Cevabı kaydeder: doğruysa kart bir üst kutuya çıkar, yanlışsa ilk kutuya döner.</summary>
    public void Grade(string id, bool correct, DateOnly today)
    {
        lock (_gate)
        {
            if (_cards.FirstOrDefault(card => card.Id == id) is not { } card)
            {
                return;
            }

            card.FirstSeen ??= today;
            if (correct)
            {
                card.Right++;
                card.Box = Math.Min(card.Box + 1, Intervals.Length - 1);
            }
            else
            {
                card.Wrong++;
                card.Box = 0;
            }

            card.Due = today.AddDays(Intervals[card.Box]);
            Save();
        }
    }

    public DeckStats Stats(DateOnly today)
    {
        lock (_gate)
        {
            var newToday = _cards.Count(card => card.FirstSeen == today);
            var due = _cards.Count(card => card.FirstSeen is not null && card.Due <= today);
            var unseen = _cards.Count(card => card.FirstSeen is null);
            return new DeckStats(_cards.Count, _cards.Count(card => card.Box >= 3), due, unseen, due + Math.Min(unseen, Math.Max(0, NewPerDay - newToday)));
        }
    }

    /// <summary>Hiç çalışıldı mı (sabah karşılaması yalnızca o zaman tekrarları hatırlatır).</summary>
    public bool Started
    {
        get
        {
            lock (_gate)
            {
                return _cards.Any(card => card.FirstSeen is not null);
            }
        }
    }

    /// <summary>
    /// Söylenen cevap kartın anlamlarından birine uyuyor mu? Anlamlar virgül, noktalı virgül, "/" ya da "veya" ile
    /// ayrılır. Türkçe harf, büyük-küçük harf ve "demek", "galiba" gibi dolgu sözcükleri fark etmez; konuşma tanımanın
    /// tek harf hatası affedilir; fiillerde kök yeter ("vazgeçmek" için "vazgeçtim").
    /// </summary>
    public static bool Check(string answer, string back)
    {
        var said = Words(answer);
        if (said.Count == 0)
        {
            return false;
        }

        var spoken = string.Join(' ', said);
        foreach (var meaning in back.Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries)
                     .SelectMany(part => part.Split([" veya ", " ya da "], StringSplitOptions.RemoveEmptyEntries)))
        {
            var target = string.Join(' ', Words(meaning));
            if (target.Length == 0)
            {
                continue;
            }

            if (spoken == target || $" {spoken} ".Contains($" {target} ", StringComparison.Ordinal)
                || (target.Length >= 5 && Distance(spoken, target) <= 1))
            {
                return true;
            }

            // "vazgeçmek" → kök "vazgeç": "vazgeçtim", "vazgeçti" de doğru sayılır.
            if ((target.EndsWith("mek", StringComparison.Ordinal) || target.EndsWith("mak", StringComparison.Ordinal)) && target.Length >= 7
                && target[..^3] is var stem && (spoken.StartsWith(stem, StringComparison.Ordinal) || $" {spoken}".Contains($" {stem}", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> Words(string text) =>
        [.. Key(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !Filler.Contains(word))];

    /// <summary>Küçük harf, Türkçe harfler sadeleşmiş, harf ve rakam dışı karakterler boşluk.</summary>
    public static string Key(string text) =>
        new string([.. text.ToLower(Turkish)
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c').Replace('â', 'a').Replace('î', 'i')
            .Select(letter => char.IsLetterOrDigit(letter) ? letter : ' ')]).Trim();

    private static int Distance(string first, string second)
    {
        if (Math.Abs(first.Length - second.Length) > 1)
        {
            return 2;
        }

        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        for (var i = 1; i <= first.Length; i++)
        {
            var current = new int[second.Length + 1];
            current[0] = i;
            for (var j = 1; j <= second.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (first[i - 1] == second[j - 1] ? 0 : 1));
            }

            previous = current;
        }

        return previous[second.Length];
    }

    private void Save()
    {
        UndoLog.Touching(this, _file);
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_cards, Options));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
