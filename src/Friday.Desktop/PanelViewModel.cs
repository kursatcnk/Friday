using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;
using Friday.Infrastructure;

namespace Friday.Desktop;

public sealed partial class ChatLine : ObservableObject
{
    public ChatLine(bool isUser, string text, DateTime time)
    {
        IsUser = isUser;
        _text = text;
        Time = time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public bool IsUser { get; }

    public string Time { get; }

    [ObservableProperty]
    private string _text;
}

public sealed record MemoryView(string Id, string Text, string Date, string? Source, string? Countdown)
{
    public bool HasCountdown => Countdown is not null;

    public string Meta => Source is null ? Date : $"{Date} · {Source}'den";
}

/// <summary>İçe aktarma önizlemesindeki bir bilgi; Friday'in zaten bildikleri baştan seçili değildir.</summary>
public sealed partial class ImportCandidateView(string text, bool alreadyKnown) : ObservableObject
{
    public string Text { get; } = text;

    public bool AlreadyKnown { get; } = alreadyKnown;

    [ObservableProperty]
    private bool _isSelected = !alreadyKnown;
}

public sealed record LedgerView(string Time, string Category, string Detail, bool IsOutbound)
{
    public string Glyph => IsOutbound ? "" : "";

    public string FlowLabel => IsOutbound ? "Dışarı" : "Yerel";
}

/// <summary>"Bugün" ekranında yaklaşan bir şey: kurulu hatırlatma (silinebilir) ya da hafızadaki tarihli bilgi.</summary>
public sealed record UpcomingView(string? ReminderId, string When, string Text)
{
    public bool IsReminder => ReminderId is not null;
}

public sealed record NoteView(string Time, string Text);

/// <summary>Panelin kullanıcı eylemleri; App bunları asistana bağlar.</summary>
public sealed class PanelActions
{
    public Action<string> Send { get; init; } = _ => { };

    public Action<string> Remember { get; init; } = _ => { };

    public Action<string> Forget { get; init; } = _ => { };

    public Action OpenSettingsFile { get; init; } = () => { };

    public Action OpenDataFolder { get; init; } = () => { };

    public Action<FridaySettings> Save { get; init; } = _ => { };

    public Action<string> PreviewVoice { get; init; } = _ => { };

    /// <summary>İçe aktarılacak .txt/.md/.json dosyasını seçtirir; vazgeçilirse null.</summary>
    public Func<string?> PickImportFile { get; init; } = () => null;

    public Func<IReadOnlyList<MemoryItem>> KnownMemories { get; init; } = () => [];

    /// <summary>Seçilen bilgileri kaynağıyla hafızaya ekler; eklenen sayıyı döndürür.</summary>
    public Func<IReadOnlyList<string>, string, int> ImportMemories { get; init; } = (_, _) => 0;

    /// <summary>Hafızayı Markdown dosyasına yazar ve klasörünü gösterir; dosya yolunu döndürür.</summary>
    public Func<string?> ExportMemories { get; init; } = () => null;

    /// <summary>Başka bir yapay zekâya yapıştırılacak mesajı panoya koyar.</summary>
    public Action CopyMemoriesForAi { get; init; } = () => { };

    public Action<string> DeleteReminder { get; init; } = _ => { };

    public Action OpenNotes { get; init; } = () => { };

    /// <summary>Sohbetler; anlık görüntüde null.</summary>
    public ChatStore? Chats { get; init; }

    /// <summary>Bir sohbeti açar (null: yeni sohbet); asistan konuşuyorsa susar.</summary>
    public Action<string?> OpenChat { get; init; } = _ => { };

    public Action<string> DeleteChat { get; init; } = _ => { };

    /// <summary>Sohbeti Markdown olarak kaydeder; dosya yolunu döndürür.</summary>
    public Func<string, string?> ExportChat { get; init; } = _ => null;

    /// <summary>Konuşma kısayolunu canlı değiştirir; sorun yoksa null, varsa açıklaması.</summary>
    public Func<string, string?> ApplyHotkey { get; init; } = _ => null;

    /// <summary>Hızlı komut kutusunun kısayolunu değiştirir ("" kapatır); sorun yoksa null.</summary>
    public Func<string, string?> ApplyQuickAskHotkey { get; init; } = _ => null;

