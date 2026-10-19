using Friday.Core;
using NAudio.Wave;

namespace Friday.Infrastructure;

/// <summary>
/// Mikrofonu yalnızca bir soru boyunca açar. Kısayola basılınca açılır, konuşma bitince ya da kısayola tekrar
/// basılınca kapanır; aradaki zamanda mikrofon cihazı hiç açık değildir. Ses yalnızca bellekte tutulur.
/// </summary>
public sealed class MicrophoneRecorder
{
    public const int SampleRate = 16_000;

    /// <summary>0–1 arası anlık ses seviyesi.</summary>
    public event EventHandler<float>? LevelChanged;

    /// <summary>Kaydı yapar ve konuşma yoksa boş dizi döndürür.</summary>
    /// <param name="noSpeechTimeout">Bu sürede konuşma başlamazsa mikrofon kapanır.</param>
    /// <param name="stopRequested">İptal edilirse kayıt o ana kadar duyulanla biter (kısayola ikinci basış).</param>
    public async Task<float[]> RecordAsync(
        FridaySettings settings, TimeSpan noSpeechTimeout, CancellationToken stopRequested, CancellationToken cancellationToken)
    {
        if (WaveIn.DeviceCount == 0)
        {
            throw new InvalidOperationException("Mikrofon bulunamadı.");
        }

        var detector = new SpeechEndpointDetector(
            SampleRate,
            TimeSpan.FromMilliseconds(settings.SilenceMilliseconds),
            noSpeechTimeout,
            TimeSpan.FromSeconds(settings.MaxRecordSeconds));
        var samples = new List<float>(SampleRate * 8);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();

        using var microphone = new WaveIn
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 40,
            NumberOfBuffers = 4
        };

        microphone.DataAvailable += (_, e) =>
        {
            var frame = new float[e.BytesRecorded / 2];
            for (var index = 0; index < frame.Length; index++)
            {
                frame[index] = BitConverter.ToInt16(e.Buffer, index * 2) / 32768f;
            }

            EndpointState state;
            lock (gate)
            {
                samples.AddRange(frame);
                state = detector.Process(frame);
            }

            LevelChanged?.Invoke(this, detector.Level);
            if (state is EndpointState.Finished or EndpointState.NoSpeech)
            {
                completion.TrySetResult();
            }
        };
        microphone.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                completion.TrySetException(e.Exception);
            }
            else
            {
                completion.TrySetResult();
            }
        };

        using var stopRegistration = stopRequested.Register(() => completion.TrySetResult());
        using var cancelRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        microphone.StartRecording();
        try
        {
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            microphone.StopRecording();
            LevelChanged?.Invoke(this, 0);
        }

        lock (gate)
        {
            // Tuşla erken bitirilen kayıtta dedektör henüz "konuşuyor" diyor olabilir; bu da geçerli bir kayıttır.
            return detector.State is EndpointState.Speaking or EndpointState.Finished ? [.. samples] : [];
        }
    }
}
