using NAudio.Wave;

namespace Friday.Infrastructure;

/// <summary>
/// Odak ve dinlenme için ortam sesleri: yağmur, beyaz/pembe/kahverengi gürültü, dalga, şömine ve rüzgâr. Hiçbiri dosya
/// değildir; ses o an matematikle üretilir, indirme gerekmez. Friday konuşurken ses kısılır, bitince geri gelir; istenen
/// süre dolunca yavaşça kapanır.
/// </summary>
public sealed class AmbientSound : IDisposable
{
    private readonly object _gate = new();
    private WaveOut? _output;
    private Generator? _generator;
    private Timer? _stopTimer;

    public double Volume { get; set; } = 0.25;

    public string? Playing { get; private set; }

    public DateTime? EndsAt { get; private set; }

    public void Start(string kind, TimeSpan? length)
    {
        lock (_gate)
        {
            StopNow();
            _generator = new Generator(kind, (float)Math.Clamp(Volume, 0.02, 1));
            _output = new WaveOut { BufferMilliseconds = 200 };
            _output.Init(_generator);
            _output.Play();
            Playing = kind;
            EndsAt = length is { } duration ? DateTime.Now + duration : null;
            _stopTimer = length is { } stopAfter ? new Timer(_ => FadeOut(), null, stopAfter, Timeout.InfiniteTimeSpan) : null;
        }
    }

    /// <summary>Friday konuşurken ortam sesi kısılır.</summary>
    public void Duck(bool ducked)
    {
        lock (_gate)
        {
            _generator?.SetTarget(ducked ? 0.25f : 1f);
        }
    }

    /// <summary>İki saniyede yavaşça susar.</summary>
    public void FadeOut()
    {
        Generator? generator;
        lock (_gate)
        {
            generator = _generator;
            generator?.SetTarget(0);
        }

        if (generator is null)
        {
            return;
        }

        _ = Task.Delay(2200).ContinueWith(_ =>
        {
            lock (_gate)
            {
                if (_generator == generator)
                {
                    StopNow();
                }
            }
        }, TaskScheduler.Default);
    }

    private void StopNow()
    {
        _stopTimer?.Dispose();
        _stopTimer = null;
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _generator = null;
        Playing = null;
        EndsAt = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopNow();
        }
    }

    /// <summary>Gürültü üreteci (44,1 kHz, stereo). Kazanç yumuşakça hedefe yaklaşır; tık sesi olmaz.</summary>
    private sealed class Generator(string kind, float volume) : ISampleProvider
    {
        private readonly Random _random = new();
        private readonly float _volume = volume;
        private float _gain;
        private float _target = 1;
        private float _b0, _b1, _b2, _b3, _b4, _b5, _b6;
        private float _brownLeft, _brownRight;
        private float _drop;
        private float _crackle;
        private float _lowLeft, _lowRight;
        private double _phase;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2);

        public void SetTarget(float target) => _target = target;

        public int Read(Span<float> buffer)
        {
            for (var index = 0; index + 1 < buffer.Length; index += 2)
            {
                _gain += (_target - _gain) * 0.00005f;
                var (left, right) = Next();
                var level = _gain * _volume;
                buffer[index] = Math.Clamp(left * level, -1, 1);
                buffer[index + 1] = Math.Clamp(right * level, -1, 1);
            }

            return buffer.Length;
        }

        private float White() => (float)(_random.NextDouble() * 2 - 1);

        /// <summary>Paul Kellet'in pembe gürültü süzgeci.</summary>
        private float Pink()
        {
            var white = White();
            _b0 = 0.99886f * _b0 + white * 0.0555179f;
            _b1 = 0.99332f * _b1 + white * 0.0750759f;
            _b2 = 0.96900f * _b2 + white * 0.1538520f;
            _b3 = 0.86650f * _b3 + white * 0.3104856f;
            _b4 = 0.55000f * _b4 + white * 0.5329522f;
            _b5 = -0.7616f * _b5 - white * 0.0168980f;
            var pink = _b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + white * 0.5362f;
            _b6 = white * 0.115926f;
            return pink * 0.11f;
        }

        private static float Brown(ref float state, float white)
        {
            state = (state + 0.02f * white) / 1.02f;
            return state * 3.5f;
        }

        private (float Left, float Right) Next()
        {
            switch (kind)
            {
                case "beyaz":
                    return (White() * 0.35f, White() * 0.35f);
                case "pembe":
                    return (Pink(), Pink());
                case "kahverengi":
                    return (Brown(ref _brownLeft, White()), Brown(ref _brownRight, White()));
                case "dalga":
                {
                    // Kahverengi gürültü ~0,08 Hz'lik yavaş bir dalgayla kabarıp çekilir.
                    _phase += 2 * Math.PI * 0.08 / 44_100;
                    var swell = (float)(0.35 + 0.65 * Math.Pow((Math.Sin(_phase) + 1) / 2, 2));
                    return (Brown(ref _brownLeft, White()) * swell, Brown(ref _brownRight, White()) * swell);
                }

                case "ruzgar":
                {
                    _phase += 2 * Math.PI * 0.05 / 44_100;
                    var gust = (float)(0.4 + 0.6 * ((Math.Sin(_phase) + Math.Sin(_phase * 2.7) * 0.5 + 1.5) / 3));
                    _lowLeft += (Pink() - _lowLeft) * 0.08f;
                    _lowRight += (Pink() - _lowRight) * 0.08f;
                    return (_lowLeft * 2.2f * gust, _lowRight * 2.2f * gust);
                }

                case "somine":
                {
                    // Alçak bir uğultu ve rastgele çıtırtılar.
                    if (_random.NextDouble() < 0.00025)
                    {
                        _crackle = 0.6f + (float)_random.NextDouble() * 0.4f;
                    }

                    _crackle *= 0.985f;
                    var crack = _crackle * White();
                    var rumble = Brown(ref _brownLeft, White()) * 0.6f;
                    return (rumble + crack, rumble + crack * 0.8f);
                }

                default:
                {
                    // Yağmur: yumuşatılmış pembe gürültü ve araya düşen damlalar.
                    if (_random.NextDouble() < 0.0018)
                    {
                        _drop = 0.25f + (float)_random.NextDouble() * 0.35f;
                    }

                    _drop *= 0.93f;
                    _lowLeft += (Pink() - _lowLeft) * 0.35f;
                    _lowRight += (Pink() - _lowRight) * 0.35f;
                    var drop = _drop * White();
                    return (_lowLeft * 1.4f + drop, _lowRight * 1.4f + drop * 0.7f);
                }
            }
        }
    }
}