    public Func<bool, bool> SetStartWithWindows { get; init; } = _ => true;

    /// <summary>"chats", "memory" ya da "reminders" verisini siler; silinen sayıyı döndürür.</summary>
    public Func<string, int> ClearData { get; init; } = _ => 0;

    public Action OpenLogs { get; init; } = () => { };

    public Func<Task<UpdateInfo>> CheckForUpdate { get; init; } = () => Task.FromResult(new UpdateInfo(false, "", null));

    public IReadOnlyList<VoiceOption> PiperVoices { get; init; } = [];

    /// <summary>Kişisel verileri zip olarak yedekler (dosya seçtirir); sonucu anlatan cümle, vazgeçilirse null.</summary>
    public Func<string?> BackupData { get; init; } = () => null;

    /// <summary>Bir yedeği seçtirir ve sonraki açılışa bekletir; sonucu anlatan cümle, vazgeçilirse null.</summary>
    public Func<string?> RestoreData { get; init; } = () => null;

    /// <summary>Friday'i kapatıp yeniden açar (geri yükleme için).</summary>
    public Action Restart { get; init; } = () => { };

    public Func<Task<IReadOnlyList<ReclaimItem>>> ScanReclaimable { get; init; } = () => Task.FromResult<IReadOnlyList<ReclaimItem>>([]);

    public Func<IReadOnlyList<ReclaimItem>, Task<long>> Reclaim { get; init; } = _ => Task.FromResult(0L);
}

public sealed record VoiceOption(string Name, string Label);

public sealed partial class PanelViewModel : ObservableObject
{
    private const int MaxLines = 240;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly PanelActions _actions;
    private readonly FridaySettings _settings;
    private ChatLine? _currentReply;

    public PanelViewModel(FridaySettings settings, PanelActions actions)
    {
        _settings = settings;
        _actions = actions;
        AssistantName = settings.AssistantName;
        LoadSettingsFields();
        LoadRoutines();
    }

    public string AssistantName { get; }

    public ObservableCollection<ChatLine> Lines { get; } = [];

    public ObservableCollection<MemoryView> Memories { get; } = [];

    public ObservableCollection<LedgerView> Ledger { get; } = [];

    public IReadOnlyList<string> Suggestions { get; } =
    [
        "Durum raporu",
        "Ekranımda ne var?",
        "Panodaki metni özetle",
        "Yarın hava nasıl?",
        "İndirme bitince haber ver",
        "25 dakika odaklanacağım",
        "Şu an ne çalıyor?",
        "Bugün ne konuştuk?",
    ];

    /// <summary>"Bugün" ekranındaki "Dene" kartı: Friday'in dil modelsiz, anında yaptığı işler.</summary>
    public IReadOnlyList<string> TodayIdeas { get; } =
    [
        "Durum raporu",
        "Yarın hava nasıl?",
        "Dolar kaç lira?",
        "İndirme bitince haber ver",
        "Yılbaşına kaç gün kaldı?",
        "Ekran görüntüsü al",
        "Yazı tura at",
        "Kronometreyi başlat",
        "Hatırlatmalarım neler?",
        "Bu hafta ne kadar çalıştım?",
    ];

