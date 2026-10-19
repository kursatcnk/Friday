using System.Diagnostics;
using System.Net.Http.Json;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// D:\Friday\tools\ollama içindeki taşınabilir Ollama'yı Friday'e özel bir portta başlatır. Ortam değişkenleri
/// yalnızca bu alt süreç için ayarlanır; modeller ve Ollama'nın kendi dosyaları D: üzerinde kalır.
/// </summary>
public sealed class OllamaServer(FridayPaths paths, HttpClient http) : IDisposable
{
    private Process? _process;
    private int _port = 11435;

    public Uri BaseAddress { get; private set; } = new("http://127.0.0.1:11435/");

    public async Task EnsureRunningAsync(int port, CancellationToken cancellationToken)
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        _port = port;
        if (await IsRespondingAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!File.Exists(paths.OllamaExe))
        {
            throw new FileNotFoundException($"Ollama bulunamadı: {paths.OllamaExe}. setup.ps1'i çalıştırın.");
        }

        _process = Process.Start(StartInfo("serve")) ?? throw new InvalidOperationException("Ollama başlatılamadı.");
        // Ollama'nın sonradan açtığı llama-server da işe girer; Friday çökse bile hepsi onunla kapanır.
        ChildProcessJob.Shared.TryAdd(_process);
        // Çıktılar okunmazsa tampon dolar ve sunucu kilitlenir.
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"Ollama kapandı (çıkış kodu {_process.ExitCode}). Port {port} kullanımda olabilir.");
            }

            if (await IsRespondingAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Ollama 30 saniyede hazır olmadı.");
    }

    /// <summary>
    /// Sohbet için görme modülü (projector, ~1,2 GB VRAM) olmayan bir varyant oluşturur. Aynı model dosyasını
    /// kullanır, diskte yer kaplamaz. Görme yalnızca ekrana bakılırken tam modelle yapılır.
    /// </summary>
    public async Task EnsureTextModelAsync(string textModel, string sourceModel, CancellationToken cancellationToken)
    {
        if (textModel == sourceModel)
        {
            return;
        }

        using (var show = await http.PostAsJsonAsync(new Uri(BaseAddress, "api/show"), new { model = textModel }, cancellationToken)
            .ConfigureAwait(false))
        {
            if (show.IsSuccessStatusCode)
            {
                return;
            }
        }

        var source = (await RunCliAsync($"show {sourceModel} --modelfile", cancellationToken).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.TrimEntries);
        // İlk FROM ana model dosyasıdır; sonrakiler projector ve taslak modeldir, alınmaz.
        var from = source.FirstOrDefault(line => line.StartsWith("FROM ", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"'{sourceModel}' modeli yüklü değil. setup.ps1'i çalıştırın.");
        var lines = new List<string> { from };
        lines.AddRange(source.Where(line =>
            line.StartsWith("RENDERER ", StringComparison.Ordinal)
            || line.StartsWith("PARSER ", StringComparison.Ordinal)
            || (line.StartsWith("PARAMETER ", StringComparison.Ordinal) && !line.Contains("draft", StringComparison.Ordinal))));

        var file = Path.Combine(paths.Temp, $"{textModel}.Modelfile");
        await File.WriteAllLinesAsync(file, lines, cancellationToken).ConfigureAwait(false);
        try
        {
            await RunCliAsync($"create {textModel} -f \"{file}\"", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private async Task<bool> IsRespondingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            using var response = await http.GetAsync(new Uri(BaseAddress, "api/version"), timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<string> RunCliAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = Process.Start(StartInfo(arguments)) ?? throw new InvalidOperationException("Ollama çalıştırılamadı.");
        ChildProcessJob.Shared.TryAdd(process);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ollama {arguments.Split(' ')[0]} başarısız: {await error.ConfigureAwait(false)}");
        }

        return output;
    }

    /// <summary>Ollama alt süreçleri için ortam: modeller ve Ollama'nın kendi dosyaları D: üzerinde kalır.</summary>
    private ProcessStartInfo StartInfo(string arguments)
    {
        Directory.CreateDirectory(paths.OllamaHome);
        Directory.CreateDirectory(paths.Temp);
        var start = new ProcessStartInfo(paths.OllamaExe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(paths.OllamaExe)!
        };
        start.Environment["OLLAMA_HOST"] = $"127.0.0.1:{_port}";
        start.Environment["OLLAMA_MODELS"] = paths.OllamaModels;
        start.Environment["USERPROFILE"] = paths.OllamaHome;
        start.Environment["HOME"] = paths.OllamaHome;
        start.Environment["TMP"] = paths.Temp;
        start.Environment["TEMP"] = paths.Temp;
        return start;
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            // Friday kapanmadan önce sunucu ve llama-server gerçekten bitmiş olsun.
            _process.WaitForExit(TimeSpan.FromSeconds(5));
        }

        _process?.Dispose();
    }
}
