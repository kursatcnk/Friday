using NAudio.Wave;

namespace Friday.Infrastructure;

/// <summary>16 bit PCM akışından geçen sesin seviyesini ölçer; sesi değiştirmez.</summary>
internal sealed class LevelMeter(IWaveProvider source) : IWaveProvider
{
    private readonly int _window = source.WaveFormat.AverageBytesPerSecond / 30;
    private double _energy;
    private int _bytes;
    private int _samples;

    public event EventHandler<float>? LevelChanged;

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(Span<byte> buffer)
    {
        var read = source.Read(buffer);
        for (var index = 0; index + 1 < read; index += 2)
        {
            var sample = (short)(buffer[index] | (buffer[index + 1] << 8)) / 32768.0;
            _energy += sample * sample;
            _samples++;
        }

        _bytes += read;
        if (_bytes >= _window)
        {
            var rms = _samples == 0 ? 0 : Math.Sqrt(_energy / _samples);
            LevelChanged?.Invoke(this, (float)Math.Clamp(rms * 5, 0, 1));
            _energy = 0;
            _samples = 0;
            _bytes = 0;
        }

        return read;
    }
}