    // ---------- Gezinme ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(PageSubtitle))]
    private bool _isToday = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(PageSubtitle))]
    private bool _isChat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(PageSubtitle))]
    private bool _isMemory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(PageSubtitle))]
    private bool _isRhythm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(PageSubtitle))]
    private bool _isPrivacy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(PageSubtitle))]
    private bool _isRoutines;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(PageSubtitle))]
    private bool _isSettings;

    public string PageTitle => IsToday ? "Bugün" : IsMemory ? "Hafıza" : IsRhythm ? "Ritim" : IsPrivacy ? "Gizlilik" : IsRoutines ? "Rutinler" : IsSettings ? "Ayarlar" : AssistantName;

    public string PageSubtitle =>
        IsToday ? $"{DateTime.Now.ToString("d MMMM dddd", Turkish)} · {StatusText}"
        : IsMemory ? "Senin hakkında bildikleri; yalnızca bu bilgisayarda durur."
        : IsRhythm ? "Çalışma ritmin, molaların ve odak oturumların."
        : IsPrivacy ? "Bugün neyin bilgisayarda kaldığı, neyin dışarı çıktığı."
        : IsRoutines ? "Tek cümleyle başlayan alışkanlıkların; ekle, düzenle, sil."
        : IsSettings ? "Kısayollar, ses, uyandırma, ritim, sohbet ve verilerin."
        : StatusText;

    public string Greeting => string.IsNullOrWhiteSpace(_settings.UserName) ? "Merhaba." : $"Merhaba {_settings.UserName.Trim()}.";

    /// <summary>Uyandırma kelimesinin okunuşu: "hey_friday" → "Hey Friday".</summary>
    public string WakeLabel => string.Join(' ', _settings.WakeWord.Split('_', StringSplitOptions.RemoveEmptyEntries)
        .Select(word => char.ToUpper(word[0], Turkish) + word[1..]));

    /// <summary>Boş sohbet ekranındaki ipucu: uyandırma kelimesi açıksa onu da söyler.</summary>
    public string WakeHint => _settings.WakeWord.Length > 0
        ? $"\"{WakeLabel}\" de, {Hotkey.Replace("+", " ", StringComparison.Ordinal)} tuşlarına bas ya da aşağıya yaz."
        : $"{Hotkey.Replace("+", " ", StringComparison.Ordinal)} tuşlarına bas ve konuş ya da aşağıya yaz.";

    /// <summary>Günün saatine göre selam: "Günaydın Kürşat", "İyi akşamlar".</summary>
    public string TimeGreeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            var hello = hour switch { >= 5 and < 11 => "Günaydın", >= 11 and < 17 => "İyi günler", >= 17 and < 22 => "İyi akşamlar", _ => "İyi geceler" };
            return string.IsNullOrWhiteSpace(_settings.UserName) ? $"{hello}." : $"{hello} {_settings.UserName.Trim()}.";
        }
    }

    /// <summary>Adı girilmemişse "Bugün" ekranında kısa bir tanışma kartı görünür.</summary>
    public bool NeedsIntro => string.IsNullOrWhiteSpace(_settings.UserName);

    // ---------- Bugün ----------

    public ObservableCollection<UpcomingView> Upcoming { get; } = [];

    public ObservableCollection<NoteView> RecentNotes { get; } = [];

    [ObservableProperty]
    private bool _hasUpcoming;

    [ObservableProperty]
    private bool _hasNotes;

    [ObservableProperty]
    private bool _hasWeather;

    [ObservableProperty]
    private string _weatherTemperature = "";

    [ObservableProperty]
    private string _weatherSummary = "";

    [ObservableProperty]
    private string _weatherDetail = "";

    [ObservableProperty]
    private string _introName = "";

    [ObservableProperty]
    private string _introCity = "";

    // ---------- Durum ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateBrush))]
    private AssistantState _state = AssistantState.Starting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageSubtitle))]
    private string _statusText = "Başlatılıyor…";

    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyKeys), nameof(WakeHint))]
    private string _hotkey = "Ctrl+Alt+F";

    [ObservableProperty]
    private string _hotkeyNote = "";

    public IReadOnlyList<string> HotkeyKeys => Hotkey.Split('+');

    public Brush StateBrush
    {
        get
        {
            var brush = new SolidColorBrush(AudioRing.Palette(State));
            brush.Freeze();
            return brush;
        }
    }

    // ---------- Sohbet ----------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _draft = "";

    [ObservableProperty]
    private bool _hasLines;

    // ---------- Hafıza ----------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemoryCommand))]
    private string _newMemory = "";

    [ObservableProperty]
    private bool _hasMemories;

    [ObservableProperty]
    private string _memorySummary = "";

    public ObservableCollection<ImportCandidateView> ImportCandidates { get; } = [];

    [ObservableProperty]
    private bool _hasImport;

    [ObservableProperty]
    private string _importSource = "";

    [ObservableProperty]
    private string _importSummary = "";

    [ObservableProperty]
    private bool _isPasteOpen;

    [ObservableProperty]
    private string _pasteText = "";

    [ObservableProperty]
    private string _memoryMessage = "";

    // ---------- Gizlilik ----------

    [ObservableProperty]
    private int _localCount;

    [ObservableProperty]
    private int _outboundCount;

    [ObservableProperty]
    private string _microphoneSummary = "Bugün açılmadı";

    // ---------- Ayarlar ----------

    public IReadOnlyList<VoiceOption> Voices { get; } =
        [.. XttsSynthesizer.RecommendedVoices.Select(voice => new VoiceOption(voice.Name, voice.Label))];

    [ObservableProperty]
    private VoiceOption? _selectedVoice;

    [ObservableProperty]
    private bool _engineXtts = true;

    [ObservableProperty]
    private bool _engineAzure;

    [ObservableProperty]
    private bool _enginePiper;

    [ObservableProperty]
    private string _azureKey = "";

    [ObservableProperty]
    private string _azureRegion = "";

    [ObservableProperty]
    private bool _voiceEmel = true;

    [ObservableProperty]
    private bool _voiceAhmet;

    [ObservableProperty]
    private bool _personalityWitty = true;

    [ObservableProperty]
    private bool _personalityPlain;

    [ObservableProperty]
    private bool _personalityFormal;

    [ObservableProperty]
    private bool _followUpOff;

    [ObservableProperty]
    private bool _followUpShort = true;

    [ObservableProperty]
    private bool _followUpLong;

    /// <summary>Sohbet modu: cevaptan sonra 15 sn dinler; "teşekkürler" ya da "bu kadar" deyince biter.</summary>
    [ObservableProperty]
    private bool _followUpChat;

    [ObservableProperty]
    private string _city = "";

    /// <summary>"Otomatik" ve bilgisayardaki kayıt cihazları.</summary>
    public IReadOnlyList<string> Microphones { get; } = ["Otomatik", .. MicrophoneRecorder.DeviceNames()];

    [ObservableProperty]
    private string _selectedMicrophone = "Otomatik";

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _engineLabel = "";

    [ObservableProperty]
    private string _savedMessage = "";

    [ObservableProperty]
    private bool _wakeEnabled = true;

    [ObservableProperty]
    private bool _wakeSensitive;

    [ObservableProperty]
    private bool _wakeNormal = true;

    [ObservableProperty]
    private bool _wakeStrict;

    [ObservableProperty]
    private bool _rhythmEnabled = true;

    [ObservableProperty]
    private bool _breakEvery30;

    [ObservableProperty]
    private bool _breakEvery50 = true;

    [ObservableProperty]
    private bool _breakEvery90;

    [ObservableProperty]
    private bool _morningEnabled = true;

    public string ModelSummary =>
        $"Sohbet: {_settings.OllamaModel} · Görme: {_settings.VisionModel} (yalnızca ekrana bakarken). " +
        $"{_settings.GpuIdleMinutes} dk boşta kalınca dil modeli, Whisper ve Piper kapanır; GPU ve RAM boşalır, Friday boşta ~60 MB kullanır. " +
        "Kalıbı belli komutlar (saat, hava, döviz, müzik, zamanlayıcı, uygulama) dil modeline gitmeden yarım saniyede cevaplanır.";

    // ---------- Asistandan gelenler ----------

    public void OnStatus(AssistantStatus status)
    {
        State = status.State;
        StatusText = status.Message;
        if (status.State is AssistantState.Listening)
        {
            _currentReply = null;
        }
    }

    public void OnUserSaid(string text)
    {
        // "Bugün" ekranındayken konuşma başlarsa sohbet görünür.
        if (IsToday)
        {
            IsToday = false;
            IsChat = true;
        }

        _currentReply = null;
        AddLine(new ChatLine(true, text, DateTime.Now));
    }

    public void OnAssistantSaid(string text)
    {
        if (_currentReply is null)
        {
            _currentReply = new ChatLine(false, text, DateTime.Now);
            AddLine(_currentReply);
        }
        else
        {
            _currentReply.Text = text;
        }
    }

    public void ShowMemories(IEnumerable<MemoryItem> items)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        Memories.Clear();
        // Yaklaşan tarihli bilgiler en üstte, gerisi en yeniden eskiye.
        foreach (var entry in items.Reverse()
                     .Select(item => (Item: item, Next: MemoryDates.NextOccurrence(item.Text, today), Left: MemoryDates.Countdown(item.Text, today)))
                     .OrderBy(entry => entry.Left is null ? 1 : 0)
                     .ThenBy(entry => entry.Left is null ? 0 : entry.Next!.Value.DayNumber))
        {
            Memories.Add(new MemoryView(entry.Item.Id, entry.Item.Text, entry.Item.Created.ToLocalTime().ToString("d MMMM yyyy", Turkish),
                entry.Item.Source, entry.Left));
        }

        HasMemories = Memories.Count > 0;
        var upcoming = Memories.Count(memory => memory.HasCountdown);
        MemorySummary = Memories.Count == 0 ? "Henüz bilgi yok"
            : upcoming > 0 ? $"{Memories.Count} bilgi · {upcoming} yaklaşan tarih" : $"{Memories.Count} bilgi";
    }

    /// <summary>"Bugün" ekranındaki yaklaşanlar: kurulu hatırlatmalar ve 30 gün içinde tarihi gelen hafıza bilgileri.</summary>
    public void ShowToday(IReadOnlyList<Reminder> reminders, IEnumerable<MemoryItem> memories)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        Upcoming.Clear();
        var items = reminders.Select(item => (Sort: item.Due, View: new UpcomingView(item.Id, TurkishTime.Describe(item.Due, now),
                item.Text.Length > 0 ? TurkishGrammar.Capitalize(item.Text) : "Zamanlayıcı")))
            .Concat(memories
                .Select(item => (Item: item, Next: MemoryDates.NextOccurrence(item.Text, today)))
                .Where(entry => entry.Next is { } next && next.DayNumber - today.DayNumber <= 30)
                .Select(entry => (Sort: entry.Next!.Value.ToDateTime(TimeOnly.MinValue),
                    View: new UpcomingView(null, MemoryDates.Countdown(entry.Item.Text, today) ?? "", entry.Item.Text))))
            .OrderBy(entry => entry.Sort)
            .Take(8);
        foreach (var (_, view) in items)
        {
            Upcoming.Add(view);
        }

        HasUpcoming = Upcoming.Count > 0;
    }

    public void ShowNotes(IReadOnlyList<Note> notes)
    {
        RecentNotes.Clear();
        foreach (var note in notes.TakeLast(5).Reverse())
        {
            RecentNotes.Add(new NoteView(note.Time.Date == DateTime.Today ? note.Time.ToString("HH:mm", Turkish) : note.Time.ToString("d MMM", Turkish), note.Text));
        }

        HasNotes = RecentNotes.Count > 0;
    }

    public void ShowWeather(WeatherCard? card)
    {
        HasWeather = card is not null;
        if (card is null)
        {
            return;
        }

        WeatherTemperature = $"{card.Temperature}°";
        WeatherSummary = $"{card.Place} · {card.Description}";
        WeatherDetail = $"En yüksek {card.Max}° · en düşük {card.Min}° · yağış %{card.RainChance}";
    }

    public void ShowLedger(IEnumerable<LedgerEntry> entries)
    {
        Ledger.Clear();
        foreach (var entry in entries)
        {
            AddLedger(entry);
        }
    }

    public void AddLedger(LedgerEntry entry)
    {
        Ledger.Insert(0, new LedgerView(entry.Time.ToString("HH:mm", Turkish), entry.Category, entry.Detail, entry.Flow == DataFlow.Outbound));
        LocalCount = Ledger.Count(item => !item.IsOutbound);
        OutboundCount = Ledger.Count(item => item.IsOutbound);
        var microphone = Ledger.Where(item => item.Category == "Mikrofon").ToList();
        MicrophoneSummary = microphone.Count == 0
            ? "Bugün açılmadı"
            : $"Bugün {microphone.Count} kayıt · ses kaydedilmedi";
    }

    // ---------- Komutlar ----------

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        var text = Draft.Trim();
        Draft = "";
        _actions.Send(text);
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(Draft);

    [RelayCommand]
    private void Suggest(string text) => _actions.Send(text);

    [RelayCommand(CanExecute = nameof(CanAddMemory))]
    private void AddMemory()
    {
        _actions.Remember(NewMemory.Trim());
        NewMemory = "";
    }

    private bool CanAddMemory() => !string.IsNullOrWhiteSpace(NewMemory);

    [RelayCommand]
    private void ForgetMemory(string id) => _actions.Forget(id);

    [RelayCommand]
    private void ImportFile()
    {
        if (_actions.PickImportFile() is not { } file)
        {
            return;
        }

        try
        {
            PreviewImport(File.ReadAllText(file), Path.GetFileName(file));
        }
        catch (IOException exception)
        {
            MemoryMessage = $"Dosya okunamadı: {exception.Message}";
        }
    }

    [RelayCommand]
    private void TogglePaste() => IsPasteOpen = !IsPasteOpen;

    [RelayCommand]
    private void ParsePaste()
    {
        PreviewImport(PasteText, "");
        IsPasteOpen = false;
        PasteText = "";
    }

    [RelayCommand]
    private void ConfirmImport()
    {
        var chosen = ImportCandidates.Where(candidate => candidate.IsSelected).Select(candidate => candidate.Text).ToList();
        var added = _actions.ImportMemories(chosen, ImportSource);
        MemoryMessage = added == 0 ? "Yeni bilgi eklenmedi." : $"{added} bilgi eklendi ({ImportSource}).";
        CancelImport();
    }

    [RelayCommand]
    private void CancelImport()
    {
        ImportCandidates.Clear();
        HasImport = false;
    }

    [RelayCommand]
    private void ExportMemory() =>
        MemoryMessage = _actions.ExportMemories() is { } file ? $"Kaydedildi: {file}" : "Dışa aktarılamadı.";

    [RelayCommand]
    private void CopyForAi()
    {
        _actions.CopyMemoriesForAi();
        MemoryMessage = "Panoya kondu. ChatGPT, Claude ya da Gemini'ye yapıştırman yeter.";
    }

    [RelayCommand]
    private void DeleteReminder(string id) => _actions.DeleteReminder(id);

    [RelayCommand]
    private void OpenNotes() => _actions.OpenNotes();

    /// <summary>Kısayollar: "chat", "memory", "rhythm", "routines", "settings", "import".</summary>
    [RelayCommand]
    private void GoTo(string page)
    {
        IsToday = IsPrivacy = false;
        IsChat = page == "chat";
        IsMemory = page is "memory" or "import";
        IsRhythm = page == "rhythm";
        IsRoutines = page == "routines";
        IsSettings = page == "settings";
        if (page == "import")
        {
            ImportFile();
        }
    }

    [RelayCommand]
    private void SaveIntro()
    {
        if (!string.IsNullOrWhiteSpace(IntroName))
        {
            _settings.UserName = IntroName.Trim();
            UserName = _settings.UserName;
        }

        if (!string.IsNullOrWhiteSpace(IntroCity))
        {
            _settings.City = IntroCity.Trim();
            City = _settings.City;
        }

        _actions.Save(_settings);
        OnPropertyChanged(nameof(NeedsIntro));
        OnPropertyChanged(nameof(TimeGreeting));
        OnPropertyChanged(nameof(Greeting));
    }

    /// <summary>Ritim düğmeleri sesli komutla aynı yoldan gider: "Mola ver", "25 dakika odaklanacağım".</summary>
    [RelayCommand]
    private void Rhythm(string sentence) => _actions.Send(sentence);

    /// <summary>İçe aktarma önizlemesini doldurur (dosya, yapıştırılan metin ya da anlık görüntü).</summary>
    internal void PreviewImport(string text, string fileName)
    {
        var candidates = MemoryImport.Preview(text, _actions.KnownMemories());
        ImportCandidates.Clear();
        foreach (var candidate in candidates)
        {
            ImportCandidates.Add(new ImportCandidateView(candidate.Text, candidate.AlreadyKnown));
        }

        ImportSource = MemoryImport.GuessSource(text, fileName);
        var known = candidates.Count(candidate => candidate.AlreadyKnown);
        ImportSummary = candidates.Count == 0
            ? "Bu metinde ayrı bilgiler bulamadım."
            : $"{candidates.Count} bilgi bulundu" + (known > 0 ? $" · {known} tanesini zaten biliyorum" : "");
        HasImport = candidates.Count > 0;
        MemoryMessage = candidates.Count == 0 ? ImportSummary : "";
    }

    [RelayCommand]
    private void OpenSettingsFile() => _actions.OpenSettingsFile();

    [RelayCommand]
    private void OpenDataFolder() => _actions.OpenDataFolder();

    [RelayCommand]
    private void PreviewVoice()
    {
        if (SelectedVoice is { } voice)
        {
            EngineXtts = true;
            EngineAzure = false;
            EnginePiper = false;
            _actions.PreviewVoice(voice.Name);
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settings.SpeechEngine = EngineXtts ? "xtts" : EngineAzure ? "azure" : "piper";
        if (SelectedVoice is { } voice)
        {
            _settings.XttsSpeaker = voice.Name;
        }

        _settings.AzureSpeechKey = AzureKey.Trim();
        _settings.AzureSpeechRegion = string.IsNullOrWhiteSpace(AzureRegion) ? "westeurope" : AzureRegion.Trim();
        _settings.AzureVoice = VoiceAhmet ? "tr-TR-AhmetNeural" : "tr-TR-EmelNeural";
        _settings.Personality = PersonalityPlain ? "sade" : PersonalityFormal ? "resmi" : "esprili";
        _settings.FollowUpSeconds = FollowUpOff ? 0 : FollowUpChat ? 15 : FollowUpLong ? 7 : 4;
        _settings.City = City.Trim();
        _settings.MicrophoneDevice = SelectedMicrophone == "Otomatik" ? "" : SelectedMicrophone;
        _settings.UserName = UserName.Trim();
        _settings.WakeWord = WakeEnabled ? "hey_friday" : "";
        _settings.WakeWordThreshold = WakeSensitive ? 0.35 : WakeStrict ? 0.7 : 0.5;
        _settings.Rhythm = RhythmEnabled;
        _settings.BreakEveryMinutes = BreakEvery30 ? 30 : BreakEvery90 ? 90 : 50;
        _settings.MorningGreeting = MorningEnabled;
        SaveMoreSettings();
        _actions.Save(_settings);
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(WakeHint));
        OnPropertyChanged(nameof(WakeLabel));
        OnPropertyChanged(nameof(TimeGreeting));
        OnPropertyChanged(nameof(NeedsIntro));
        SavedMessage = $"Kaydedildi · {DateTime.Now:HH:mm}{_settingsNote}";
    }

    private void LoadSettingsFields()
    {
        var engine = _settings.SpeechEngine.Trim().ToLowerInvariant();
        EngineAzure = engine == "azure";
        EnginePiper = engine == "piper";
        EngineXtts = !EngineAzure && !EnginePiper;
        SelectedVoice = Voices.FirstOrDefault(voice => voice.Name == _settings.XttsSpeaker) ?? Voices[0];
        AzureKey = _settings.AzureSpeechKey;
        AzureRegion = _settings.AzureSpeechRegion;
        VoiceAhmet = _settings.AzureVoice.Contains("Ahmet", StringComparison.OrdinalIgnoreCase);
        VoiceEmel = !VoiceAhmet;
        PersonalityPlain = _settings.Personality == "sade";
        PersonalityFormal = _settings.Personality == "resmi";
        PersonalityWitty = !PersonalityPlain && !PersonalityFormal;
        FollowUpOff = _settings.FollowUpSeconds <= 0;
        FollowUpChat = _settings.FollowUpSeconds >= 12;
        FollowUpLong = _settings.FollowUpSeconds is > 5 and < 12;
        FollowUpShort = !FollowUpOff && !FollowUpLong && !FollowUpChat;
        City = _settings.City;
        SelectedMicrophone = Microphones.FirstOrDefault(name => _settings.MicrophoneDevice.Length > 0
            && name.Contains(_settings.MicrophoneDevice, StringComparison.OrdinalIgnoreCase)) ?? "Otomatik";
        UserName = _settings.UserName;
        WakeEnabled = _settings.WakeWord.Length > 0;
        WakeSensitive = _settings.WakeWordThreshold < 0.45;
        WakeStrict = _settings.WakeWordThreshold > 0.6;
        WakeNormal = !WakeSensitive && !WakeStrict;
        RhythmEnabled = _settings.Rhythm;
        BreakEvery30 = _settings.BreakEveryMinutes <= 35;
        BreakEvery90 = _settings.BreakEveryMinutes >= 75;
        BreakEvery50 = !BreakEvery30 && !BreakEvery90;
        MorningEnabled = _settings.MorningGreeting;
        LoadMoreSettings();
    }

    private void AddLine(ChatLine line)
    {
        Lines.Add(line);
        while (Lines.Count > MaxLines)
        {
            Lines.RemoveAt(0);
        }

        HasLines = Lines.Count > 0;
    }
}
