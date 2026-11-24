using System.Globalization;
using System.Text;
using System.Text.Json;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Belge hafızası: seçilen klasörlerdeki belgeleri yerelde dizinler ve sorulara en ilgili parçaları verir. Tarama
/// düşük öncelikli tek bir iş parçacığında, açılıştan 2 dakika sonra ve saatte bir yapılır; yalnızca değişen dosyalar
/// yeniden okunur. Parçaların metni diskte (data\documents.txt) durur, bellekte yalnızca sözcük dizini tutulur.
/// Hiçbir şey bilgisayardan çıkmaz.
/// </summary>
public sealed class DocumentLibrary : IDisposable
{
    private const int MaxFiles = 5000;
    private const long MaxFileBytes = 30L * 1024 * 1024;
    private const long MaxTextBytes = 80L * 1024 * 1024;

    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", "$RECYCLE.BIN", "AppData", ".vs", "packages", "venv", ".venv", "__pycache__", "My Music", "My Pictures", "My Videos"
    };

    private static readonly JsonSerializerOptions Json = new();

    private readonly string _metaFile;
    private readonly string _textFile;

    /// <summary>
    /// Metni çıkmayan dosyalar (taranmış PDF, boş ya da bozuk belge) ve damgaları. Eskiden bunlar hiçbir yere yazılmadığı
    /// için her saatlik taramada baştan okunuyordu (günlükte her saat aynı "53 yeni/değişen dosya"); artık dosya
    /// değişmedikçe atlanır.
    /// </summary>
    private readonly string _skipFile;
    private readonly object _gate = new();
    private List<IndexedDocument> _documents = [];
    private DocumentIndex _index = new();
    private Timer? _timer;
    private int _scanning;
    private bool _loaded;

    public DocumentLibrary(string dataFolder)
    {
        _metaFile = Path.Combine(dataFolder, "documents.json");
        _textFile = Path.Combine(dataFolder, "documents.txt");
        _skipFile = Path.Combine(dataFolder, "documents-skipped.json");
    }

    /// <summary>Taranan klasörler; boşsa Belgeler ve Masaüstü.</summary>
    public IReadOnlyList<string> Folders { get; set; } = [];

    public DateTime? LastScan { get; private set; }

    public bool IsScanning => Volatile.Read(ref _scanning) == 1;

    /// <summary>Tarama bitti ya da dizin yüklendi; durum yazısıyla.</summary>
    public event EventHandler<string>? Changed;

    public string Status
    {
        get
        {
            lock (_gate)
            {
                if (IsScanning)
                {
                    return _documents.Count == 0 ? "İlk tarama sürüyor…" : $"{_documents.Count:N0} belge · tarama sürüyor…";
                }

                return LastScan is { } scan
                    ? string.Create(CultureInfo.GetCultureInfo("tr-TR"), $"{_documents.Count:N0} belge, {_index.ChunkCount:N0} parça · son tarama {scan:HH:mm}")
                    : _documents.Count > 0 ? $"{_documents.Count:N0} belge" : "Henüz taranmadı; açılıştan 2 dakika sonra başlar.";
            }
        }
    }

    public static IReadOnlyList<string> DefaultFolders() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
    ];

    public void Start()
    {
        _timer ??= new Timer(_ => _ = ScanAsync(), null, TimeSpan.FromMinutes(2), TimeSpan.FromHours(1));
        _ = RunLowPriority(EnsureLoaded);
    }

    /// <summary>Düşük öncelikli ayrı bir iş parçacığında tarar (oyun ya da derleme sırasında işlemciyle yarışmaz).</summary>
    public Task ScanAsync() => RunLowPriority(Scan);

    /// <summary>En ilgili parçalar: (dosya yolu, parça metni).</summary>
    public IReadOnlyList<(string Path, string Text)> Search(string question, int take = 5)
    {
        lock (_gate)
        {
            EnsureLoadedLocked();
            var hits = _index.Search(question, take);
            if (hits.Count == 0 || !File.Exists(_textFile))
            {
                return [];
            }

            using var text = new FileStream(_textFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return [.. hits.Select(hit =>
            {
                var document = _documents[hit.Document];
                return (document.Path, ReadChunk(text, document.Chunks[hit.Chunk]));
            })];
        }
    }

    /// <summary>Adı sorguya en çok benzeyen belge (eşitlikte en yeni olanı).</summary>
    public string? Find(string name)
    {
        var words = Fold(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length > 1).ToList();
        if (words.Count == 0)
        {
            return null;
        }

        lock (_gate)
        {
            EnsureLoadedLocked();
            return _documents
                .Select(document => (document, score: words.Count(word => Fold(Path.GetFileNameWithoutExtension(document.Path)).Contains(word, StringComparison.Ordinal))))
                .Where(item => item.score > 0)
                .OrderByDescending(item => item.score)
                .ThenByDescending(item => item.document.Modified)
                .Select(item => item.document.Path)
                .FirstOrDefault();
        }
    }

    private static Task RunLowPriority(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work();
                done.SetResult();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                FridayLog.Warn($"Belge hafızası: {exception.Message}");
                done.SetResult();
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "Friday belge hafızası"
        };
        thread.Start();
        return done.Task;
    }

    private void EnsureLoaded()
    {
        lock (_gate)
        {
            EnsureLoadedLocked();
        }

        Changed?.Invoke(this, Status);
    }

    private void EnsureLoadedLocked()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_metaFile) || !File.Exists(_textFile))
        {
            return;
        }

        var documents = JsonSerializer.Deserialize<List<IndexedDocument>>(File.ReadAllText(_metaFile), Json) ?? [];
        var index = new DocumentIndex();
        using (var text = new FileStream(_textFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            for (var number = 0; number < documents.Count; number++)
            {
                var document = documents[number];
                index.AddRange(document.Chunks.Select((chunk, position) => (number, position, ReadChunk(text, chunk))));
            }
        }

        _documents = documents;
        _index = index;
        LastScan = File.GetLastWriteTime(_metaFile);
    }

    private Dictionary<string, FileStamp> ReadSkipped()
    {
        try
        {
            return File.Exists(_skipFile)
                ? new Dictionary<string, FileStamp>(JsonSerializer.Deserialize<Dictionary<string, FileStamp>>(File.ReadAllText(_skipFile), Json) ?? [], StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Bir dosyanın değişip değişmediğini anlamaya yeten damga: son yazılma zamanı ve boyutu.</summary>
    internal sealed record FileStamp(DateTime Modified, long Size);

    private void Scan()
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1)
        {
            return;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var temporary = _textFile + ".tmp";
        try
        {
            Dictionary<string, IndexedDocument> previous;
            lock (_gate)
            {
                EnsureLoadedLocked();
                previous = _documents.ToDictionary(document => document.Path, StringComparer.OrdinalIgnoreCase);
            }

            Changed?.Invoke(this, Status);
            var skipped = ReadSkipped();
            var stillSkipped = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
            var documents = new List<IndexedDocument>();
            var index = new DocumentIndex();
            long written = 0;
            var read = 0;
            using (var oldText = File.Exists(_textFile) ? new FileStream(_textFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite) : null)
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            {
                foreach (var file in Files().Take(MaxFiles))
                {
                    var info = new FileInfo(file);
                    var stamp = new FileStamp(info.LastWriteTimeUtc, info.Length);
                    if (skipped.TryGetValue(file, out var empty) && empty == stamp)
                    {
                        stillSkipped[file] = stamp;
                        continue;
                    }

                    IReadOnlyList<string> texts;
                    if (previous.TryGetValue(file, out var known) && known.Modified == info.LastWriteTimeUtc && known.Size == info.Length && oldText is not null)
                    {
                        texts = [.. known.Chunks.Select(chunk => ReadChunk(oldText, chunk))];
                    }
                    else
                    {
                        try
                        {
                            texts = DocumentIndex.Split(DocumentText.Extract(file));
                            read++;
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
                                                              or System.Xml.XmlException or FormatException or ArgumentException or IndexOutOfRangeException)
                        {
                            stillSkipped[file] = stamp;
                            continue;
                        }

                        // Diskle ve işlemciyle nazik ol: her yeni dosyadan sonra kısa bir mola.
                        Thread.Sleep(3);
                    }

                    if (texts.Count == 0)
                    {
                        stillSkipped[file] = stamp;
                        continue;
                    }

                    var chunks = new List<ChunkRef>();
                    foreach (var text in texts)
                    {
                        var bytes = Encoding.UTF8.GetBytes(text);
                        chunks.Add(new ChunkRef(output.Position, bytes.Length));
                        output.Write(bytes);
                        written += bytes.Length;
                    }

                    var number = documents.Count;
                    index.AddRange(texts.Select((text, position) => (number, position, text)));
                    documents.Add(new IndexedDocument(file, info.LastWriteTimeUtc, info.Length, chunks));
                    if (written > MaxTextBytes)
                    {
                        break;
                    }
                }
            }

            lock (_gate)
            {
                File.Move(temporary, _textFile, overwrite: true);
                File.WriteAllText(_metaFile, JsonSerializer.Serialize(documents, Json));
                File.WriteAllText(_skipFile, JsonSerializer.Serialize(stillSkipped, Json));
                _documents = documents;
                _index = index;
                LastScan = DateTime.Now;
            }

            FridayLog.Info($"Belge hafızası tarandı: {documents.Count} belge, {index.ChunkCount} parça, {read} yeni/değişen dosya okundu ({clock.Elapsed.TotalSeconds:0.0} sn)");
        }
        finally
        {
            Volatile.Write(ref _scanning, 0);
            Changed?.Invoke(this, Status);
        }
    }

    private IEnumerable<string> Files()
    {
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        var pending = new Stack<string>((Folders.Count > 0 ? Folders : DefaultFolders()).Where(Directory.Exists));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            if (!seen.Add(folder))
            {
                continue;
            }

            IEnumerable<string> files;
            IEnumerable<string> folders;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", options).ToList();
                folders = Directory.EnumerateDirectories(folder, "*", options).ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (DocumentText.Extensions.Contains(Path.GetExtension(file)) && !Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)
                    && new FileInfo(file).Length is > 0 and <= MaxFileBytes)
                {
                    yield return file;
                }
            }

            foreach (var child in folders)
            {
                if (!SkipFolders.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static string ReadChunk(FileStream text, ChunkRef chunk)
    {
        var buffer = new byte[chunk.Length];
        text.Seek(chunk.Offset, SeekOrigin.Begin);
        text.ReadExactly(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    private static string Fold(string text) => new(text.ToLower(CultureInfo.GetCultureInfo("tr-TR"))
        .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
        .Select(letter => char.IsLetterOrDigit(letter) ? letter : ' ').ToArray());

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
