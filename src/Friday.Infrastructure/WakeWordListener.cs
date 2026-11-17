using System.Threading.Channels;
using Microsoft.ML.OnnxRuntime;
using NAudio.Wave;

namespace Friday.Infrastructure;

/// <summary>
/// Uyandırma kelimesini ("Hey Friday") bekler. Mikrofon açık kalır ama ses 80 ms'lik parçalar halinde yalnızca bellekte işlenip atılır;
/// diske yazılmaz, hiçbir yere gönderilmez. Ses kartının iş parçacığı yalnızca kopyalar; modeller tek bir arka plan
/// iş parçacığında çalışır, böylece ses sürücüsü hiç bekletilmez. Bir mikrofon çıkarılırsa birkaç saniye sonra
/// kendiliğinden yeniden açılır.
/// </summary>
public sealed class WakeWordListener : IDisposable
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gerçek uyandırma kelimesi eşiği art arda 2–9 parça (160–720 ms) boyunca geçer; benzer sesli cümlelerdeki yanlış
    /// alarmların çoğu ise tek parçada kalır. İki parça şartı gecikmeye yalnızca 80 ms ekler.
    /// </summary>
    private const int RequiredHits = 2;

    private readonly string _folder;
    private readonly object _gate = new();
    private readonly List<Slot> _slots = [];
    private Channel<(Slot Slot, short[] Samples, int Count)>? _audio;
    private Task? _worker;
    private Timer? _retry;
    private WakeWordEngine? _engine;
    private string _model = "";
    private float _threshold = 0.5f;
    private string _devices = "";
    private DateTime _quietUntil = DateTime.MinValue;
    private volatile bool _suspended;
    private bool _disposed;

    public WakeWordListener(string folder) => _folder = folder;

    /// <summary>Uyandırma kelimesi duyuldu; değer onu duyan mikrofonun adıdır.</summary>
    public event EventHandler<string>? Detected;

    /// <summary>Mikrofon açılamadı ya da kapandı (yeniden denenecek).</summary>
    public event EventHandler<string>? Problem;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _slots.Count > 0;
            }
        }
    }

    /// <summary>
    /// Asistan zaten dinlerken ses işlenmez (işlemci boşta kalır, kullanıcının sorusu kelimeyi yeniden tetiklemez).
    /// Kalkınca ilk tahminler yok sayılır; arabellekteki eski ses yanlış alarm vermez.
    /// </summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended && !value)
            {
                lock (_gate)
                {
                    foreach (var slot in _slots)
                    {
                        slot.ResetPending = true;
                    }
                }
            }

            _suspended = value;
        }
    }

    public static bool IsInstalled(string folder, string model) => WakeWordEngine.IsInstalled(folder, model);

    /// <summary>Dinlemeyi başlatır ya da yeni ayarlarla yeniden başlatır.</summary>
    /// <param name="devicePreference">Ayardaki mikrofon adı; boşsa bütün mikrofonlar.</param>
    public void Start(string model, double threshold, string devicePreference)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CloseDevices();
            if (_engine is null || _model != model)
            {
                _engine?.Dispose();
                _engine = new WakeWordEngine(_folder, model);
                _model = model;
            }

            _threshold = (float)Math.Clamp(threshold, 0.05, 0.99);
            _devices = devicePreference;
            if (_audio is null)
            {
                // Taşma olursa en eski parça atılır: işlemci bir an yetişemezse gecikme birikmez.
                _audio = Channel.CreateBounded<(Slot, short[], int)>(new BoundedChannelOptions(64)
                {
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });
                _worker = Task.Run(() => WorkAsync(_audio.Reader));
            }

            OpenDevices();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _retry?.Dispose();
            _retry = null;
            CloseDevices();
        }
    }

    private void OpenDevices()
    {
        var engine = _engine!;
        var writer = _audio!.Writer;
        foreach (var device in MicrophoneRecorder.Devices(_devices))
        {
            var slot = new Slot(MicrophoneRecorder.DeviceName(device), engine.CreateStream());
            var input = new WaveIn
            {
                DeviceNumber = device,
                WaveFormat = new WaveFormat(MicrophoneRecorder.SampleRate, 16, 1),
                BufferMilliseconds = 80,
                NumberOfBuffers = 3
            };
            slot.Input = input;
            input.DataAvailable += (_, e) =>
            {
                if (_suspended || e.BytesRecorded < 2)
                {
                    return;
                }

                var samples = new short[e.BytesRecorded / 2];
                Buffer.BlockCopy(e.Buffer, 0, samples, 0, samples.Length * 2);
                writer.TryWrite((slot, samples, samples.Length));
            };
            input.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null)
                {
                    Problem?.Invoke(this, $"{slot.Name}: {e.Exception.Message}");
                    ScheduleRetry();
                }
            };

            try
            {
                input.StartRecording();
                _slots.Add(slot);
            }
            catch (NAudio.MmException)
            {
                // Başka bir uygulamanın kilitlediği ya da bozuk cihazlar atlanır.
                input.Dispose();
            }
        }

        if (_slots.Count == 0)
        {
            Problem?.Invoke(this, "Mikrofon açılamadı; birkaç saniye sonra yeniden denenecek.");
            ScheduleRetry();
            return;
        }

        FridayLog.Info($"Uyandırma kelimesi dinleniyor: {string.Join(", ", _slots.Select(slot => slot.Name))}");
    }

    private void CloseDevices()
    {
        foreach (var slot in _slots)
        {
            slot.Closed = true;
            try
            {
                slot.Input?.StopRecording();
            }
            catch (NAudio.MmException)
            {
                // Çıkarılmış cihaz zaten kapalıdır.
            }

            slot.Input?.Dispose();
        }

        _slots.Clear();
    }

    /// <summary>Çıkarılan ya da hata veren mikrofonlar için bütün cihazlar kısa bir süre sonra yeniden açılır.</summary>
    private void ScheduleRetry()
    {
        lock (_gate)
        {
            if (_disposed || _retry is not null)
            {
                return;
            }

            _retry = new Timer(_ =>
            {
                lock (_gate)
                {
                    _retry?.Dispose();
                    _retry = null;
                    if (_disposed || _engine is null)
                    {
                        return;
                    }

                    CloseDevices();
                    OpenDevices();
                }
            }, null, RetryDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task WorkAsync(ChannelReader<(Slot Slot, short[] Samples, int Count)> reader)
    {
        await foreach (var (slot, samples, count) in reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (slot.Closed || _suspended)
            {
                continue;
            }

            if (slot.ResetPending)
            {
                slot.ResetPending = false;
                slot.Stream.Reset();
                slot.Hits = 0;
            }

            try
            {
                var offset = 0;
                while (offset < count)
                {
                    var take = Math.Min(count - offset, WakeWordEngine.ChunkSamples - slot.Filled);
                    samples.AsSpan(offset, take).CopyTo(slot.Pending.AsSpan(slot.Filled));
                    slot.Filled += take;
                    offset += take;
                    if (slot.Filled < WakeWordEngine.ChunkSamples)
                    {
                        break;
                    }

                    slot.Filled = 0;
                    var score = slot.Stream.Process(slot.Pending);
                    slot.Hits = score >= _threshold ? slot.Hits + 1 : 0;
                    if (slot.Hits >= RequiredHits && DateTime.UtcNow >= _quietUntil)
                    {
                        _quietUntil = DateTime.UtcNow + Cooldown;
                        ResetAll();
                        var name = slot.Name;
                        _ = Task.Run(() => Detected?.Invoke(this, name));
                    }
                }
            }
            catch (OnnxRuntimeException exception)
            {
                Problem?.Invoke(this, exception.Message);
            }
        }
    }

    private void ResetAll()
    {
        lock (_gate)
        {
            foreach (var slot in _slots)
            {
                slot.Stream.Reset();
                slot.Hits = 0;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _retry?.Dispose();
            CloseDevices();
            _audio?.Writer.TryComplete();
        }

        _worker?.Wait(TimeSpan.FromSeconds(2));
        _engine?.Dispose();
    }

    private sealed class Slot(string name, WakeWordTracker stream)
    {
        public string Name { get; } = name;

        public WakeWordTracker Stream { get; } = stream;

        /// <summary>Eşiği art arda geçen parça sayısı.</summary>
        public int Hits { get; set; }

        public WaveIn? Input { get; set; }

        public short[] Pending { get; } = new short[WakeWordEngine.ChunkSamples];

        public int Filled { get; set; }

        public volatile bool ResetPending;

        public volatile bool Closed;
    }
}
