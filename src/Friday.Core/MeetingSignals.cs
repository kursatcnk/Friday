namespace Friday.Core;

/// <summary>
/// Windows'un gizlilik kaydındaki bir uygulamanın mikrofon (ya da kamera) kullanımı. Anahtar, paketli uygulamada paket
/// adı ("MSTeams_8wekyb3d8bbwe"), paketsiz uygulamada yolun "#"lı hali ("C:#Program Files#Zoom#bin#Zoom.exe");
/// Stop 0 ise uygulama cihazı şu an kullanıyordur.
/// </summary>
public sealed record DeviceUse(string Key, long Start, long Stop)
{
    public bool InUse => Start > 0 && Stop == 0;
}

/// <summary>
/// Toplantı algılama: Friday dışında bir uygulama mikrofonu kullanıyorsa kullanıcı büyük ihtimalle bir görüşmede ya da
/// kayıttadır. Hiçbir ses dinlenmez; yalnızca Windows'un "mikrofonu kim kullanıyor" kaydına bakılır.
/// </summary>
public static class MeetingSignals
{
    private static readonly (string Part, string Name)[] Known =
    [
        ("msteams", "Teams"), ("teams", "Teams"), ("zoom", "Zoom"), ("webex", "Webex"), ("discord", "Discord"), ("slack", "Slack"),
        ("skype", "Skype"), ("whatsapp", "WhatsApp"), ("telegram", "Telegram"), ("signal", "Signal"), ("obs", "OBS"),
        ("chrome", "Chrome"), ("msedge", "Edge"), ("firefox", "Firefox"), ("opera", "Opera"), ("brave", "Brave"),
        ("soundrecorder", "Ses Kaydedici"), ("audacity", "Audacity")
    ];

    /// <summary>Mikrofonu şu an kullanan ilk uygulamanın okunur adı; Friday'in kendisi sayılmaz. Kimse kullanmıyorsa null.</summary>
    /// <param name="ownExecutable">Friday'in exe yolu (ör. E:\...\Friday.exe).</param>
    public static string? ActiveApp(IEnumerable<DeviceUse> uses, string ownExecutable)
    {
        var own = ownExecutable.Replace('\\', '#');
        return uses
            .Where(use => use.InUse && !string.Equals(use.Key, own, StringComparison.OrdinalIgnoreCase))
            .Where(use => !Path.GetFileName(use.Key.Replace('#', '\\')).Equals("Friday.exe", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(use => use.Start)
            .Select(use => FriendlyName(use.Key))
            .FirstOrDefault();
    }

    /// <summary>"C:#Program Files#Zoom#bin#Zoom.exe" → "Zoom", "MSTeams_8wekyb3d8bbwe" → "Teams".</summary>
    public static string FriendlyName(string key)
    {
        var name = key.Contains('#', StringComparison.Ordinal)
            ? Path.GetFileNameWithoutExtension(key.Replace('#', '\\'))
            : key.Split('_')[0];
        if (name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
        {
            name = name["Microsoft.".Length..];
        }

        var lower = name.ToLowerInvariant();
        foreach (var (part, friendly) in Known)
        {
            if (lower.Contains(part, StringComparison.Ordinal))
            {
                return friendly;
            }
        }

        return name;
    }
}
