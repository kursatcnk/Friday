using Friday.Core;
using NAudio.Wave;

namespace Friday.Infrastructure;

/// <summary>Bir kaydın sesi ve, kullanıcı tahminden sonra konuşmadıysa, hazır yazısı.</summary>
public sealed record Recording(float[] Audio, string? Transcript);

/// <summary>
/// Mikrofonu yalnızca bir soru boyunca açar. Kısayola basılınca açılır, konuşma bitince ya da kısayola tekrar
/// basılınca kapanır; aradaki zamanda mikrofon cihazı hiç açık değildir. Ses yalnızca bellekte tutulur.
/// </summary>
/// <remarks>
/// Windows'un varsayılan mikrofonu sanal bir cihaz olabilir (ör. SteelSeries Sonar) ve hiç ses vermeyebilir; gürültü
/// kapılı kulaklıklar da sessizken tam sıfır verir, yani ikisi sessizlikte ayırt edilemez. Bu yüzden ayarda belirli
/// bir mikrofon seçilmediyse bütün mikrofonlar aynı anda dinlenir, konuşmayı ilk yakalayan seçilir ve diğerleri
/// hemen kapatılır.
/// </remarks>
public sealed class MicrophoneRecorder
{
    public const int SampleRate = 16_000;

    /// <summary>0–1 arası anlık ses seviyesi.</summary>
    public event EventHandler<float>? LevelChanged;

    /// <summary>Son kaydın yapıldığı cihaz (gizlilik defteri ve durum satırı için).</summary>
    public string LastDeviceName { get; private set; } = "";

    /// <summary>Son kayıttaki en yüksek seviye (0–1).</summary>
    public float LastPeak { get; private set; }

    /// <summary>Kullanıcı bu kadar susunca o ana kadarki ses tahminen yazıya dökülür.</summary>
    private static readonly TimeSpan SpeculateAfter = TimeSpan.FromMilliseconds(500);

    /// <summary>Kaydı yapar; konuşma yoksa ses boştur.</summary>
    /// <param name="noSpeechTimeout">Bu sürede konuşma başlamazsa mikrofon kapanır.</param>
    /// <param name="stopRequested">İptal edilirse kayıt o ana kadar duyulanla biter (kısayola ikinci basış).</param>
    /// <param name="onlyDevice">Uyandırma kelimesini duyan mikrofon; verilirse yalnızca o açılır.</param>
    /// <param name="speculate">
    /// Kullanıcı yarım saniye susunca o ana kadarki ses buna verilir (yazıya dökme). Yazı tamamlanmış bir komutsa
    /// (Complete) kayıt, 1,2 sn'lik sessizliği beklemeden hemen biter. Kullanıcı sonra konuşmazsa aynı yazı kayıtla
    /// birlikte döner ve yeniden yazıya dökülmez; konuşursa atılır.
    /// </param>
    public async Task<Recording> RecordAsync(
        FridaySettings settings, TimeSpan noSpeechTimeout, CancellationToken stopRequested, CancellationToken cancellationToken,
        string? onlyDevice = null, Func<float[], CancellationToken, Task<(string Text, bool Complete)>>? speculate = null)
    {
        Task<string?>? speculation = null;
        long speculatedAt = -1;
        if (WaveIn.DeviceCount == 0)
        {
            throw new InvalidOperationException("Mikrofon bulunamadı.");
        }

        var devices = Devices(onlyDevice ?? settings.MicrophoneDevice);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();
        Channel? lead = null;
        var channels = new List<Channel>();

        foreach (var device in devices)
        {
            var channel = new Channel(device, new SpeechEndpointDetector(
                SampleRate,
                TimeSpan.FromMilliseconds(settings.SilenceMilliseconds),
                noSpeechTimeout,
                TimeSpan.FromSeconds(settings.MaxRecordSeconds)));
            channel.Input.DataAvailable += (_, e) =>
            {
                var frame = new float[e.BytesRecorded / 2];
                for (var index = 0; index < frame.Length; index++)
                {
                    frame[index] = BitConverter.ToInt16(e.Buffer, index * 2) / 32768f;
                }

                lock (gate)
                {
                    if (lead is not null && lead != channel)
                    {
                        return;
                    }

                    channel.Add(frame);
                    // Konuşmayı ilk yakalayan mikrofon seçilir; diğerlerinin sesi bundan sonra yok sayılır. Burada
                    // durdurulmazlar: kilit tutulurken başka bir cihazın iş parçacığını beklemek kilitlenmeye yol açabilir.
                    if (lead is null && channel.Detector.State == EndpointState.Speaking)
                    {
                        lead = channel;
                    }

                    var level = lead is null ? channels.Max(item => item.Detector.Level) : lead.Detector.Level;
                    LevelChanged?.Invoke(this, level);

                    var everyoneTimedOut = lead is null && channels.All(item => item.Detector.State == EndpointState.NoSpeech);
                    if ((lead is not null && lead.Detector.State is EndpointState.Finished or EndpointState.NoSpeech) || everyoneTimedOut)
                    {
                        completion.TrySetResult();
                    }
                    else if (speculate is not null && lead == channel && channel.Detector.State == EndpointState.Speaking
                             && channel.Detector.TrailingSilence >= SpeculateAfter && channel.Detector.LastSpeechAt != speculatedAt)
                    {
                        var at = speculatedAt = channel.Detector.LastSpeechAt;
                        var snapshot = Normalize([.. channel.Samples]);
                        speculation = Task.Run(async () =>
                        {
                            try
                            {
                                var (text, complete) = await speculate(snapshot, cancellationToken).ConfigureAwait(false);
                                lock (gate)
                                {
                                    if (complete && channel.Detector.LastSpeechAt == at)
                                    {
                                        completion.TrySetResult();
                                    }
                                }

                                return text;
                            }
                            catch (Exception exception) when (exception is not OutOfMemoryException)
                            {
                                // Tahmin başarısızsa kayıt normal biter ve ses yeniden yazıya dökülür.
                                return null;
                            }
                        }, CancellationToken.None);
                    }
                }
            };
            channel.Input.RecordingStopped += (_, e) =>
            {
                // Bir mikrofonun hata vermesi kaydı bitirmez; yalnızca seçilen mikrofon hata verirse biter.
                if (e.Exception is not null && lead == channel)
                {
                    completion.TrySetException(e.Exception);
                }
            };

            try
            {
                channel.Input.StartRecording();
                channels.Add(channel);
            }
            catch (NAudio.MmException)
            {
                // Başka bir uygulamanın kilitlediği ya da bozuk cihazlar atlanır.
                channel.Dispose();
            }
        }

        if (channels.Count == 0)
        {
            throw new InvalidOperationException("Hiçbir mikrofon açılamadı.");
        }

        using var stopRegistration = stopRequested.Register(() => completion.TrySetResult());
        using var cancelRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }

