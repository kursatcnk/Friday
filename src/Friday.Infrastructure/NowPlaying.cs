using Windows.Media.Control;

namespace Friday.Infrastructure;

/// <summary>Windows'un medya oturumlarından (Spotify, tarayıcıdaki YouTube…) o an çalanı okur.</summary>
public static class NowPlaying
{
    public static async Task<string> DescribeAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var session = manager.GetCurrentSession();
        if (session is null)
        {
            return "Şu an çalan bir şey yok.";
        }

        var properties = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var state = playback.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "çalıyor",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "duraklatılmış",
            _ => "durmuş"
        };
        var source = session.SourceAppUserModelId.Split('!')[0].Split('\\').Last().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        var artist = string.IsNullOrWhiteSpace(properties.Artist) ? "" : $", sanatçı: {properties.Artist}";
        var album = string.IsNullOrWhiteSpace(properties.AlbumTitle) ? "" : $", albüm: {properties.AlbumTitle}";
        return $"{source} üzerinde {state}: \"{properties.Title}\"{artist}{album}.";
    }
}
