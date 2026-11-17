namespace Friday.Core;

/// <summary>
/// Friday'in araç, model ve veri klasörleri. Varsayılan kök D:\Friday'dir; FRIDAY_HOME ile değiştirilebilir
/// ama C: sürücüsü kabul edilmez.
/// </summary>
public sealed class FridayPaths
{
    public const string DefaultHome = @"D:\Friday";
    public const string LinguaFlowModels = @"D:\LinguaFlowModels";

    public FridayPaths(string home)
    {
        var full = Path.GetFullPath(home);
        if (string.Equals(Path.GetPathRoot(full), @"C:\", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Friday C: sürücüsünde çalışmaz. FRIDAY_HOME için başka bir sürücü seçin.");
        }

        Home = full;
    }

    public string Home { get; }

    public string Tools => Path.Combine(Home, "tools");

    public string Models => Path.Combine(Home, "models");

    public string Data => Path.Combine(Home, "data");

    public string SettingsFile => Path.Combine(Data, "settings.json");

    public string OllamaExe => Path.Combine(Tools, "ollama", "ollama.exe");

    public string OllamaModels => Path.Combine(Models, "ollama");

    /// <summary>Ollama'nın anahtar ve geçici dosyaları; C:\Users\...\.ollama yerine buraya yazılır.</summary>
    public string OllamaHome => Path.Combine(Data, "ollama-home");

    public string Temp => Path.Combine(Data, "tmp");

    /// <summary>Uyandırma kelimesi modelleri ("Hey Friday") ve openWakeWord özellik çıkarıcıları.</summary>
    public string WakeWordModels => Path.Combine(Models, "wakeword");

    public string Logs => Path.Combine(Data, "logs");

    public string PiperExe => Path.Combine(Tools, "piper", "piper.exe");

    /// <summary>Topluluğun eğittiği daha doğal Türkçe Piper sesi; yoksa eski dfki sesi.</summary>
    public string DefaultVoice
    {
        get
        {
            var natural = Path.Combine(Models, "piper", "tr_TR-99eren99.onnx");
            return File.Exists(natural) ? natural : Path.Combine(Models, "piper", "tr_TR-dfki-medium.onnx");
        }
    }

    public static FridayPaths Resolve()
    {
        var home = Environment.GetEnvironmentVariable("FRIDAY_HOME");
        return new FridayPaths(string.IsNullOrWhiteSpace(home) ? DefaultHome : home);
    }

    /// <summary>Ayarda yol yoksa önce LinguaFlow'un modelini, sonra Friday'in kendi kopyasını kullanır.</summary>
    public string ResolveWhisperModel(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        const string file = "ggml-large-v3-turbo-q5_0.bin";
        var shared = Path.Combine(LinguaFlowModels, file);
        return File.Exists(shared) ? shared : Path.Combine(Models, "whisper", file);
    }
}
