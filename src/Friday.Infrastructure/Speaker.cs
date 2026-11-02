using System.Threading.Channels;
using Friday.Core;
using NAudio.Wave;

namespace Friday.Infrastructure;

/// <summary>
/// Cümleleri sırayla seslendirir. Birincil motor XTTS (doğal, tamamen yerel) ya da Azure nöral sestir; seçilen motor
/// hata verirse o cümle yerel Piper ile okunur ve bir dakika sonra birincil motor yeniden denenir. Bir cümle çalarken
/// bir sonraki hazırlanır.
/// </summary>
public sealed class Speaker : IDisposable
{
    private const int NeuralRate = 24_000;
    private static readonly TimeSpan PrimaryCooldown = TimeSpan.FromMinutes(1);

    private readonly FridayPaths _paths;
    private readonly object _gate = new();
    private readonly Channel<(long Generation, string Text)> _queue = Channel.CreateUnbounded<(long, string)>();
    private readonly CancellationTokenSource _shutdown = new();
    private BufferedWaveProvider? _buffer;
    private WaveOut? _output;
    private XttsSynthesizer? _xtts;
    private AzureSynthesizer? _azure;
    private PiperSynthesizer? _piper;
    private CancellationTokenSource _current = new();
    private long _generation;
    private int _pending;
    private int _onlineCharacters;
    private DateTime _primaryRetryAfter = DateTime.MinValue;
    private int _outputRate = NeuralRate;
    private double _room;
    private Task? _worker;

    public Speaker(FridayPaths paths) => _paths = paths;

    /// <summary>Çalınan sesin seviyesi (0–1), saniyede ~30 kez.</summary>
    public event EventHandler<float>? LevelChanged;

    /// <summary>Birincil motor kullanılamayıp Piper'a düşüldüğünde (neden).</summary>
    public event EventHandler<string>? FellBack;

    public string EngineLabel { get; private set; } = "";

    public void Configure(FridaySettings settings, string piperVoice)
    {
        lock (_gate)
        {
            _azure?.Dispose();
            _azure = null;
            _piper?.Dispose();
            _piper = new PiperSynthesizer(_paths.PiperExe, piperVoice, settings.SpeechSpeed, settings.PiperNoiseScale, settings.PiperNoiseW);
            _room = Math.Clamp(settings.VoiceRoom, 0, 0.3);
            _primaryRetryAfter = DateTime.MinValue;

            var engine = settings.SpeechEngine.Trim().ToLowerInvariant();
            var hasAzureKey = !string.IsNullOrWhiteSpace(settings.AzureSpeechKey);
            var xttsReady = XttsSynthesizer.IsInstalled(_paths);
            var useXtts = (engine == "xtts" || engine == "auto") && xttsReady;
            if (!useXtts)
            {
                _xtts?.Dispose();
                _xtts = null;
            }

            if (useXtts)
            {
                // Sunucu açıksa korunur; yalnızca ses ve hız değişir (yeniden başlatma ~15 sn sürerdi).
                _xtts ??= new XttsSynthesizer(_paths, settings);
                _xtts.Update(settings);
                EngineLabel = $"XTTS yerel doğal ses ({_xtts.VoiceName})";
            }
            else if ((engine == "azure" || engine == "auto") && hasAzureKey)
            {
                _azure = new AzureSynthesizer(settings.AzureSpeechKey.Trim(), settings.AzureSpeechRegion.Trim(),
                    settings.AzureVoice, settings.SpeechSpeed);
                EngineLabel = $"Azure nöral ses ({settings.AzureVoice.Replace("tr-TR-", "", StringComparison.Ordinal).Replace("Neural", "", StringComparison.Ordinal)})";
            }
            else
            {
                EngineLabel = "Piper (yerel)";
            }

            _output?.Dispose();
            // Piper birincilse çıkış onun kendi örnekleme hızındadır; yeniden örnekleme sesi matlaştırırdı.
            _outputRate = _xtts is null && _azure is null ? _piper.SampleRate : NeuralRate;
            _buffer = new BufferedWaveProvider(new WaveFormat(_outputRate, 16, 1), TimeSpan.FromMinutes(5))
            {
                // Boşken sessizlik verir; çıkış cihazı sürekli açık kalır, cümleler arasında tık sesi olmaz.
                ReadFully = true
            };
            var meter = new LevelMeter(_buffer);
            meter.LevelChanged += (_, level) => LevelChanged?.Invoke(this, level);
            _output = new WaveOut { BufferMilliseconds = 60, NumberOfBuffers = 3 };
            _output.Init(meter);
            _output.Play();
            _worker ??= Task.Run(WorkAsync);
        }
    }

    /// <summary>Motorları önceden hazırlar; ilk cümlede bağlantı/model yükleme beklenmez.</summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        _piper?.WarmUp();
        try
        {
            _azure?.WarmUp();
            if (_xtts is not null)
            {
                await _xtts.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is ApplicationException or InvalidOperationException or HttpRequestException or TimeoutException)
        {
            // İlk cümlede yeniden denenir; olmazsa Piper okur.
            FellBack?.Invoke(this, exception.Message);
        }
    }

