using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Friday.Infrastructure;

/// <summary>
/// Tamamen yerel yedek ses. Piper süreci açık tutulur; her cümle stdin'e bir satır olarak yazılır, ham PCM stdout'tan
/// okunur. Piper her cümleden sonra stderr'e "Real-time factor" yazar; cümlenin bittiği buradan anlaşılır.
/// </summary>
internal sealed class PiperSynthesizer : IDisposable
{
    private readonly string _piperExe;
    private readonly string _voice;
    private readonly double _speed;
    private readonly double _noiseScale;
    private readonly double _noiseW;
    private readonly object _gate = new();
    private Process? _process;
    private MemoryStream _audio = new();
    private TaskCompletionSource? _sentenceDone;
    private DateTime _lastAudio;

    /// <param name="noiseScale">Ton değişkenliği (Piper varsayılanı 0,667).</param>
    /// <param name="noiseW">Hece süresi değişkenliği; yüksek değer ritmi mekaniklikten kurtarır.</param>
    public PiperSynthesizer(string piperExe, string voice, double speed, double noiseScale = 0.667, double noiseW = 0.9)
    {
        _noiseScale = Math.Clamp(noiseScale, 0, 1.5);
        _noiseW = Math.Clamp(noiseW, 0, 1.5);
        if (!File.Exists(piperExe))
        {
            throw new FileNotFoundException($"Piper bulunamadı: {piperExe}. setup.ps1'i çalıştırın.");
        }

        if (!File.Exists(voice))
        {
            throw new FileNotFoundException($"Ses modeli bulunamadı: {voice}. setup.ps1'i çalıştırın.");
        }

        _piperExe = piperExe;
        _voice = voice;
        _speed = Math.Clamp(speed, 0.5, 2);
        SampleRate = ReadSampleRate(voice);
    }

    public int SampleRate { get; }

    public void WarmUp()
    {
        lock (_gate)
        {
            EnsureProcess();
        }
    }

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        TaskCompletionSource done;
        lock (_gate)
        {
            var process = EnsureProcess();
            _audio = new MemoryStream();
            done = _sentenceDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.StandardInput.WriteLine(text.Replace('\n', ' ').Replace('\r', ' '));
            process.StandardInput.Flush();
        }

        using (cancellationToken.Register(() => { Kill(); done.TrySetCanceled(cancellationToken); }))
        {
            await done.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }

        // stdout ve stderr ayrı borular; son ses baytları bitiş satırından biraz sonra gelebilir.
        while (DateTime.UtcNow - _lastAudio < TimeSpan.FromMilliseconds(120))
        {
            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            var bytes = _audio.ToArray();
            return bytes.Length % 2 == 0 ? bytes : bytes[..^1];
        }
    }

    private Process EnsureProcess()
    {
        if (_process is { HasExited: false })
        {
            return _process;
        }

        var start = new ProcessStartInfo(_piperExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(_piperExe)!
        };
        foreach (var argument in new[]
                 {
                     "--model", _voice, "--output_raw",
                     // 0,97: seçilen örnekteki biraz canlı tempo; ayardaki hız bunun üstüne uygulanır.
                     "--length_scale", (0.97 / _speed).ToString("0.00", CultureInfo.InvariantCulture),
                     "--noise_scale", _noiseScale.ToString("0.000", CultureInfo.InvariantCulture),
                     "--noise_w", _noiseW.ToString("0.000", CultureInfo.InvariantCulture),
                     "--sentence_silence", "0.1"
                 })
        {
            start.ArgumentList.Add(argument);
        }

        var process = ChildProcessJob.Shared.Start(start) ?? throw new InvalidOperationException("Piper başlatılamadı.");
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data?.Contains("Real-time factor", StringComparison.Ordinal) == true)
            {
                _sentenceDone?.TrySetResult();
            }
        };
        process.BeginErrorReadLine();
        _process = process;
        _ = Task.Run(() => Pump(process));
        return process;
    }

    private void Pump(Process process)
    {
        var chunk = new byte[8192];
        try
        {
            int read;
            while ((read = process.StandardOutput.BaseStream.Read(chunk, 0, chunk.Length)) > 0)
            {
                lock (_gate)
                {
                    _audio.Write(chunk, 0, read);
                    _lastAudio = DateTime.UtcNow;
                }
            }
        }
        catch (IOException)
        {
            // Konuşma kesilince süreç öldürülür; akışın kopması beklenen durumdur.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Boştayken Piper süreci kapatılır; bir sonraki cümlede ya da kısayolda yeniden açılır.</summary>
    public void Sleep() => Kill();

    private void Kill()
    {
        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                _process.Kill();
                _process.WaitForExit(TimeSpan.FromSeconds(2));
            }

            _process?.Dispose();
            _process = null;
        }
    }

    private static int ReadSampleRate(string voice)
    {
        var config = voice + ".json";
        return File.Exists(config)
            ? JsonNode.Parse(File.ReadAllText(config))?["audio"]?["sample_rate"]?.GetValue<int>() ?? 22_050
            : 22_050;
    }

    public void Dispose() => Kill();
}
