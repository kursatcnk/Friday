using System.Text.Encodings.Web;
using System.Text.Json;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed class JsonSettingsStore(FridayPaths paths)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Türkçe karakterler dosyada ı yerine okunur halde kalsın.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public FridaySettings Load()
    {
        Directory.CreateDirectory(paths.Data);
        if (!File.Exists(paths.SettingsFile))
        {
            var defaults = new FridaySettings();
            Save(defaults);
            return defaults;
        }

        // Dosya bir araçla yanlış kodlamada kaydedildiyse "Ä°stanbul" gibi bozulan harfler düzeltilip geri yazılır.
        var text = TextRepair.FixMojibake(File.ReadAllText(paths.SettingsFile));
        var settings = JsonSerializer.Deserialize<FridaySettings>(text, Options) ?? new FridaySettings();
        settings.Apps = new Dictionary<string, string>(settings.Apps, StringComparer.OrdinalIgnoreCase);
        settings.Routines = new Dictionary<string, List<string>>(settings.Routines, StringComparer.OrdinalIgnoreCase);
        // Yeni sürümde eklenen ayarlar dosyaya varsayılan değerleriyle yazılır; kullanıcı hepsini dosyada görür.
        Save(settings);
        return settings;
    }

    public void Save(FridaySettings settings)
    {
        Directory.CreateDirectory(paths.Data);
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(settings, Options));
    }
}
