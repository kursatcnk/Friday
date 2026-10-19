using System.Text;
using Friday.Core;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Friday.Infrastructure;

/// <summary>
/// Sesi bilgisayarda, GPU üzerinde (Vulkan) yazıya döker. Ses hiçbir yere gönderilmez. Uzun süre kullanılmazsa
/// GPU'dan boşaltılır; kısayola basılınca kullanıcı konuşurken yeniden yüklenir.
/// </summary>
public sealed class WhisperTranscriber : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string _modelPath = "";
    private FridaySettings _settings = new();

    public string RuntimeLabel { get; private set; } = "";

    public bool IsLoaded => _processor is not null;

    public void Configure(string modelPath, FridaySettings settings)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Whisper modeli bulunamadı: {modelPath}. setup.ps1'i çalıştırın.");
        }

        _modelPath = modelPath;
        _settings = settings;
    }

    /// <summary>Yüklü değilse modeli GPU'ya yükler ve ısındırır; yüklüyse hemen döner.</summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_processor is not null)
            {
                return;
            }

            await Task.Run(Load, cancellationToken).ConfigureAwait(false);

            // İlk çözümleme GPU çekirdeklerini derler; ilk soruda beklememek için sahte sesle ısındırılır.
            var warmup = new float[MicrophoneRecorder.SampleRate];
            await foreach (var _ in _processor!.ProcessAsync(warmup, cancellationToken).ConfigureAwait(false))
            {
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Modeli GPU'dan boşaltır; bir sonraki kullanımda yeniden yüklenir.</summary>
    public async Task UnloadAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _processor?.Dispose();
            _factory?.Dispose();
            _processor = null;
            _factory = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var text = new StringBuilder();
            await foreach (var segment in _processor!.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
            {
                text.Append(segment.Text);
            }

            return TranscriptFilter.Normalize(text.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    private void Load()
    {
        _factory = WhisperFactory.FromPath(_modelPath, new WhisperFactoryOptions
        {
            UseGpu = true,
            UseFlashAttention = true
        });
        RuntimeLabel = RuntimeOptions.LoadedLibrary?.ToString() switch
        {
            "Cuda" => "GPU (CUDA)",
            "Vulkan" => "GPU (Vulkan)",
            _ => "CPU"
        };

        _processor = _factory.CreateBuilder()
            .WithLanguage(string.IsNullOrWhiteSpace(_settings.Language) ? "auto" : _settings.Language)
            .WithNoContext()
            .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 8))
            .WithBeamSearchSamplingStrategy(strategy => strategy.WithBeamSize(3))
            // Asistanın adını önceden duyurmak, adın doğru yazılmasını sağlar.
            .WithPrompt($"Merhaba {_settings.AssistantName}.")
            .Build();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
        _lock.Dispose();
    }
}
