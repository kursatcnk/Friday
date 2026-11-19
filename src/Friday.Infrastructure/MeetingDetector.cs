using Friday.Core;
using Microsoft.Win32;

namespace Friday.Infrastructure;

/// <summary>Windows'un gizlilik kaydından (CapabilityAccessManager) mikrofonu o an kullanan uygulamayı okur.</summary>
public static class MeetingDetector
{
    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    private static readonly string Own = Environment.ProcessPath ?? "Friday.exe";

    /// <summary>Mikrofonu kullanan (Friday dışı) uygulamanın adı; yoksa ya da okunamazsa null.</summary>
    public static string? ActiveCall()
    {
        try
        {
            return MeetingSignals.ActiveApp(Read("microphone"), Own);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public static IReadOnlyList<DeviceUse> Read(string capability)
    {
        var uses = new List<DeviceUse>();
        using var root = Registry.CurrentUser.OpenSubKey(Store + capability);
        if (root is null)
        {
            return uses;
        }

        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key is null)
            {
                continue;
            }

            if (name == "NonPackaged")
            {
                foreach (var app in key.GetSubKeyNames())
                {
                    using var appKey = key.OpenSubKey(app);
                    if (appKey is not null)
                    {
                        uses.Add(Use(app, appKey));
                    }
                }
            }
            else
            {
                uses.Add(Use(name, key));
            }
        }

        return uses;
    }

    private static DeviceUse Use(string name, RegistryKey key) =>
        new(name, key.GetValue("LastUsedTimeStart") is long start ? start : 0, key.GetValue("LastUsedTimeStop") is long stop ? stop : -1);
}