    /// <summary>Kısayola basılınca Piper'ı başlatır (~0,3 sn) ve XTTS'i GPU'ya alır (RAM'den ~1 sn).</summary>
    public void Prewarm()
    {
        _piper?.WarmUp();
        if (_xtts is { } xtts)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await xtts.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TimeoutException)
                {
                    // Asıl cümle hatayı yeniden yakalar.
                }
            });
        }
    }

    /// <summary>Boşta kalınca Piper kapanır (~100 MB RAM) ve XTTS RAM'e taşınır (VRAM boşalır).</summary>
    public Task SleepAsync()
    {
        _piper?.Sleep();
        return _xtts?.UnloadAsync() ?? Task.CompletedTask;
    }

    public void Speak(string sentence)
    {
        var text = sentence.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Interlocked.Increment(ref _pending);
        _queue.Writer.TryWrite((Interlocked.Read(ref _generation), text));
    }

    /// <summary>Son çağrıdan bu yana Azure'a gönderilen karakter sayısı (gizlilik defteri için).</summary>
    public int TakeOnlineCharacters() => Interlocked.Exchange(ref _onlineCharacters, 0);

    /// <summary>Sıradaki tüm cümleler okunana kadar bekler.</summary>
    public async Task WaitUntilDoneAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsBusy())
        {
            await Task.Delay(60, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Konuşmayı hemen keser: sıradaki cümleler atılır, çalan ses susar.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _generation);
            while (_queue.Reader.TryRead(out _))
            {
            }

            _pending = 0;
            _current.Cancel();
            _current.Dispose();
            _current = new CancellationTokenSource();
            _buffer?.ClearBuffer();
        }
    }

    private bool IsBusy()
    {
        lock (_gate)
        {
            return Volatile.Read(ref _pending) > 0 || (_buffer?.BufferedDuration ?? TimeSpan.Zero) > TimeSpan.FromMilliseconds(30);
        }
    }

    private async Task WorkAsync()
    {
        await foreach (var (generation, text) in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
        {
            CancellationToken token;
            lock (_gate)
            {
                token = _current.Token;
            }

            try
            {
                if (generation == Interlocked.Read(ref _generation))
                {
                    await SynthesizeAsync(text, generation, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
            {
                FellBack?.Invoke(this, exception.Message);
            }
            finally
            {
                if (generation == Interlocked.Read(ref _generation) && Volatile.Read(ref _pending) > 0)
                {
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
    }

    private async Task SynthesizeAsync(string text, long generation, CancellationToken cancellationToken)
    {
        void Add(byte[] pcm)
        {
            lock (_gate)
            {
                if (generation == Interlocked.Read(ref _generation) && pcm.Length > 0)
                {
                    _buffer?.AddSamples(pcm, 0, pcm.Length);
                }
            }
        }

        if (DateTime.UtcNow >= _primaryRetryAfter)
        {
            try
            {
                if (_xtts is not null)
                {
                    await _xtts.SynthesizeAsync(text, Add, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (_azure is not null)
                {
                    await _azure.SynthesizeAsync(text, Add, cancellationToken).ConfigureAwait(false);
                    Interlocked.Add(ref _onlineCharacters, text.Length);
                    return;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TimeoutException)
            {
                // Sunucu çöktü, internet yok ya da anahtar geçersiz: bir dakika Piper'la devam, sonra yeniden denenir.
                _primaryRetryAfter = DateTime.UtcNow + PrimaryCooldown;
                FellBack?.Invoke(this, exception.Message);
            }
        }

        if (_piper is null)
        {
            return;
        }

        var pcm = await _piper.SynthesizeAsync(text, cancellationToken).ConfigureAwait(false);
        if (_room > 0)
        {
            var samples = new short[pcm.Length / 2];
            Buffer.BlockCopy(pcm, 0, samples, 0, samples.Length * 2);
            var warm = VoiceWarmth.Process(samples, _piper.SampleRate, _room);
            pcm = new byte[warm.Length * 2];
            Buffer.BlockCopy(warm, 0, pcm, 0, pcm.Length);
        }

        Add(Resample(pcm, _piper.SampleRate, _outputRate));
    }

    /// <summary>16 bit mono PCM'i doğrusal aradeğerlemeyle yeniden örnekler (Piper 22 050 Hz → 24 000 Hz).</summary>
    internal static byte[] Resample(byte[] pcm, int from, int to)
    {
        if (from == to || pcm.Length < 4)
        {
            return pcm;
        }

        var source = new short[pcm.Length / 2];
        Buffer.BlockCopy(pcm, 0, source, 0, source.Length * 2);
        var length = (int)((long)source.Length * to / from);
        var target = new short[length];
        var ratio = (double)from / to;
        for (var index = 0; index < length; index++)
        {
            var position = index * ratio;
            var left = (int)position;
            var right = Math.Min(left + 1, source.Length - 1);
            var fraction = position - left;
            target[index] = (short)Math.Round(source[left] + ((source[right] - source[left]) * fraction));
        }

        var bytes = new byte[length * 2];
        Buffer.BlockCopy(target, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _queue.Writer.TryComplete();
        lock (_gate)
        {
            _current.Cancel();
            _current.Dispose();
            _xtts?.Dispose();
            _azure?.Dispose();
            _piper?.Dispose();
            _output?.Dispose();
        }

        _shutdown.Dispose();
    }
}
