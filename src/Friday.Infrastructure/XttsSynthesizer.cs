using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Tamamen yerel, doğal Türkçe ses (XTTS v2). Python sunucusunu Friday başlatır ve kapatır; sunucu yalnızca
/// 127.0.0.1'i dinler. Model boştayken RAM'de, konuşurken GPU'dadır (~2 GB VRAM).
/// </summary>
public sealed class XttsSynthesizer : IDisposable
{
    private const int Port = 11436;

    private readonly FridayPaths _paths;
    private readonly HttpClient _http = new() { BaseAddress = new Uri($"http://127.0.0.1:{Port}/"), Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private string _speaker = "";
    private string _speakerWav = "";
    private double _speed = 1;
    private Process? _process;

    public XttsSynthesizer(FridayPaths paths, FridaySettings settings)
    {
        _paths = paths;
        Update(settings);
    }

    /// <summary>Sesi ve hızı değiştirir; sunucu ve model yerinde kalır.</summary>
    public void Update(FridaySettings settings)
    {
        _speaker = settings.XttsSpeaker;
        _speakerWav = File.Exists(settings.XttsVoiceSample) ? Path.GetFullPath(settings.XttsVoiceSample) : "";
        _speed = Math.Clamp(settings.SpeechSpeed, 0.7, 1.5);
    }

    public static int SampleRate => 24_000;

    /// <summary>
    /// Türkçe seçmelerinde Whisper'ın hatasız anladığı hazır sesler (ilk ses ~0,25 sn). Hangisinin en doğal
    /// geldiği kulağa kalmış; panelden dinlenip seçilir.
    /// </summary>
    public static IReadOnlyList<(string Name, string Label)> RecommendedVoices { get; } =
    [
        ("Ana Florence", "Ana · kadın"),
        ("Asya Anara", "Asya · kadın"),
        ("Daisy Studious", "Daisy · kadın"),
        ("Sofia Hellen", "Sofia · kadın"),
        ("Alma María", "Alma · kadın"),
        ("Gracie Wise", "Gracie · kadın"),
        ("Alison Dietlinde", "Alison · kadın"),
        ("Viktor Eka", "Viktor · erkek"),
        ("Andrew Chipper", "Andrew · erkek"),
        ("Damien Black", "Damien · erkek"),
        ("Badr Odhiambo", "Badr · erkek"),
    ];

    public static bool IsInstalled(FridayPaths paths) =>
        File.Exists(PythonPath(paths)) && File.Exists(Path.Combine(paths.Models, "xtts", "model.pth"));

    public string VoiceName => _speakerWav.Length > 0 ? "senin ses örneğin" : _speaker;

    /// <summary>Sunucuyu başlatır ve modeli GPU'ya alır.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await EnsureServerAsync(cancellationToken).ConfigureAwait(false);
        using var response = await _http.PostAsJsonAsync("load", new { }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Modeli RAM'e taşır; VRAM boşalır, yeniden yükleme ~1 sn sürer.</summary>
    public async Task UnloadAsync()
    {
        if (_process is not { HasExited: false })
        {
            return;
        }

        try
        {
            using var response = await _http.PostAsJsonAsync("unload", new { }).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Sunucu yoksa zaten VRAM kullanmıyordur.
        }
    }

    public async Task SynthesizeAsync(string text, Action<byte[]> sink, CancellationToken cancellationToken)
    {
        await EnsureServerAsync(cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "tts")
        {
            Content = JsonContent.Create(new
            {
                text,
                language = "tr",
                speaker = _speaker,
                speaker_wav = _speakerWav,
                speed = _speed
            })
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"XTTS hatası: {error}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[16_384];
        var carry = -1;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            // 16 bitlik örnekler iki okuma arasında bölünebilir; tek kalan bayt bir sonrakine eklenir.
            var data = buffer.AsSpan(0, read);
            var output = new List<byte>(read + 1);
            if (carry >= 0)
            {
                output.Add((byte)carry);
                carry = -1;
            }

            output.AddRange(data);
            if (output.Count % 2 == 1)
            {
                carry = output[^1];
                output.RemoveAt(output.Count - 1);
            }

            sink([.. output]);
        }
    }

    private async Task EnsureServerAsync(CancellationToken cancellationToken)
    {
        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false })
            {
                return;
            }

            if (!IsInstalled(_paths))
            {
                throw new InvalidOperationException("XTTS kurulu değil. setup-xtts.ps1'i çalıştırın.");
            }

            _process?.Dispose();
            _process = Process.Start(StartInfo()) ?? throw new InvalidOperationException("XTTS sunucusu başlatılamadı.");
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var errors = new System.Text.StringBuilder();
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data?.StartsWith("ready", StringComparison.Ordinal) == true)
                {
                    ready.TrySetResult();
                }
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (errors)
                    {
                        errors.AppendLine(e.Data);
                    }
                }
            };
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => ready.TrySetException(new InvalidOperationException(
                "XTTS sunucusu kapandı: " + LastLines(errors)));
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // İlk açılışta PyTorch ve model yüklenir (~10–20 sn); sonraki açılışlar daha hızlıdır.
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(120), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _startLock.Release();
        }
    }

    private static string LastLines(System.Text.StringBuilder errors)
    {
        lock (errors)
        {
            var lines = errors.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(" | ", lines.TakeLast(3));
        }
    }

    /// <summary>Python ve kütüphaneleri her şeyi D: üzerine yazsın diye ortam yalnızca bu alt süreç için ayarlanır.</summary>
    private ProcessStartInfo StartInfo()
    {
        var home = Path.Combine(_paths.Data, "xtts-home");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(_paths.Temp);
        var script = Path.Combine(AppContext.BaseDirectory, "Voice", "xtts_server.py");
        var start = new ProcessStartInfo(PythonPath(_paths))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = home
        };
        foreach (var argument in new[] { "-u", script, "--model-dir", Path.Combine(_paths.Models, "xtts"), "--port", Port.ToString(CultureInfo.InvariantCulture), "--gpu" })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["PYTHONNOUSERSITE"] = "1";
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["USERPROFILE"] = home;
        start.Environment["HOME"] = home;
        start.Environment["APPDATA"] = Path.Combine(home, "Roaming");
        start.Environment["LOCALAPPDATA"] = Path.Combine(home, "Local");
        start.Environment["TMP"] = _paths.Temp;
        start.Environment["TEMP"] = _paths.Temp;
        start.Environment["HF_HOME"] = Path.Combine(home, "huggingface");
        start.Environment["TORCH_HOME"] = Path.Combine(home, "torch");
        start.Environment["TTS_HOME"] = Path.Combine(home, "tts");
        start.Environment["MPLCONFIGDIR"] = Path.Combine(home, "matplotlib");
        start.Environment["NUMBA_CACHE_DIR"] = Path.Combine(home, "numba");
        start.Environment["CUDA_CACHE_PATH"] = Path.Combine(home, "cuda-cache");
        return start;
    }

    private static string PythonPath(FridayPaths paths) => Path.Combine(paths.Tools, "xtts-env", "Scripts", "python.exe");

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
        }

        _process?.Dispose();
        _http.Dispose();
        _startLock.Dispose();
    }
}
