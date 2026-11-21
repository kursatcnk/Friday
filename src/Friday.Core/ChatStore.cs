using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Sohbet listesindeki bir satır.</summary>
public sealed record ChatSummary(string Id, string Title, DateTimeOffset Created, DateTimeOffset Updated, int TurnCount, bool Pinned, string Preview);

/// <summary>Sohbetlerde aramanın bir sonucu: sohbet ve eşleşen cümlenin çevresi.</summary>
public sealed record ChatSearchHit(ChatSummary Chat, string Snippet);

/// <summary>
/// Ayrı sohbetler (D:\Friday\data\chats\&lt;id&gt;.json). Her sohbetin bir başlığı vardır; başlık ilk anlamlı sorudan
/// kendiliğinden çıkar, istenirse değiştirilir. Bir süre konuşulmazsa ya da "yeni sohbet" denince yeni bir sohbet açılır;
/// eski sohbetler listeden açılıp kaldığı yerden sürdürülebilir. Hepsi yalnızca bu bilgisayarda durur.
/// </summary>
public sealed partial class ChatStore
{
    private const int TitleWords = 7;
    private const int TitleLength = 46;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _directory;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, ChatFile> _chats = new(StringComparer.Ordinal);
    private string? _currentId;

    public ChatStore(string directory, TimeProvider? clock = null)
    {
        _directory = directory;
        _clock = clock ?? TimeProvider.System;
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<ChatFile>(File.ReadAllText(file), Options) is { Id.Length: > 0 } chat)
                {
                    _chats[chat.Id] = chat;
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                // Bozuk bir dosya diğer sohbetleri engellemez.
            }
        }
    }

    /// <summary>Liste değişti (yeni tur, yeni başlık, silme, sabitleme).</summary>
    public event EventHandler? Changed;

    /// <summary>Açık sohbet değişti (yeni sohbet ya da listeden başka bir sohbet açıldı).</summary>
    public event EventHandler? CurrentChanged;

    /// <summary>Açık sohbet; yeni ve henüz boş bir sohbetteyken null.</summary>
    public string? CurrentId
    {
        get
        {
            lock (_gate)
            {
                return _currentId;
            }
        }
    }

    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _chats.Count;
            }
        }
    }

    /// <summary>Sabitlenenler önce, sonra en son konuşulandan eskiye.</summary>
    public IReadOnlyList<ChatSummary> List()
    {
        lock (_gate)
        {
            return [.. _chats.Values
                .OrderByDescending(chat => chat.Pinned)
                .ThenByDescending(chat => chat.Updated)
                .Select(Summarize)];
        }
    }

    public ChatSummary? Find(string id)
    {
        lock (_gate)
        {
            return _chats.TryGetValue(id, out var chat) ? Summarize(chat) : null;
        }
    }

    public IReadOnlyList<ConversationTurn> Turns(string? id)
    {
        lock (_gate)
        {
            return id is not null && _chats.TryGetValue(id, out var chat) ? [.. chat.Turns] : [];
        }
    }

    public IReadOnlyList<ConversationTurn> CurrentTurns => Turns(CurrentId);

    /// <summary>Yeni, boş bir sohbete geçer. Sohbet ilk soruyla birlikte kaydedilir; boş sohbet listede görünmez.</summary>
    public void NewChat()
    {
        lock (_gate)
        {
            if (_currentId is null)
            {
                return;
            }

            _currentId = null;
        }

        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Listeden bir sohbeti açar; sonraki sorular bu sohbete eklenir.</summary>
    public bool Open(string id)
    {
        lock (_gate)
        {
            if (!_chats.ContainsKey(id))
            {
                return false;
            }

            if (_currentId == id)
            {
                return true;
            }

            _currentId = id;
        }

        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Açık sohbete bir soru-cevap ekler; sohbet yoksa ilk sorudan başlığıyla birlikte oluşturur.</summary>
    public ChatSummary Append(string user, string assistant)
    {
        var now = _clock.GetLocalNow();
        ChatSummary summary;
        lock (_gate)
        {
            // İlk soruyla oluşan sohbet "başka sohbete geçmek" sayılmaz; CurrentChanged yalnızca Changed ile bildirilir.
            if (_currentId is null || !_chats.TryGetValue(_currentId, out var chat))
            {
                chat = new ChatFile { Id = NewId(now), Created = now, Title = TitleFrom(user), TitleFromSmallTalk = ConversationStyle.IsSmallTalk(user) };
                _chats[chat.Id] = chat;
                _currentId = chat.Id;
            }
            else if (!chat.TitleEdited && chat.TitleFromSmallTalk && chat.Turns.Count < 4 && !ConversationStyle.IsSmallTalk(user)
                     && TitleFrom(user) is { Length: > 0 } better)
            {
                // "Merhaba" ile başlayan bir sohbetin başlığı ilk gerçek sorudan alınır.
                chat.Title = better;
                chat.TitleFromSmallTalk = false;
            }

            chat.Turns.Add(new ConversationTurn(now, user, assistant));
            chat.Updated = now;
            Save(chat);
            summary = Summarize(chat);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return summary;
    }

    public void Rename(string id, string title)
    {
        var clean = Whitespace().Replace(title, " ").Trim();
        lock (_gate)
        {
            if (clean.Length == 0 || !_chats.TryGetValue(id, out var chat))
            {
                return;
            }

            chat.Title = clean.Length > 80 ? clean[..80] : clean;
            chat.TitleEdited = true;
            Save(chat);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetPinned(string id, bool pinned)
    {
        lock (_gate)
        {
            if (!_chats.TryGetValue(id, out var chat) || chat.Pinned == pinned)
            {
                return;
            }

            chat.Pinned = pinned;
            Save(chat);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sohbeti kalıcı olarak siler; açık sohbetse yeni, boş bir sohbete geçilir.</summary>
    public bool Delete(string id)
    {
        bool wasCurrent;
        lock (_gate)
        {
            if (!_chats.Remove(id))
            {
                return false;
            }

            TryDelete(FileFor(id));
            wasCurrent = _currentId == id;
            if (wasCurrent)
            {
                _currentId = null;
            }
        }

        if (wasCurrent)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Bütün sohbetleri siler; silinen sohbet sayısını döndürür.</summary>
    public int DeleteAll()
    {
        int count;
        lock (_gate)
        {
            count = _chats.Count;
            foreach (var id in _chats.Keys)
            {
                TryDelete(FileFor(id));
            }

            _chats.Clear();
            _currentId = null;
        }

        CurrentChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
        return count;
    }

    /// <summary>Başlıklarda ve konuşmalarda arar (büyük/küçük harf ve Türkçe harf farkı gözetmeden).</summary>
    public IReadOnlyList<ChatSearchHit> Search(string query, int limit = 40)
    {
        var needle = Fold(query.Trim());
        if (needle.Length == 0)
        {
            return [];
        }

        var hits = new List<ChatSearchHit>();
        lock (_gate)
        {
            foreach (var chat in _chats.Values.OrderByDescending(chat => chat.Updated))
            {
                if (Fold(chat.Title).Contains(needle, StringComparison.Ordinal))
                {
                    hits.Add(new ChatSearchHit(Summarize(chat), Summarize(chat).Preview));
                }
                else if (chat.Turns.SelectMany(turn => new[] { turn.User, turn.Assistant })
                             .FirstOrDefault(text => Fold(text).Contains(needle, StringComparison.Ordinal)) is { } text)
                {
                    hits.Add(new ChatSearchHit(Summarize(chat), Snippet(text, needle)));
                }

                if (hits.Count >= limit)
                {
                    break;
                }
            }
        }

        return hits;
    }

    /// <summary>Bütün sohbetlerin bütün turları, sohbet başlığıyla, eskiden yeniye (konuşma hafızası için).</summary>
    public IReadOnlyList<PastTurn> PastTurns()
    {
        lock (_gate)
        {
            return [.. _chats.Values
                .SelectMany(chat => chat.Turns.Select(turn => new PastTurn(turn.Time.LocalDateTime, turn.User, turn.Assistant, chat.Title)))
                .OrderBy(turn => turn.Time)];
        }
    }

    /// <summary>Bir günün bütün turları, bütün sohbetlerden, saat sırasıyla ("Bugün ne konuştuk?").</summary>
    public IReadOnlyList<ConversationTurn> Read(DateOnly day)
    {
        lock (_gate)
        {
            return [.. _chats.Values
                .SelectMany(chat => chat.Turns)
                .Where(turn => DateOnly.FromDateTime(turn.Time.DateTime) == day)
                .OrderBy(turn => turn.Time)];
        }
    }

    /// <summary>Sohbeti okunur bir Markdown metnine çevirir (dışa aktarma).</summary>
    public string ToMarkdown(string id, string assistantName)
    {
        lock (_gate)
        {
            if (!_chats.TryGetValue(id, out var chat))
            {
                return "";
            }

            var text = new StringBuilder();
            text.Append("# ").AppendLine(chat.Title).AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"_{chat.Created.ToString("d MMMM yyyy HH:mm", Turkish)}_").AppendLine();
            foreach (var turn in chat.Turns)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"**Sen** ({turn.Time.ToString("HH:mm", Turkish)}): {turn.User}").AppendLine();
                text.AppendLine(CultureInfo.InvariantCulture, $"**{assistantName}**: {turn.Assistant}").AppendLine();
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Eski sürümlerin gün gün tuttuğu geçmiş (data\history\*.jsonl) bir kez sohbetlere aktarılır: her gün bir sohbet olur.
    /// Eski dosyalara dokunulmaz.
    /// </summary>
    public int ImportLegacy(string historyDirectory)
    {
        var marker = Path.Combine(_directory, ".history-imported");
        if (!Directory.Exists(historyDirectory) || File.Exists(marker))
        {
            return 0;
        }

        var imported = 0;
        var log = new ConversationLog(historyDirectory);
        foreach (var file in Directory.EnumerateFiles(historyDirectory, "*.jsonl").Order(StringComparer.Ordinal))
        {
            if (!DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || log.Read(day) is not { Count: > 0 } turns)
            {
                continue;
            }

            var first = turns.FirstOrDefault(turn => !ConversationStyle.IsSmallTalk(turn.User)) ?? turns[0];
            var chat = new ChatFile
            {
                Id = NewId(turns[0].Time),
                Title = TitleFrom(first.User),
                Created = turns[0].Time,
                Updated = turns[^1].Time,
                Turns = [.. turns]
            };
            lock (_gate)
            {
                _chats[chat.Id] = chat;
                Save(chat);
            }

            imported++;
        }

        Directory.CreateDirectory(_directory);
        File.WriteAllText(marker, imported.ToString(CultureInfo.InvariantCulture));
        if (imported > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return imported;
    }

    /// <summary>
    /// İlk sorudan kısa bir başlık: hitap ve dolgu sözcükleri atılır, en fazla yedi kelime, baş harf büyük.
    /// "hey friday yarın istanbul'da hava nasıl olacak?" → "Yarın İstanbul'da hava nasıl olacak".
    /// </summary>
    public static string TitleFrom(string text)
    {
        var clean = Address().Replace(Whitespace().Replace(text, " ").Trim(), "");
        clean = Politeness().Replace(clean, "").Trim().TrimEnd('.', '!', '?', ',', ';', ':', '…').Trim();
        if (clean.Length == 0)
        {
            return "Yeni sohbet";
        }

        var words = clean.Split(' ');
        var title = string.Join(' ', words.Take(TitleWords));
        if (title.Length > TitleLength)
        {
            var cut = title.LastIndexOf(' ', TitleLength);
            title = title[..(cut > 20 ? cut : TitleLength)];
        }

        if (title.Length < clean.Length)
        {
            title = title.TrimEnd(',', ';', ':') + "…";
        }

        return char.ToUpper(title[0], Turkish) + title[1..];
    }

    private static ChatSummary Summarize(ChatFile chat)
    {
        var last = chat.Turns.Count > 0 ? chat.Turns[^1].Assistant : "";
        var preview = Whitespace().Replace(last, " ").Trim();
        return new ChatSummary(chat.Id, chat.Title, chat.Created, chat.Updated, chat.Turns.Count, chat.Pinned,
            preview.Length > 90 ? preview[..90].TrimEnd() + "…" : preview);
    }

    private static string Snippet(string text, string needle)
    {
        var flat = Whitespace().Replace(text, " ").Trim();
        var at = Fold(flat).IndexOf(needle, StringComparison.Ordinal);
        var start = Math.Max(0, at - 30);
        var length = Math.Min(flat.Length - start, 100);
        return string.Concat(start > 0 ? "…" : "", flat.AsSpan(start, length), start + length < flat.Length ? "…" : "");
    }

    /// <summary>Aramada "Istanbul", "istanbul" ve "İstanbul" aynı sayılır; uzunluk değişmez (konum hesabı için).</summary>
    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var letter in text.ToLower(Turkish))
        {
            folded.Append(letter switch { 'ı' => 'i', 'ş' => 's', 'ğ' => 'g', 'ü' => 'u', 'ö' => 'o', 'ç' => 'c', 'â' => 'a', 'î' => 'i', 'û' => 'u', _ => letter });
        }

        return folded.ToString();
    }

    private static string NewId(DateTimeOffset time) =>
        string.Create(CultureInfo.InvariantCulture, $"{time:yyyyMMdd-HHmmss}-{Random.Shared.Next(0x1000, 0xFFFF):x4}");

    private string FileFor(string id) => Path.Combine(_directory, id + ".json");

    /// <summary>Önce geçici dosyaya yazılır, sonra yerine taşınır; yazarken kapanan Friday sohbeti bozmaz.</summary>
    private void Save(ChatFile chat)
    {
        Directory.CreateDirectory(_directory);
        var file = FileFor(chat.Id);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(chat, Options));
        File.Move(temporary, file, overwrite: true);
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^(?:hey\s+)?friday[,!.\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    [GeneratedRegex(@"\s+(?:lütfen)(?=[\s.!?]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Politeness();

    /// <summary>Dosyadaki biçim.</summary>
    private sealed class ChatFile
    {
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";

        /// <summary>Başlığı kullanıcı verdi; kendiliğinden değişmez.</summary>
        public bool TitleEdited { get; set; }

        /// <summary>Başlık "merhaba" gibi bir selamdan çıktı; ilk gerçek soruda değişir.</summary>
        public bool TitleFromSmallTalk { get; set; }

        public bool Pinned { get; set; }

        public DateTimeOffset Created { get; set; }

        public DateTimeOffset Updated { get; set; }

        public List<ConversationTurn> Turns { get; set; } = [];
    }
}
