using System.Text.Encodings.Web;
using System.Text.Json;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed class JsonSettingsStore(FridayPaths paths)
{
    private static readonly string[] OldBreakRoutine = ["medya:oynat_duraklat", "soyle:Mola zamanı. Biraz su iç, gözlerini dinlendir."];

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
            var defaults = new FridaySettings { SettingsVersion = FridaySettings.CurrentVersion };
            Save(defaults);
            return defaults;
        }

        // Dosya bir araçla yanlış kodlamada kaydedildiyse "Ä°stanbul" gibi bozulan harfler düzeltilip geri yazılır.
        var text = TextRepair.FixMojibake(File.ReadAllText(paths.SettingsFile));
        var settings = JsonSerializer.Deserialize<FridaySettings>(text, Options) ?? new FridaySettings();
        settings.Apps = new Dictionary<string, string>(settings.Apps, StringComparer.OrdinalIgnoreCase);
        settings.Routines = new Dictionary<string, List<string>>(settings.Routines, StringComparer.OrdinalIgnoreCase);
        settings.Triggers = new Dictionary<string, List<string>>(settings.Triggers, StringComparer.OrdinalIgnoreCase);
        // Eski varsayılan mola rutini (müziği durdurup tek cümle söylemek) ritim koçunun rehberli molasıyla değişir;
        // kullanıcının kendi yazdığı bir mola rutinine dokunulmaz.
        if (settings.Routines.TryGetValue("mola", out var steps) && steps.SequenceEqual(OldBreakRoutine))
        {
            settings.Routines["mola"] = ["ritim:mola"];
        }

        Migrate(settings);

        // Yeni sürümde eklenen ayarlar dosyaya varsayılan değerleriyle yazılır; kullanıcı hepsini dosyada görür.
        Save(settings);
        return settings;
    }

    /// <summary>
    /// Eski ayar dosyalarını sırayla günceller; kullanıcının kendi değiştirdiği değerlere dokunulmaz.
    /// Sürüm 2: uyandırma kelimesi "Hey Friday" olur (eski varsayılan "hey_jarvis") ve "toplantı modu" ile "iyi geceler"
    /// rutinleri eklenir. Sürüm 3: sohbetler ayrı sohbetler halinde tutulur; eski 10 dakikalık varsayılan, sohbetlerin
    /// her soruda bölünmemesi için 30 dakika olur.
    /// </summary>
    internal static void Migrate(FridaySettings settings)
    {
        if (settings.SettingsVersion < 2)
        {
            if (settings.WakeWord == "hey_jarvis")
            {
                settings.WakeWord = "hey_friday";
            }

            foreach (var (name, steps) in new FridaySettings().Routines)
            {
                settings.Routines.TryAdd(name, steps);
            }
        }

        if (settings.SettingsVersion < 3 && settings.ConversationMemoryMinutes == 10)
        {
            settings.ConversationMemoryMinutes = 30;
        }

        settings.SettingsVersion = FridaySettings.CurrentVersion;
    }

    public void Save(FridaySettings settings)
    {
        Directory.CreateDirectory(paths.Data);
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(settings, Options));
    }
}
