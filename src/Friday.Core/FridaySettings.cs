namespace Friday.Core;

/// <summary>data\settings.json içeriği. Dosya yoksa varsayılanlarla oluşturulur.</summary>
public sealed class FridaySettings
{
    public string AssistantName { get; set; } = "Friday";

    /// <summary>Asistanın sana nasıl hitap edeceği; boşsa isimsiz konuşur.</summary>
    public string UserName { get; set; } = "";

    public string Hotkey { get; set; } = "Ctrl+Alt+F";

    /// <summary>Whisper konuşma dili ("tr", "en" ya da "auto").</summary>
    public string Language { get; set; } = "tr";

    /// <summary>Boşsa D:\LinguaFlowModels ya da D:\Friday\models\whisper içindeki Large v3 Turbo kullanılır.</summary>
    public string WhisperModelPath { get; set; } = "";

    /// <summary>
    /// Sohbet modeli: gemma4:e4b'nin görme modülü olmayan varyantı (~1,2 GB daha az VRAM). Yoksa açılışta
    /// VisionModel'den otomatik oluşturulur.
    /// </summary>
    public string OllamaModel { get; set; } = "friday-text";

    /// <summary>"Ekrana bak" için görme yetenekli model; yalnızca o an yüklenir, sonra boşaltılır.</summary>
    public string VisionModel { get; set; } = "gemma4:e4b";

    /// <summary>Bu kadar dakika kullanılmazsa dil modeli ve Whisper GPU'dan boşaltılır (VRAM 0'a iner).</summary>
    public int GpuIdleMinutes { get; set; } = 5;

    /// <summary>
    /// "auto": XTTS kuruluysa XTTS (doğal, yerel), değilse Azure anahtarı varsa Azure, o da yoksa Piper.
    /// "xtts", "azure" ya da "piper" ile sabitlenebilir. Seçilen motor hata verirse o cümle Piper ile okunur.
    /// </summary>
    public string SpeechEngine { get; set; } = "piper";

    /// <summary>Piper'ın ses tonu değişkenliği; yükseldikçe daha canlı, düştükçe daha düz.</summary>
    public double PiperNoiseScale { get; set; } = 0.667;

    /// <summary>Piper'ın hece süresi değişkenliği; yükseldikçe ritim daha insansı, düştükçe daha mekanik.</summary>
    public double PiperNoiseW { get; set; } = 0.9;

    /// <summary>Hafif oda yankısı ve sıcaklık; sentetik sesin "kuru" duyulmasını azaltır. 0 kapatır.</summary>
    public double VoiceRoom { get; set; } = 0.07;

    /// <summary>XTTS'in hazır seslerinden biri (setup sonrası /speakers listesinde).</summary>
    public string XttsSpeaker { get; set; } = "Ana Florence";

    /// <summary>
    /// İsteğe bağlı: 6–15 saniyelik temiz bir .wav kaydı. Verilirse Friday bu sesle konuşur (ses klonlama).
    /// Örnek dosya yalnızca bilgisayarda işlenir.
    /// </summary>
    public string XttsVoiceSample { get; set; } = "";

    /// <summary>Azure Speech anahtarı (ücretsiz F0 katmanı: ayda 500 bin karakter nöral ses).</summary>
    public string AzureSpeechKey { get; set; } = "";

    public string AzureSpeechRegion { get; set; } = "westeurope";

    /// <summary>tr-TR-EmelNeural (kadın) ya da tr-TR-AhmetNeural (erkek).</summary>
    public string AzureVoice { get; set; } = "tr-TR-EmelNeural";

    /// <summary>Friday kendi Ollama sunucusunu bu portta açar; sistemde başka bir Ollama varsa çakışmaz.</summary>
    public int OllamaPort { get; set; } = 11435;

    /// <summary>Boşsa D:\Friday\models\piper\tr_TR-dfki-medium.onnx.</summary>
    public string VoiceModelPath { get; set; } = "";

    /// <summary>1 normal hız; 1,2 daha hızlı, 0,9 daha yavaş.</summary>
    public double SpeechSpeed { get; set; } = 1.0;

    /// <summary>Hava durumu sorulurken şehir söylenmezse kullanılır.</summary>
    public string City { get; set; } = "İstanbul";

    /// <summary>
    /// Kullanılacak mikrofonun adında geçen sözcük (ör. "Headset"). Boşsa Windows varsayılanı denenir; ondan hiç ses
    /// gelmiyorsa (sanal cihazlar) Friday çalışan mikrofonu kendisi bulur.
    /// </summary>
    public string MicrophoneDevice { get; set; } = "";

    /// <summary>Konuşma bittikten sonra bu kadar sessizlik olursa kayıt durur.</summary>
    public int SilenceMilliseconds { get; set; } = 900;

    public int MaxRecordSeconds { get; set; } = 20;

    /// <summary>Bu süre konuşulmazsa önceki sohbet unutulur.</summary>
    public int ConversationMemoryMinutes { get; set; } = 10;

    /// <summary>"esprili" (varsayılan), "sade" ya da "resmi".</summary>
    public string Personality { get; set; } = "esprili";

    /// <summary>Cevap bittikten sonra mikrofon bu kadar saniye açık kalır; tuşa basmadan devam sorusu sorulur. 0 kapatır.</summary>
    public int FollowUpSeconds { get; set; } = 4;

    /// <summary>Sohbetler gün gün D:\Friday\data\history altında tutulur ("Bugün ne konuştuk?" için).</summary>
    public bool KeepHistory { get; set; } = true;

    /// <summary>"Bu videoyu çevir" komutunun açacağı LinguaFlow.</summary>
    public string LinguaFlowPath { get; set; } = @"E:\Github\LinguaFlow\artifacts\publish\win-x64\LinguaFlow.Desktop.exe";

    /// <summary>
    /// Sesli rutinler. Her adım "tür:değer" biçimindedir: uygulama, site, ara, medya, soyle, ozet, linguaflow.
    /// </summary>
    public Dictionary<string, List<string>> Routines { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["çalışma modu"] = ["uygulama:Visual Studio", "site:github.com", "soyle:Çalışma modu hazır. Kolay gelsin."],
        ["günaydın"] = ["ozet"],
        ["mola"] = ["medya:oynat_duraklat", "soyle:Mola zamanı. Biraz su iç, gözlerini dinlendir."],
    };

    /// <summary>
    /// Yabancı adların okunuşu (ör. "Claude": "Klod"). Yerleşik sözlüğe ek olarak kullanılır; aynı kelime
    /// yazılırsa yerleşik okunuşu ezer.
    /// </summary>
    public Dictionary<string, string> Pronunciations { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"uygulama aç" aracı için sesli ad → komut eşlemesi. Burada olmayanlar Başlat menüsünde aranır.</summary>
    public Dictionary<string, string> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["not defteri"] = "notepad.exe",
        ["hesap makinesi"] = "calc.exe",
        ["paint"] = "mspaint.exe",
        ["dosya gezgini"] = "explorer.exe",
        ["görev yöneticisi"] = "taskmgr.exe",
        ["ayarlar"] = "ms-settings:",
    };
}
