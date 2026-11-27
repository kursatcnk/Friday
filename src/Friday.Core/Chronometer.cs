using System.Text.Json;

namespace Friday.Core;

/// <summary>Bir tur: kaçıncı, önceki turdan bu yana geçen süre ve o ana kadarki toplam.</summary>
public sealed record ChronometerLap(int Number, TimeSpan Split, TimeSpan Total);

/// <summary>
/// Kronometre: başlar, duraklar, kaldığı yerden sürer, tur alır, sıfırlanır. Durumu D:\Friday\data\chronometer.json'da
/// durur; Friday kapanıp açılsa da çalışan kronometre saymaya devam eder (başlangıç anı saklanır). Eskiden yalnızca
/// bellekteydi ve duraklatma, tur, sıfırlama yoktu.
/// </summary>
public sealed class Chronometer
{
    private sealed class State
    {
        public DateTime? RunningSince { get; set; }

        public double AccumulatedSeconds { get; set; }

        public List<double> LapTotals { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string? _file;
    private readonly object _gate = new();
    private State _state = new();

    /// <param name="file">Durumun yazılacağı dosya; null ise yalnızca bellekte tutulur (testler).</param>
    public Chronometer(string? file = null)
    {
        _file = file;
        if (file is not null && File.Exists(file))
        {
            try
            {
                _state = JsonSerializer.Deserialize<State>(File.ReadAllText(file)) ?? new State();
            }
            catch (JsonException)
            {
                _state = new State();
            }
        }
    }

    public event EventHandler? Changed;

    public bool Running
    {
        get
        {
            lock (_gate)
            {
                return _state.RunningSince is not null;
            }
        }
    }

    /// <summary>Çalışıyor ya da duraklatılmış ama sıfırlanmamış.</summary>
    public bool HasTime(DateTime now) => Running || Elapsed(now) > TimeSpan.Zero;

    public TimeSpan Elapsed(DateTime now)
    {
        lock (_gate)
        {
            var running = _state.RunningSince is { } since && now > since ? now - since : TimeSpan.Zero;
            return TimeSpan.FromSeconds(_state.AccumulatedSeconds) + running;
        }
    }

    /// <summary>Turlar, ilki başta.</summary>
    public IReadOnlyList<ChronometerLap> Laps
    {
        get
        {
            lock (_gate)
            {
                var laps = new List<ChronometerLap>();
                var previous = 0.0;
                for (var index = 0; index < _state.LapTotals.Count; index++)
                {
                    var total = _state.LapTotals[index];
                    laps.Add(new ChronometerLap(index + 1, TimeSpan.FromSeconds(total - previous), TimeSpan.FromSeconds(total)));
                    previous = total;
                }

                return laps;
            }
        }
    }

    /// <summary>Başlatır ya da duraklatılmışsa kaldığı yerden sürdürür.</summary>
    /// <returns>Zaten çalışıyorsa false.</returns>
    public bool Start(DateTime now)
    {
        lock (_gate)
        {
            if (_state.RunningSince is not null)
            {
                return false;
            }

            _state.RunningSince = now;
        }

        Save();
        return true;
    }

    /// <summary>Duraklatır; süre korunur.</summary>
    /// <returns>Çalışmıyorsa false.</returns>
    public bool Pause(DateTime now)
    {
        lock (_gate)
        {
            if (_state.RunningSince is not { } since)
            {
                return false;
            }

            _state.AccumulatedSeconds += Math.Max(0, (now - since).TotalSeconds);
            _state.RunningSince = null;
        }

        Save();
        return true;
    }

    /// <summary>O ana kadarki süreyi tur olarak kaydeder; kronometre hiç başlamadıysa null.</summary>
    public ChronometerLap? Lap(DateTime now)
    {
        var total = Elapsed(now);
        if (total <= TimeSpan.Zero)
        {
            return null;
        }

        lock (_gate)
        {
            _state.LapTotals.Add(total.TotalSeconds);
        }

        Save();
        return Laps[^1];
    }

    public void Reset()
    {
        lock (_gate)
        {
            _state = new State();
        }

        Save();
    }

    private void Save()
    {
        if (_file is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                string text;
                lock (_gate)
                {
                    text = JsonSerializer.Serialize(_state, Json);
                }

                File.WriteAllText(_file, text);
            }
            catch (IOException)
            {
                // Kaydedilemese de kronometre bellekte çalışmaya devam eder.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"1:02:09", "12:34" (saat yoksa dakika:saniye).</summary>
    public static string Clock(TimeSpan span) => RhythmText.Clock(span);
}
