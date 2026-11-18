namespace Friday.Core;

/// <summary>data\settings.json içeriği. Dosya yoksa varsayılanlarla oluşturulur.</summary>
public sealed class FridaySettings
{
    /// <summary>En güncel ayar sürümü. Eski dosyalar açılışta bu sürüme taşınır (yeni rutinler, yeni varsayılanlar).</summary>
    public const int CurrentVersion = 3;

    /// <summary>Dosyanın ayar sürümü; sürümsüz eski dosyalar 0 sayılır.</summary>
    public int SettingsVersion { get; set; }

    public string AssistantName { get; set; } = "Friday";

    /// <summary>Asistanın sana nasıl hitap edeceği; boşsa isimsiz konuşur.</summary>
    public string UserName { get; set; } = "";

    /// <summary>Pano geçmişi: kopyalanan son 20 metin yalnızca bellekte tutulur ("2. kopyaladığımı yapıştır"). Varsayılan kapalı.</summary>
    public bool ClipboardHistory { get; set; }

    /// <summary>Alışkanlık önerileri: hep aynı saatlerde açtığın rutini ya da uygulamayı o saat gelince önerir.</summary>
    public bool HabitSuggestions { get; set; } = true;

    /// <summary>Ortam seslerinin (yağmur, beyaz gürültü…) ses düzeyi, 0–1.</summary>
    public double AmbientVolume { get; set; } = 0.25;

    /// <summary>Hitap sözü ("patron", "Kürşat Bey"); boşsa adınla ya da hitapsız konuşur. Her cümlede değil, ara sıra.</summary>
    public string AddressAs { get; set; } = "";

    public string Hotkey { get; set; } = "Ctrl+Alt+F";

    /// <summary>Hızlı komut kutusu: her yerden açılan küçük bir yazı kutusu (Raycast/Spotlight gibi). Boşsa kapalıdır.</summary>
    public string QuickAskHotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>Windows açılınca Friday tepside sessizce başlar (yalnızca kullanıcının kayıt defterindeki Run girdisi).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Uyandırma kelimesi: "hey_friday" ("Hey Friday") deyince kısayola basılmış gibi dinlemeye başlar. Boş bırakılırsa kapalıdır ve
    /// mikrofon yalnızca kısayolla açılır. Açıkken mikrofon sürekli açıktır ama ses 80 ms'lik parçalar halinde yalnızca
    /// bellekte işlenip atılır; kaydedilmez, hiçbir yere gönderilmez.
    /// </summary>
    public string WakeWord { get; set; } = "hey_friday";

    /// <summary>Uyandırma kelimesinin eşik puanı (0–1). Yanlışlıkla uyanıyorsa artır, duymuyorsa azalt.</summary>
    public double WakeWordThreshold { get; set; } = 0.5;

    /// <summary>
    /// Ritim koçu: aralıksız çalışmayı ölçer, doğal bir duraksamada mola önerir, moladan dönünce kaldığın yeri söyler.
    /// Yalnızca son klavye/fare kullanım zamanı okunur.
    /// </summary>
    public bool Rhythm { get; set; } = true;

    /// <summary>Bu kadar dakika aralıksız çalışınca mola önerilir.</summary>
    public int BreakEveryMinutes { get; set; } = 50;

    /// <summary>"Mola ver" denince mola kaç dakika sürer.</summary>
    public int BreakMinutes { get; set; } = 5;

    /// <summary>Günün ilk oturuşunda (sabah) hava ve yaklaşan tarihlerle kısa bir karşılama.</summary>
    public bool MorningGreeting { get; set; } = true;

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

    /// <summary>
    /// "Akıllı" mod için büyük model (Gemma 4 12B, 7,5 GB). Ayarlarda "Akıl: Akıllı" seçilince sohbet modeli bundan
    /// üretilen görmesiz "friday-smart" olur; "Hızlı" seçilince küçük "friday-text"e dönülür.
    /// </summary>
    public string SmartModel { get; set; } = "gemma4:12b";

    /// <summary>Akıllı modun sohbet modeli adı.</summary>
    public const string SmartTextModel = "friday-smart";

    /// <summary>Hızlı modun sohbet modeli adı.</summary>
    public const string FastTextModel = "friday-text";

    /// <summary>Sohbet modelinin üretildiği asıl model: akıllı modda büyük model, hızlıda görme modeli.</summary>
    public string TextModelSource() => OllamaModel == SmartTextModel ? SmartModel : VisionModel;

    /// <summary>
    /// Bu kadar dakika kullanılmazsa dil modeli, Whisper ve Piper kapanır: VRAM 0'a iner, dil modelinin ~3 GB'lık RAM
    /// payı da boşalır. Yeniden yükleme ~3 sn sürer ve kullanıcı konuşurken biter.
    /// </summary>
    public int GpuIdleMinutes { get; set; } = 2;

