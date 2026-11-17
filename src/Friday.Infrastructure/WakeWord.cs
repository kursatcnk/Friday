using Microsoft.ML.OnnxRuntime;

namespace Friday.Infrastructure;

/// <summary>
/// openWakeWord'ün üç küçük modeli (mel spektrogram → konuşma gömmesi → uyandırma kelimesi sınıflandırıcısı). Modeller bir kez
/// yüklenir ve bütün mikrofonlar arasında paylaşılır; her mikrofonun kendi akış durumu <see cref="WakeWordTracker"/>'dedir.
/// Tek çekirdekte, tek iş parçacığıyla çalışır: 80 ms'lik her parça birkaç milisaniye sürer.
/// </summary>
public sealed class WakeWordEngine : IDisposable
{
    /// <summary>Bir adımda işlenen ses: 16 kHz'te 80 ms.</summary>
    public const int ChunkSamples = 1280;

    internal const int MelContext = 480;
    internal const int MelWindow = 76;
    internal const int MelBins = 32;
    internal const int EmbeddingSize = 96;

    private readonly InferenceSession _mel;
    private readonly InferenceSession _embedding;
    private readonly InferenceSession _wake;
    private readonly RunOptions _run = new();
    private readonly string[] _melInput;
    private readonly string[] _embeddingInput;
    private readonly string[] _wakeInput;
    private float[]? _silentMelFrame;
    private float[]? _silentEmbedding;

    public WakeWordEngine(string folder, string model)
    {
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            // Bellek havuzu küçük ve sabit boyutlu girdiler için gereksiz; kapalıyken RAM payı belirgin şekilde düşer.
            EnableCpuMemArena = false
        };
        _mel = new InferenceSession(Path.Combine(folder, "melspectrogram.onnx"), options);
        _embedding = new InferenceSession(Path.Combine(folder, "embedding_model.onnx"), options);
        _wake = new InferenceSession(ModelFile(folder, model), options);
        _melInput = [_mel.InputMetadata.Keys.First()];
        _embeddingInput = [_embedding.InputMetadata.Keys.First()];
        _wakeInput = [_wake.InputMetadata.Keys.First()];
        // Sınıflandırıcı son 16 gömmeye (~1,3 sn) bakar.
        FeatureFrames = Math.Max(1, _wake.InputMetadata[_wakeInput[0]].Dimensions[1]);
    }

    public int FeatureFrames { get; }

    public static bool IsInstalled(string folder, string model) =>
        File.Exists(Path.Combine(folder, "melspectrogram.onnx"))
        && File.Exists(Path.Combine(folder, "embedding_model.onnx"))
        && File.Exists(ModelFile(folder, model));

    public WakeWordTracker CreateStream() => new(this);

    /// <summary>Sürüm eki olan dosyayı da bulur: "hey_friday" → hey_friday_v0.1.onnx.</summary>
    private static string ModelFile(string folder, string model)
    {
        var exact = Path.Combine(folder, model + ".onnx");
        if (File.Exists(exact) || !Directory.Exists(folder))
        {
            return exact;
        }

        return Directory.EnumerateFiles(folder, model + "_v*.onnx").OrderDescending(StringComparer.Ordinal).FirstOrDefault() ?? exact;
    }

    /// <param name="audio">MelContext + ChunkSamples örnek, 16 bit ölçeğinde (−32768…32767).</param>
    /// <returns>Mel kareleri, satır satır (kare × 32).</returns>
    internal float[] Mel(float[] audio)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(audio, [1, audio.Length]);
        using var outputs = _mel.Run(_run, _melInput, [input], _mel.OutputNames);
        var values = outputs[0].GetTensorDataAsSpan<float>();
        var frames = new float[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            // openWakeWord'ün eğitimde kullandığı ölçek.
            frames[index] = (values[index] / 10f) + 2f;
        }

        return frames;
    }

    /// <param name="window">Son 76 mel karesi (76 × 32).</param>
    internal void Embed(float[] window, Span<float> destination)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(window, [1, MelWindow, MelBins, 1]);
        using var outputs = _embedding.Run(_run, _embeddingInput, [input], _embedding.OutputNames);
        outputs[0].GetTensorDataAsSpan<float>()[..EmbeddingSize].CopyTo(destination);
    }

    /// <param name="features">Son <see cref="FeatureFrames"/> gömme (FeatureFrames × 96).</param>
    internal float Score(float[] features)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(features, [1, FeatureFrames, EmbeddingSize]);
        using var outputs = _wake.Run(_run, _wakeInput, [input], _wake.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>()[0];
    }

    /// <summary>Tam sessizliğin (sıfır örnekler) mel karesi; sessiz mikrofonlarda modeli hiç çalıştırmamak için.</summary>
    internal float[] SilentMelFrame()
    {
        if (_silentMelFrame is null)
        {
            var frames = Mel(new float[MelContext + ChunkSamples]);
            _silentMelFrame = frames[..MelBins];
        }

        return _silentMelFrame;
    }

    /// <summary>76 karesi de sessiz bir pencerenin gömmesi.</summary>
    internal float[] SilentEmbedding()
    {
        if (_silentEmbedding is null)
        {
            var frame = SilentMelFrame();
            var window = new float[MelWindow * MelBins];
            for (var row = 0; row < MelWindow; row++)
            {
                frame.CopyTo(window, row * MelBins);
            }

            var embedding = new float[EmbeddingSize];
            Embed(window, embedding);
            _silentEmbedding = embedding;
        }

        return _silentEmbedding;
    }

    public void Dispose()
    {
        _mel.Dispose();
        _embedding.Dispose();
        _wake.Dispose();
        _run.Dispose();
    }
}

