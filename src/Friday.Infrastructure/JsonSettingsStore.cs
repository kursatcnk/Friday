using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Ayar dosyası. Sesle yapılan ayar değişiklikleri (kur alarmı, uyku saati, kısaltma, okunuş…) geri alma günlüğüne
/// girer: "geri al" dosyayı eski haline döndürür ve değerleri uygulamanın paylaştığı canlı ayar nesnesine yazar.
/// </summary>
public sealed class JsonSettingsStore(FridayPaths paths) : IRestorableStore
{
    private static readonly string[] OldBreakRoutine = ["medya:oynat_duraklat", "soyle:Mola zamanı. Biraz su iç, gözlerini dinlendir."];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Türkçe karakterler dosyada ı yerine okunur halde kalsın.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Uygulamanın paylaştığı canlı ayar nesnesi; geri alınan değerler buna yazılır.</summary>
    public FridaySettings? Live { get; set; }

    /// <summary>Ayarlar geri alındı ya da yinelendi; uygulama onları yeniden uygulamalı.</summary>
    public event EventHandler? Restored;

    /// <inheritdoc/>
    public string Title => "ayarlar";

    /// <inheritdoc/>
    public void Restore(string file, string? content)
    {
        if (content is null)
        {
            return;
        }

        var restored = Read(content);
        Normalize(restored);
        if (Live is { } live)
        {
            // Herkes aynı nesneyi tuttuğu için değerler yerinde kopyalanır.
            foreach (var property in typeof(FridaySettings).GetProperties().Where(property => property.CanRead && property.CanWrite))
            {
                property.SetValue(live, property.GetValue(restored));
            }
        }

        UndoLog.WriteBack(file, content);
        Restored?.Invoke(this, EventArgs.Empty);
    }

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
        var settings = Read(text);
        Normalize(settings);
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

    /// <summary>Okunan sözlükler büyük/küçük harf ayırmayan karşılaştırıcıyla yeniden kurulur.</summary>
    private static void Normalize(FridaySettings settings)
    {
        settings.Apps = new Dictionary<string, string>(settings.Apps, StringComparer.OrdinalIgnoreCase);
        settings.Routines = new Dictionary<string, List<string>>(settings.Routines, StringComparer.OrdinalIgnoreCase);
        settings.Triggers = new Dictionary<string, List<string>>(settings.Triggers, StringComparer.OrdinalIgnoreCase);
        settings.Snippets = new Dictionary<string, string>(settings.Snippets, StringComparer.OrdinalIgnoreCase);
        settings.Pronunciations = new Dictionary<string, string>(settings.Pronunciations, StringComparer.OrdinalIgnoreCase);
    }

    public void Save(FridaySettings settings)
    {
        UndoLog.Touching(this, paths.SettingsFile);
        Directory.CreateDirectory(paths.Data);
        File.WriteAllText(paths.SettingsFile, Write(settings));
    }

    /// <summary>Dosya metni → ayarlar; şifreli sırlar çözülür (bkz. <see cref="SettingsSecrets"/>).</summary>
    internal static FridaySettings Read(string text)
    {
        if (JsonNode.Parse(text) is not JsonObject node)
        {
            return new FridaySettings();
        }

        SettingsSecrets.Unprotect(node);
        return node.Deserialize<FridaySettings>(Options) ?? new FridaySettings();
    }

    /// <summary>Ayarlar → dosya metni; sırlar şifrelenir, bellekteki nesne değişmez.</summary>
    internal static string Write(FridaySettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings, Options)!.AsObject();
        SettingsSecrets.Protect(node);
        return node.ToJsonString(Options);
    }
}