    /// <summary>
    /// Oyun, tam ekran video ya da sunum açıkken modeller cevaptan ~20 sn sonra ekran kartından çıkar: Friday yine
    /// tam güçle cevap verir ama oyunun belleğini ve işlemcisini tutmaz.
    /// </summary>
    public bool YieldToFullScreen { get; set; } = true;

    /// <summary>
    /// Arayüzü ekran kartıyla çiz. Kapalıyken (varsayılan) pencereler işlemciyle çizilir: Friday'in arayüzü ekran
    /// kartında hiç bellek tutmaz, oyunla ve dil modeliyle yarışmaz, sürücü takıldığında beyaz pencere sorunu yaşanmaz.
    /// Yeniden başlatınca geçerli olur.
    /// </summary>
    public bool GpuRendering { get; set; }

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
    /// Takvimin gizli iCal adresi (Google Takvim › Ayarlar › Takvimi entegre et › "iCal biçiminde gizli adres"; Outlook ›
    /// Takvimi yayımla › ICS). Boşsa takvim kapalıdır. Adres yalnızca bu dosyada durur.
    /// </summary>
    public string CalendarUrl { get; set; } = "";

    /// <summary>
    /// Kullanılacak mikrofonun adında geçen sözcük (ör. "Headset"). Boşsa Windows varsayılanı denenir; ondan hiç ses
    /// gelmiyorsa (sanal cihazlar) Friday çalışan mikrofonu kendisi bulur.
    /// </summary>
    public string MicrophoneDevice { get; set; } = "";

    /// <summary>Konuşma bittikten sonra bu kadar sessizlik olursa kayıt durur.</summary>
    public int SilenceMilliseconds { get; set; } = 1200;

    public int MaxRecordSeconds { get; set; } = 20;

    /// <summary>
    /// Bu kadar dakika konuşulmazsa sohbet biter: sonraki soru yeni bir sohbet olarak başlar ve eski konuşma modele
    /// taşınmaz. Eski sohbetler panelde durur, açılıp kaldığı yerden sürdürülebilir.
    /// </summary>
    public int ConversationMemoryMinutes { get; set; } = 30;

    /// <summary>"esprili" (varsayılan), "sade" ya da "resmi".</summary>
    public string Personality { get; set; } = "esprili";

    /// <summary>Cevap bittikten sonra mikrofon bu kadar saniye açık kalır; tuşa basmadan devam sorusu sorulur. 0 kapatır.</summary>
    public int FollowUpSeconds { get; set; } = 4;

    /// <summary>Sohbetler D:\Friday\data\chats altında tutulur (sohbet listesi, arama, "Bugün ne konuştuk?").</summary>
    public bool KeepHistory { get; set; } = true;

    /// <summary>"Bu videoyu çevir" komutunun açacağı LinguaFlow.</summary>
    public string LinguaFlowPath { get; set; } = @"E:\Github\LinguaFlow\artifacts\publish\win-x64\LinguaFlow.Desktop.exe";

    /// <summary>
    /// Sesli rutinler. Her adım "tür:değer" biçimindedir: uygulama, site, ara, medya, ses, soyle, ozet, ritim, hatirlat,
    /// bekle, linguaflow. Paneldeki Rutinler sayfasından da eklenip düzenlenebilir.
    /// </summary>
    /// <summary>Rutinlerin kendiliğinden çalışma zamanı: ad → "09:00 haftaici", "18:30 hergun" ya da "acilis".</summary>
    public Dictionary<string, string> RoutineSchedules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rutinlerin genel kısayolları: ad → "Ctrl+Alt+1". Friday arkadayken de çalışır.</summary>
    public Dictionary<string, string> RoutineHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> Routines { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["çalışma modu"] = ["uygulama:Visual Studio", "site:github.com", "soyle:Çalışma modu hazır. Kolay gelsin."],
        ["günaydın"] = ["ozet"],
        ["mola"] = ["ritim:mola"],
        ["toplantı modu"] = ["ritim:odak 45", "soyle:Toplantıda başarılar."],
        ["iyi geceler"] = ["ritim:durum", "soyle:İyi geceler, yarın görüşürüz."],
    };

    /// <summary>
    /// Özel komutlar: cümlenin herhangi bir yerinde bu ifade geçince adımlar çalışır. "Baha dediğimde 'x' de" diye sesle
    /// ya da Rutinler sayfasından eklenir. Adımlar rutinlerle aynı biçimdedir.
    /// </summary>
    public Dictionary<string, List<string>> Triggers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
