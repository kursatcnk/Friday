using System.Globalization;

namespace Friday.Core;

public enum WatchKind
{
    /// <summary>İndirilenler klasöründeki yarım dosyalar bitince.</summary>
    Download,

    /// <summary>İşlemci ya da ekran kartı yoğun çalıştıktan sonra boşa çıkınca (render, derleme, dışa aktarma bitti).</summary>
    Idle,

    /// <summary>İnternet bağlantısı geri gelince.</summary>
    Internet,

    /// <summary>Ekran kartı sıcaklığı sınırı geçince.</summary>
    GpuTemperature
}

/// <summary>"X olunca haber ver" isteği.</summary>
public sealed record WatchRequest(WatchKind Kind, double Threshold = 0)
{
    /// <summary>"indirmelerin bitmesini", "ekran kartının 85 dereceyi geçmesini".</summary>
    public string Describe() => Kind switch
    {
        WatchKind.Download => "indirmelerin bitmesini",
        WatchKind.Idle => "bilgisayarın boşa çıkmasını",
        WatchKind.Internet => "internetin gelmesini",
        _ => string.Create(CultureInfo.InvariantCulture, $"ekran kartının {Threshold:0} dereceyi geçmesini")
    };
}

/// <summary>
/// "Bilgisayar boşa çıkınca haber ver": önce yoğun bir çalışma (yüzde 35 üstü) görülmeli, sonra yük 30 sn boyunca yüzde
/// 15'in altında kalmalı. Böylece bir render ya da derleme bittiğinde haber verilir, zaten boşta olan bilgisayar için değil.
/// </summary>
public sealed class IdleDetector
{
    public const double BusyAbove = 35;
    public const double QuietBelow = 15;
    public static readonly TimeSpan QuietFor = TimeSpan.FromSeconds(30);

    private DateTime? _quietSince;

    public bool SawWork { get; private set; }

    /// <param name="busyPercent">İşlemci ve ekran kartı kullanımının büyüğü (0–100).</param>
    /// <returns>İş bitti sayılıyorsa true.</returns>
    public bool Update(double busyPercent, DateTime now)
    {
        if (busyPercent >= BusyAbove)
        {
            SawWork = true;
            _quietSince = null;
            return false;
        }

        if (!SawWork || busyPercent >= QuietBelow)
        {
            _quietSince = null;
            return false;
        }

        _quietSince ??= now;
        return now - _quietSince >= QuietFor;
    }
}

/// <summary>Tarayıcıların ve indirme araçlarının yarım dosya uzantıları.</summary>
public static class DownloadFiles
{
    private static readonly string[] PartialExtensions = [".crdownload", ".part", ".partial", ".download", ".opdownload", ".!ut", ".!qb"];

    public static bool IsPartial(string path) =>
        PartialExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>"film.mkv.crdownload" → "film.mkv".</summary>
    public static string FinalName(string path) =>
        IsPartial(path) ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);
}

/// <summary>Yazı tura, zar ve rastgele sayı.</summary>
public static class Chance
{
    public static string CoinFlip(Random random) => random.Next(2) == 0 ? "Yazı geldi." : "Tura geldi.";

    public static string Dice(Random random, int count)
    {
        count = Math.Clamp(count, 1, 5);
        var rolls = Enumerable.Range(0, count).Select(_ => random.Next(1, 7)).ToList();
        return count == 1
            ? $"Zar {rolls[0]} geldi."
            : $"Zarlar {TurkishGrammar.Join([.. rolls.Select(roll => roll.ToString(CultureInfo.InvariantCulture))])} geldi, toplam {rolls.Sum()}.";
    }

    public static string Number(Random random, int from, int to)
    {
        if (from > to)
        {
            (from, to) = (to, from);
        }

        return $"{random.Next(from, to + 1)}.";
    }
}

/// <summary>Kronometre ve süre okunuşu.</summary>
public static class SpokenDuration
{
    /// <summary>"1 saat 3 dakika 12 saniye", "45 saniye".</summary>
    public static string Of(TimeSpan span)
    {
        var parts = new List<string>();
        var total = Math.Max(0, (long)Math.Floor(span.TotalSeconds));
        if (total >= 3600)
        {
            parts.Add($"{total / 3600} saat");
        }

        if (total % 3600 >= 60)
        {
            parts.Add($"{total % 3600 / 60} dakika");
        }

        if (total % 60 > 0 || parts.Count == 0)
        {
            parts.Add($"{total % 60} saniye");
        }

        return string.Join(' ', parts);
    }
}