            LevelChanged?.Invoke(this, 0);
        }

        float[] audio;
        bool reusable;
        lock (gate)
        {
            var chosen = lead ?? channels.OrderByDescending(channel => channel.Peak).First();
            LastDeviceName = chosen.Name;
            LastPeak = chosen.Peak;
            // Tuşla erken bitirilen kayıtta dedektör henüz "konuşuyor" diyor olabilir; bu da geçerli bir kayıttır.
            audio = lead is not null && lead.Detector.State is EndpointState.Speaking or EndpointState.Finished
                ? Normalize([.. lead.Samples])
                : [];
            reusable = audio.Length > 0 && lead!.Detector.LastSpeechAt == speculatedAt;
        }

        var transcript = reusable && speculation is not null ? await speculation.ConfigureAwait(false) : null;
        return new Recording(audio, transcript);
    }

    /// <summary>Ayarda adı verilen mikrofon; yoksa bilgisayardaki bütün mikrofonlar.</summary>
    internal static List<int> Devices(string preference)
    {
        var all = Enumerable.Range(0, WaveIn.DeviceCount).ToList();
        if (string.IsNullOrWhiteSpace(preference))
        {
            return all;
        }

        var chosen = all.Where(device => DeviceName(device).Contains(preference.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return chosen.Count > 0 ? chosen : all;
    }

    /// <summary>Kısık kayıtları Whisper'a vermeden önce yükseltir (en fazla 10 kat); gürültü yükseltilmez.</summary>
    internal static float[] Normalize(float[] samples)
    {
        if (samples.Length == 0)
        {
            return samples;
        }

        var peak = samples.Max(Math.Abs);
        if (peak is < 0.002f or > 0.5f)
        {
            return samples;
        }

        var gain = Math.Min(0.9f / peak, 10f);
        return [.. samples.Select(sample => sample * gain)];
    }

    /// <summary>Bilgisayardaki kayıt cihazlarının adları (ayarlardaki seçici için).</summary>
    public static IReadOnlyList<string> DeviceNames() =>
        [.. Enumerable.Range(0, WaveIn.DeviceCount).Select(DeviceName)];

    internal static string DeviceName(int device) => WaveIn.GetCapabilities(device).ProductName.Trim();

    /// <summary>Tek bir mikrofonun kaydı, dedektörü ve en yüksek seviyesi.</summary>
    private sealed class Channel(int device, SpeechEndpointDetector detector) : IDisposable
    {
        private bool _stopped;

        public WaveIn Input { get; } = new()
        {
            DeviceNumber = device,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 40,
            NumberOfBuffers = 4
        };

        public string Name { get; } = DeviceName(device);

        public SpeechEndpointDetector Detector { get; } = detector;

        public List<float> Samples { get; } = new(SampleRate * 8);

        public float Peak { get; private set; }

        public void Add(float[] frame)
        {
            Samples.AddRange(frame);
            foreach (var sample in frame)
            {
                Peak = Math.Max(Peak, Math.Abs(sample));
            }

            Detector.Process(frame);
        }

        public void Stop()
        {
            if (!_stopped)
            {
                _stopped = true;
                Input.StopRecording();
            }
        }

        public void Dispose()
        {
            Stop();
            Input.Dispose();
        }
    }
}
