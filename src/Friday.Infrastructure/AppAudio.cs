using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace Friday.Infrastructure;

/// <summary>Ses çıkaran bir uygulama: adı, sesi (0–1), kapalı mı, şu an çalıyor mu.</summary>
public sealed record AudioApp(string Name, float Volume, bool Muted, bool Playing);

/// <summary>
/// Uygulama bazında ses (Windows ses karıştırıcısı): varsayılan hoparlördeki oturumları okur, bir uygulamanın sesini
/// ayarlar ya da kapatır. Bir uygulamanın birden çok oturumu olabilir (Chrome sekmeleri); hepsine uygulanır.
/// </summary>
public static class AppAudio
{
    /// <summary>"Tarayıcı" denince bunlardan hangisi ses çıkarıyorsa o.</summary>
    private static readonly string[] Browsers = ["chrome", "msedge", "firefox", "opera", "brave", "vivaldi"];

    public static IReadOnlyList<AudioApp> Apps()
    {
        var apps = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
        Visit((name, session) =>
        {
            var volume = session.SimpleAudioVolume;
            var playing = session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive;
            if (!apps.TryGetValue(name, out var known) || (playing && !known.Playing))
            {
                apps[name] = new AudioApp(name, volume.Volume, volume.Mute, playing);
            }
        });
        return [.. apps.Values.OrderByDescending(app => app.Playing).ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Söylenen uygulamanın bütün oturumlarına uygular; uygulama bulunamazsa null, bulunursa yeni durumu.</summary>
    /// <param name="change">Eski ses ve kapalılık → yeni ses (0–1) ve kapalılık.</param>
    public static AudioApp? Apply(string spoken, Func<float, bool, (float Volume, bool Muted)> change)
    {
        var wanted = Match(spoken);
        AudioApp? result = null;
        Visit((name, session) =>
        {
            if (!wanted(name))
            {
                return;
            }

            var volume = session.SimpleAudioVolume;
            var (level, muted) = change(volume.Volume, volume.Mute);
            volume.Volume = Math.Clamp(level, 0f, 1f);
            volume.Mute = muted;
            result = new AudioApp(name, volume.Volume, volume.Mute, session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive);
        });
        return result;
    }

    /// <summary>"spotify" → Spotify oturumları; "tarayıcı" → çalışan tarayıcı; "edge" → msedge.</summary>
    public static Func<string, bool> Match(string spoken)
    {
        var wanted = AssistantTools.Simplify(spoken);
        if (wanted is "tarayici" or "tarayicinin" or "internet")
        {
            return name => Browsers.Contains(AssistantTools.Simplify(name));
        }

        return name =>
        {
            var key = AssistantTools.Simplify(name);
            return key.Length >= 2 && (key == wanted || (wanted.Length >= 3 && key.Contains(wanted, StringComparison.Ordinal))
                || (key.Length >= 4 && wanted.Contains(key, StringComparison.Ordinal)));
        };
    }

    private static void Visit(Action<string, AudioSessionControl> action)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            if (session.IsSystemSoundsSession || session.GetProcessID == (uint)Environment.ProcessId)
            {
                continue;
            }

            if (ProcessName((int)session.GetProcessID) is { } name)
            {
                action(name, session);
            }
        }
    }

    private static string? ProcessName(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
