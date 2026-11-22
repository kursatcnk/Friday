using System.Runtime.InteropServices;
using Windows.Management.Deployment;

namespace Friday.Infrastructure;

/// <summary>
/// Microsoft Mağazası'ndan kurulan uygulamalar (WhatsApp, Spotify, Netflix, Hesap Makinesi…). Bunların Başlat menüsü
/// klasörlerinde kısayolu olmadığı için "WhatsApp'ı aç" ancak ayarlara elle eklenmişse çalışıyordu. Liste kullanıcının
/// paketlerinden bir kez okunur (Friday açılırken arka planda) ve uygulama "shell:AppsFolder\…" adresiyle açılır.
/// </summary>
public static class StoreApps
{
    private static readonly Lazy<List<(string Name, string Target)>> List = new(Load);

    /// <summary>Görünen ad ve açma adresi.</summary>
    public static IReadOnlyList<(string Name, string Target)> All => List.Value;

    /// <summary>Listeyi arka planda hazırlar; ilk "X'i aç" beklemez.</summary>
    public static void Warm() => _ = Task.Run(() => List.Value);

    private static List<(string Name, string Target)> Load()
    {
        var apps = new List<(string Name, string Target)>();
        try
        {
            foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
            {
                if (package.IsFramework || package.IsResourcePackage || package.IsBundle)
                {
                    continue;
                }

                foreach (var entry in package.GetAppListEntries())
                {
                    var name = entry.DisplayInfo.DisplayName?.Trim() ?? "";
                    if (name.Length > 1 && !name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                    {
                        apps.Add((name, @"shell:AppsFolder\" + entry.AppUserModelId));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            FridayLog.Warn("Mağaza uygulamaları okunamadı: " + exception.Message);
        }

        return apps;
    }
}