/// <summary>
/// Tek bir mikrofonun akış durumu: son 30 ms ses, son 76 mel karesi ve son ~1,3 sn'lik gömmeler. Arabellekler bir kez
/// ayrılır ve kaydırılarak kullanılır; 80 ms'de bir yapılan işte bellek ayırma neredeyse yoktur.
/// </summary>
public sealed class WakeWordTracker
{
    /// <summary>Sıfırlamadan sonra ilk tahminler eksik bağlamla yapılır ve yok sayılır (openWakeWord'deki gibi).</summary>
    private const int WarmupPredictions = 5;

    /// <summary>Bu tepe değerin (~−44 dBFS) altındaki parça "sessiz" sayılır; konuşma bunun çok üstündedir.</summary>
    private const int QuietPeak = 200;

    /// <summary>
    /// Sessizlik başladıktan sonra modeller ~1,3 sn daha çalışır: kelimenin son hecesi ve hemen ardından gelen
    /// yüksek puanlar (iki parça şartı için gerekli) kesilmez.
    /// </summary>
    private const int QuietHangover = 16;

    private readonly WakeWordEngine _engine;
    private readonly float[] _audio = new float[WakeWordEngine.MelContext + WakeWordEngine.ChunkSamples];
    private readonly float[] _mel = new float[WakeWordEngine.MelWindow * WakeWordEngine.MelBins];
    private readonly float[] _features;
    private int _predictions;
    private int _quietChunks;

    internal WakeWordTracker(WakeWordEngine engine)
    {
        _engine = engine;
        _features = new float[engine.FeatureFrames * WakeWordEngine.EmbeddingSize];
        // openWakeWord'deki başlangıç durumu: mel arabelleği 1'lerle, gömmeler hafif gürültüyle doldurulur.
        Array.Fill(_mel, 1f);
        var noise = new Random(7);
        var chunk = new short[WakeWordEngine.ChunkSamples];
        for (var step = 0; step < engine.FeatureFrames + 10; step++)
        {
            for (var index = 0; index < chunk.Length; index++)
            {
                chunk[index] = (short)noise.Next(-1000, 1000);
            }

            Process(chunk);
        }

        Reset();
    }

    /// <summary>80 ms'lik bir parçayı işler ve uyandırma kelimesinin puanını (0–1) döndürür.</summary>
    public float Process(ReadOnlySpan<short> chunk)
    {
        if (chunk.Length != WakeWordEngine.ChunkSamples)
        {
            throw new ArgumentException($"Parça {WakeWordEngine.ChunkSamples} örnek olmalı.", nameof(chunk));
        }

        Array.Copy(_audio, WakeWordEngine.ChunkSamples, _audio, 0, WakeWordEngine.MelContext);
        var peak = 0;
        for (var index = 0; index < chunk.Length; index++)
        {
            _audio[WakeWordEngine.MelContext + index] = chunk[index];
            peak = Math.Max(peak, Math.Abs((int)chunk[index]));
        }

        _quietChunks = peak < QuietPeak ? _quietChunks + 1 : 0;
        if (_quietChunks > QuietHangover)
        {
            // Oda bir süredir sessiz: modeller çalıştırılmaz, arabellekler sessizliğin önceden hesaplanmış karşılığıyla
            // kaydırılır. Ses gelince ilk parçada tam işlemeye dönülür ve bağlam, sessizlikten sonra konuşma başlamış
            // gibi doğru kalır. Friday günün çoğunu böyle geçirdiği için işlemci neredeyse boş kalır.
            var frame = _engine.SilentMelFrame();
            Push(_mel, frame, 8, WakeWordEngine.MelBins);
            Push(_features, _engine.SilentEmbedding(), 1, WakeWordEngine.EmbeddingSize);
            _predictions++;
            return 0;
        }

        var frames = _engine.Mel(_audio);
        Shift(_mel, frames);
        Array.Copy(_features, WakeWordEngine.EmbeddingSize, _features, 0, _features.Length - WakeWordEngine.EmbeddingSize);
        _engine.Embed(_mel, _features.AsSpan(_features.Length - WakeWordEngine.EmbeddingSize));
        var score = _engine.Score(_features);
        return ++_predictions <= WarmupPredictions ? 0 : score;
    }

    /// <summary>Algılamadan ya da duraklamadan sonra: ilk birkaç tahmin yok sayılır, aynı kelime iki kez tetiklemez.</summary>
    public void Reset() => _predictions = 0;

    /// <summary>Yeni mel karelerini sona ekler, en eskileri atar.</summary>
    private static void Shift(float[] buffer, float[] frames)
    {
        if (frames.Length >= buffer.Length)
        {
            Array.Copy(frames, frames.Length - buffer.Length, buffer, 0, buffer.Length);
            return;
        }

        Array.Copy(buffer, frames.Length, buffer, 0, buffer.Length - frames.Length);
        Array.Copy(frames, 0, buffer, buffer.Length - frames.Length, frames.Length);
    }

    /// <summary>Aynı satırı <paramref name="count"/> kez sona ekler.</summary>
    private static void Push(float[] buffer, float[] row, int count, int width)
    {
        var length = Math.Min(count * width, buffer.Length);
        Array.Copy(buffer, length, buffer, 0, buffer.Length - length);
        for (var offset = buffer.Length - length; offset < buffer.Length; offset += width)
        {
            row.AsSpan(0, width).CopyTo(buffer.AsSpan(offset));
        }
    }
}
