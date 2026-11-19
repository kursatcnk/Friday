using System.Diagnostics;
using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed partial class AssistantTools
{
    private const string ReadingList = "okuma listesi";
    private const string SongList = "beğendiğim şarkılar";

    /// <summary>Sesli günlük; asistan kurulurken verilir.</summary>
    public JournalStore? Journal { get; set; }

    /// <summary>Odak hedefini ayarlar ya da bugünkü ilerlemeyi ve seriyi söyler.</summary>
    private string FocusGoal(string minutes)
    {
        if (int.TryParse(minutes, out var value) && value >= 10)
        {
            Settings.FocusGoalMinutes = Math.Min(value, 600);
            SaveSettings?.Invoke(Settings);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return $"Tamam, günlük odak hedefin {TurkishGrammar.Duration(Settings.FocusGoalMinutes)}.";
        }

        if (Rhythm is not { } rhythm)
        {
            return "Ritim koçu kapalı; Ayarlar › Ritim'den açabilirsin.";
        }

        return RhythmGoal.Describe(rhythm.Today.FocusMinutes, Settings.FocusGoalMinutes, RhythmGoal.Streak(rhythm.History(60), Settings.FocusGoalMinutes));
    }

    /// <summary>Odak sesi: tür verilirse ortam sesini başlatır, null ise kapatır.</summary>
    public void SetFocusSound(string? kind) => _ = kind is null ? AmbientCommand("kapat", "", "") : AmbientCommand("ac", kind, "");

    /// <summary>"Bunu sonra okuyacağım": öndeki tarayıcı sekmesinin başlığı ve adresi okuma listesine.</summary>
    private string ReadLater(string action)
    {
        if (Lists is not { } lists)
        {
            return "Okuma listesi şu an kullanılamıyor.";
        }

        switch (Simplify(action))
        {
            case "kaydet":
                if (Desktop.Foreground() is not { } window || Desktop.BrowserAddress(window) is not { Length: > 0 } address)
                {
                    return "Öndeki pencere bir tarayıcı sekmesi değil; kaydetmek istediğin sayfayı açıp tekrar söyle.";
                }

                var url = address.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? address : "https://" + address;
                var title = TrimBrowserName(window.Title);
                lists.Add(ReadingList, $"{title.Replace(",", " ", StringComparison.Ordinal)} — {url}");
                ledger.Record(DataFlow.Local, "Okuma listesi", "Bir sayfa kaydedildi");
                return $"Okuma listene ekledim: {title}. Listede {lists.Items(ReadingList).Count} sayfa var.";
            case "ac":
                var next = lists.Items(ReadingList) is [var first, ..] ? first : null;
                if (next is null)
                {
                    return "Okuma listen boş.";
                }

                var parts = next.Split(" — ", 2);
                if (parts.Length == 2 && Uri.TryCreate(parts[1], UriKind.Absolute, out var link) && link.Scheme is "http" or "https")
                {
                    using var process = Process.Start(new ProcessStartInfo(link.ToString()) { UseShellExecute = true });
                }

                lists.Remove(ReadingList, next);
                return $"Açtım: {parts[0]}. Listede {lists.Items(ReadingList).Count} sayfa kaldı.";
            default:
                var items = lists.Items(ReadingList);
                return items.Count == 0
                    ? "Okuma listen boş. Bir sayfadayken \"bunu sonra okuyacağım\" de."
                    : $"Okuma listende {items.Count} sayfa var: {string.Join("; ", items.Take(5).Select(item => item.Split(" — ", 2)[0]))}.";
        }
    }

    private static string TrimBrowserName(string title)
    {
        foreach (var suffix in new[] { " - Google Chrome", " - Microsoft Edge", " — Mozilla Firefox", " - Brave", " - Opera", " - Vivaldi", " ve diğer", " and" })
        {
            var at = title.IndexOf(suffix, StringComparison.Ordinal);
            if (at > 0)
            {
                title = title[..at];
            }
        }

        return title.Trim();
    }

    /// <summary>"Bu şarkıyı kaydet": çalan parça "beğendiğim şarkılar" listesine.</summary>
    private async Task<string> SaveSongAsync()
    {
        if (Lists is not { } lists)
        {
            return "Listeler şu an kullanılamıyor.";
        }

        if (await NowPlaying.CurrentAsync().ConfigureAwait(false) is not { } song)
        {
            return "Şu an çalan bir şarkı yok.";
        }

        var entry = song.Artist.Length > 0 ? $"{song.Artist} - {song.Title}" : song.Title;
        var added = lists.Add(SongList, entry.Replace(",", " ", StringComparison.Ordinal).Replace(" ve ", " & ", StringComparison.Ordinal));
        return added.Count == 0
            ? $"\"{song.Title}\" zaten beğendiğin şarkılarda."
            : $"\"{song.Title}\" beğendiğin şarkılara eklendi; listede {lists.Items(SongList).Count} şarkı var.";
    }

    /// <summary>
    /// Disk analizi: kullanıcı klasörleri ve Friday'in diski taranır (en fazla ~20 sn, düşük öncelik); en büyük
    /// klasörler ve dosyalar söylenir. Hiçbir şey silinmez.
    /// </summary>
    private string DiskReport()
    {
        var clock = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(20);
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home ? Path.Combine(home, "Downloads") : "",
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint };
        var folders = new List<(string Path, long Bytes)>();
        var biggest = new List<(string Path, long Bytes)>();
        var thread = Thread.CurrentThread;
        var priority = thread.Priority;
        thread.Priority = ThreadPriority.BelowNormal;
        try
        {
            foreach (var root in roots)
            {
                foreach (var folder in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint }).Append(root))
                {
                    long size = 0;
                    var files = folder == root
                        ? Directory.EnumerateFiles(root, "*", new EnumerationOptions { IgnoreInaccessible = true })
                        : Directory.EnumerateFiles(folder, "*", options);
                    foreach (var file in files)
                    {
                        if (clock.Elapsed > budget)
                        {
                            break;
                        }

                        try
                        {
                            var length = new FileInfo(file).Length;
                            size += length;
                            if (length > 200L * 1024 * 1024)
                            {
                                biggest.Add((file, length));
                            }
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            // Okunamayan dosya sayılmaz.
                        }
                    }

                    folders.Add((folder == root ? root + " (doğrudan içindekiler)" : folder, size));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Erişilemeyen klasörler atlanır.
        }
        finally
        {
            thread.Priority = priority;
        }

        static string Size(long bytes) => bytes >= 1L << 30
            ? string.Create(CultureInfo.GetCultureInfo("tr-TR"), $"{bytes / (double)(1L << 30):0.0} GB")
            : $"{bytes / (1L << 20)} MB";

        var topFolders = folders.Where(item => item.Bytes > 100L * 1024 * 1024).OrderByDescending(item => item.Bytes).Take(5).ToList();
        var topFiles = biggest.OrderByDescending(item => item.Bytes).Take(5).ToList();
        if (topFolders.Count == 0 && topFiles.Count == 0)
        {
            return "Kullanıcı klasörlerinde 100 MB'tan büyük bir şey bulamadım.";
        }

        var parts = new List<string>();
        if (topFolders.Count > 0)
        {
            parts.Add("En çok yer kaplayan klasörler: " + string.Join(", ", topFolders.Select(item => $"{Path.GetFileName(item.Path.TrimEnd('\\'))} {Size(item.Bytes)}")) + ".");
        }

        if (topFiles.Count > 0)
        {
            parts.Add("En büyük dosyalar: " + string.Join(", ", topFiles.Select(item => $"{Path.GetFileName(item.Path)} {Size(item.Bytes)}")) + ".");
        }

        if (folders.FirstOrDefault(item => item.Path.EndsWith("Downloads", StringComparison.OrdinalIgnoreCase)) is { Bytes: > 2L * 1024 * 1024 * 1024 } downloads)
        {
            parts.Add($"İndirilenler {Size(downloads.Bytes)}; \"indirilenleri düzenle\" dersen türlerine göre ayırırım, silmek senin kararın.");
        }

        ledger.Record(DataFlow.Local, "Disk analizi", "Kullanıcı klasörlerinin boyutları okundu; hiçbir şey silinmedi");
        return string.Join(" ", parts) + (clock.Elapsed > budget ? " (Tarama 20 saniyede kesildi, sonuçlar eksik olabilir.)" : "");
    }

    private string JournalCommand(string action, string text, string span)
    {
        if (Journal is not { } journal)
        {
            return "Günlük şu an kullanılamıyor.";
        }

        var now = DateTime.Now;
        if (Simplify(action) is "yaz" or "ekle")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "Günlüğe ne yazayım?";
            }

            journal.Add(now, text);
            ledger.Record(DataFlow.Local, "Günlük", "Bir yazı eklendi");
            return "Günlüğe yazdım.";
        }

        var (from, to) = Simplify(span) switch
        {
            "dun" => (now.Date.AddDays(-1), now.Date.AddTicks(-1)),
            "hafta" => (now.Date.AddDays(-6), now),
            _ => (now.Date, now)
        };
        return JournalStore.Describe(journal.Between(from, to), now);
    }
}
