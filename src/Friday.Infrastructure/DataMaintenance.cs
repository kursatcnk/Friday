using System.Globalization;
using System.IO.Compression;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>"Yer aç" listesinde silinebilecek bir klasör.</summary>
public sealed record ReclaimItem(string Label, string Path, long Bytes, string Note);

/// <summary>
/// Yedekleme, geri yükleme ve yer açma. Yedek yalnızca kişisel verileri içerir (ayarlar, hafıza, sohbetler, hatırlatmalar,
/// notlar, ritim, gizlilik defteri) ve birkaç MB'dır; modeller ve önbellekler yedeğe girmez, setup.ps1 onları yeniden indirir.
/// Geri yükleme Friday kapanıp açılırken yapılır; açıkken dosyalar bellekteki verilerle ezilmesin diye önce bekletilir.
/// </summary>
public static class DataMaintenance
{
    public const string PendingFolder = "restore-pending";

    /// <summary>Yedeğe girmeyen, yeniden üretilebilen ya da büyük klasörler.</summary>
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "uv-cache", "tmp", "xtts-home", "ollama-home", "logs", "captures", "exports", PendingFolder
    };

    /// <summary>Kişisel verileri tek bir zip dosyasına yazar.</summary>
    /// <returns>Yedeğe giren dosya sayısı.</returns>
    public static int CreateBackup(string dataFolder, string zipFile)
    {
        var temporary = zipFile + ".tmp";
        File.Delete(temporary);
        var count = 0;
        using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(dataFolder, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(dataFolder, file);
                var top = relative.Split(Path.DirectorySeparatorChar)[0];
                if (Excluded.Contains(top) || relative.StartsWith("before-restore-", StringComparison.OrdinalIgnoreCase)
                    || relative.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                archive.CreateEntryFromFile(file, relative.Replace('\\', '/'), CompressionLevel.Optimal);
                count++;
            }
        }

        File.Move(temporary, zipFile, overwrite: true);
        return count;
    }

    /// <summary>Yedeği doğrular ve bir sonraki açılışta uygulanmak üzere bekletir.</summary>
    /// <returns>Geçerli bir Friday yedeğiyse true.</returns>
    public static bool StageRestore(string zipFile, string dataFolder)
    {
        using (var archive = ZipFile.OpenRead(zipFile))
        {
            if (!archive.Entries.Any(entry => entry.FullName is "settings.json" or "memory.json"))
            {
                return false;
            }
        }

        var pending = Path.Combine(dataFolder, PendingFolder);
        if (Directory.Exists(pending))
        {
            Directory.Delete(pending, recursive: true);
        }

        ZipFile.ExtractToDirectory(zipFile, pending);
        return true;
    }

    /// <summary>
    /// Bekleyen bir geri yükleme varsa (açılışta, veriler yüklenmeden önce) uygular. Mevcut veriler silinmez,
    /// data\before-restore-… klasörüne taşınır.
    /// </summary>
    /// <returns>Geri yükleme yapıldıysa true.</returns>
    public static bool ApplyPendingRestore(string dataFolder)
    {
        var pending = Path.Combine(dataFolder, PendingFolder);
        if (!Directory.Exists(pending))
        {
            return false;
        }

        var keep = Path.Combine(dataFolder, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}");
        foreach (var source in Directory.EnumerateFileSystemEntries(pending))
        {
            var name = Path.GetFileName(source);
            var target = Path.Combine(dataFolder, name);
            if (File.Exists(target) || Directory.Exists(target))
            {
                Directory.CreateDirectory(keep);
                if (Directory.Exists(target))
                {
                    Directory.Move(target, Path.Combine(keep, name));
                }
                else
                {
                    File.Move(target, Path.Combine(keep, name));
                }
            }

            if (Directory.Exists(source))
            {
                Directory.Move(source, target);
            }
            else
            {
                File.Move(source, target);
            }
        }

        Directory.Delete(pending, recursive: true);
        return true;
    }

    /// <summary>Friday çalışırken silinebilecek, kullanılmayan ya da yeniden indirilebilen klasörler (boyutlarıyla).</summary>
    public static IReadOnlyList<ReclaimItem> Reclaimable(FridayPaths paths, FridaySettings settings)
    {
        var items = new List<ReclaimItem>();
        void Add(string label, string path, string note)
        {
            if (Directory.Exists(path) && Size(path) is > 1024 * 1024 and var bytes)
            {
                items.Add(new ReclaimItem(label, path, bytes, note));
            }
        }

        Add("Python indirme önbelleği", Path.Combine(paths.Data, "uv-cache"), "XTTS kurulumundan kalan, yeniden üretilir");
        Add("Uyandırma kelimesi eğitim verisi", Path.Combine(paths.Home, "training"), "eğitilmiş model GitHub'da duruyor");
        Add("Yarım kalmış indirmeler", Path.Combine(paths.Home, "downloads"), "setup.ps1 yeniden indirir");
        if (!string.Equals(settings.SpeechEngine.Trim(), "xtts", StringComparison.OrdinalIgnoreCase))
        {
            Add("XTTS ortamı (kullanılmıyor)", Path.Combine(paths.Tools, "xtts-env"), "setup-xtts.ps1 yeniden kurar");
            Add("XTTS modeli (kullanılmıyor)", Path.Combine(paths.Models, "xtts"), "setup-xtts.ps1 yeniden indirir");
        }

        return items;
    }

    /// <summary>Seçilen klasörleri siler; açılan yeri bayt olarak döndürür. Silinemeyen dosya atlanır.</summary>
    public static long Reclaim(IEnumerable<ReclaimItem> items)
    {
        long freed = 0;
        foreach (var item in items)
        {
            var before = Size(item.Path);
            try
            {
                Directory.Delete(item.Path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                FridayLog.Warn($"{item.Path} tamamen silinemedi: {exception.Message}");
            }

            freed += before - (Directory.Exists(item.Path) ? Size(item.Path) : 0);
        }

        return freed;
    }

    public static long Size(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 })
                .Sum(file => file.Length);
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
    }

    /// <summary>"8,2 GB", "540 MB".</summary>
    public static string Describe(long bytes) => bytes >= 1024L * 1024 * 1024
        ? string.Create(CultureInfo.GetCultureInfo("tr-TR"), $"{bytes / 1024.0 / 1024 / 1024:0.0} GB")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, bytes / 1024 / 1024)} MB");
}
