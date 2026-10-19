namespace Friday.Core;

public enum EndpointState
{
    /// <summary>Henüz konuşma başlamadı.</summary>
    Waiting,

    /// <summary>Konuşma sürüyor.</summary>
    Speaking,

    /// <summary>Konuşma bitti; kayıt durdurulabilir.</summary>
    Finished,

    /// <summary>Süre doldu ama hiç konuşulmadı.</summary>
    NoSpeech
}

/// <summary>
/// Kısayola basıldıktan sonra konuşmanın ne zaman bittiğini anlar: 20 ms'lik pencerelerin enerjisini ortam
/// gürültüsüne göre ölçer, konuşmadan sonra belirli bir süre sessizlik olunca kaydı bitirir. Böylece tuşa
/// ikinci kez basmak gerekmez.
/// </summary>
public sealed class SpeechEndpointDetector
{
    private const float MinimumSpeechLevel = 0.012f;
    private const float NoiseMultiplier = 3.5f;
    private const int SpeechWindowsToStart = 3;

    private readonly int _windowSize;
    private readonly long _silenceToStop;
    private readonly long _noSpeechTimeout;
    private readonly long _maxDuration;

    private float _noiseFloor = 0.003f;
    private double _windowEnergy;
    private int _windowFill;
    private int _speechRun;
    private long _silenceRun;
    private long _elapsed;
    private bool _speechStarted;

    public SpeechEndpointDetector(int sampleRate, TimeSpan silenceToStop, TimeSpan noSpeechTimeout, TimeSpan maxDuration)
    {
        _windowSize = sampleRate / 50;
        _silenceToStop = Samples(sampleRate, silenceToStop);
        _noSpeechTimeout = Samples(sampleRate, noSpeechTimeout);
        _maxDuration = Samples(sampleRate, maxDuration);
    }

    public EndpointState State { get; private set; } = EndpointState.Waiting;

    /// <summary>Son pencerenin 0–1 arası ses seviyesi (arayüzdeki gösterge için).</summary>
    public float Level { get; private set; }

    public EndpointState Process(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            if (State is EndpointState.Finished or EndpointState.NoSpeech)
            {
                break;
            }

            _windowEnergy += sample * sample;
            if (++_windowFill == _windowSize)
            {
                CloseWindow();
            }
        }

        return State;
    }

    private void CloseWindow()
    {
        var rms = (float)Math.Sqrt(_windowEnergy / _windowSize);
        _windowEnergy = 0;
        _windowFill = 0;
        _elapsed += _windowSize;
        Level = Math.Clamp(rms * 8, 0, 1);

        var isSpeech = rms > Math.Max(MinimumSpeechLevel, _noiseFloor * NoiseMultiplier);
        if (!isSpeech)
        {
            // Gürültü tabanı yalnızca konuşma dışındaki pencerelerden yavaşça öğrenilir.
            _noiseFloor = (_noiseFloor * 0.95f) + (rms * 0.05f);
        }

        if (isSpeech)
        {
            _silenceRun = 0;
            if (++_speechRun >= SpeechWindowsToStart)
            {
                _speechStarted = true;
                State = EndpointState.Speaking;
            }
        }
        else
        {
            _speechRun = 0;
            _silenceRun += _windowSize;
        }

        if (_speechStarted && _silenceRun >= _silenceToStop)
        {
            State = EndpointState.Finished;
        }
        else if (!_speechStarted && _elapsed >= _noSpeechTimeout)
        {
            State = EndpointState.NoSpeech;
        }
        else if (_elapsed >= _maxDuration)
        {
            State = _speechStarted ? EndpointState.Finished : EndpointState.NoSpeech;
        }
    }

    private static long Samples(int sampleRate, TimeSpan duration) => (long)(duration.TotalSeconds * sampleRate);
}
