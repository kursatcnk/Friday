using NAudio.Wave;

namespace Friday.Infrastructure;

/// <summary>Mikrofonun açıldığını ve kapandığını duyuran kısa tonlar.</summary>
public static class AudioCues
{
    private const int SampleRate = 24_000;

    private static readonly byte[] Open = Tones(660, 880);
    private static readonly byte[] Close = Tones(880, 560);

    public static void PlayListening() => Play(Open);

    public static void PlayDone() => Play(Close);

    private static void Play(byte[] pcm)
    {
        var output = new WaveOut { BufferMilliseconds = 60 };
        var source = new RawSourceWaveStream(new MemoryStream(pcm, writable: false), new WaveFormat(SampleRate, 16, 1));
        output.PlaybackStopped += (_, _) =>
        {
            output.Dispose();
            source.Dispose();
        };
        output.Init(source);
        output.Play();
    }

    private static byte[] Tones(double first, double second)
    {
        const double toneSeconds = 0.07;
        var perTone = (int)(SampleRate * toneSeconds);
        var pcm = new byte[perTone * 2 * 2];
        for (var index = 0; index < perTone * 2; index++)
        {
            var frequency = index < perTone ? first : second;
            var local = index % perTone;
            // Kısa yumuşak giriş/çıkış: tonlar tık sesi yapmaz.
            var envelope = Math.Min(1, Math.Min(local, perTone - local) / (SampleRate * 0.008));
            var value = (short)(Math.Sin(2 * Math.PI * frequency * index / SampleRate) * envelope * 0.18 * short.MaxValue);
            pcm[index * 2] = (byte)value;
            pcm[(index * 2) + 1] = (byte)(value >> 8);
        }

        return pcm;
    }
}
