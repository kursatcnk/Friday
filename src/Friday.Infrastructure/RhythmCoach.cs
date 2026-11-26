using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Çalışma ritmi koçu. 20 sn'de bir yalnızca "son klavye/fare kullanımı ne zamandı" sorusunu sorar (tuşlar, ekran ve
/// pencere içerikleri okunmaz). Uzun süre aralıksız çalışılınca doğal bir duraksamada mola önerir; tam ekran bir oyun,
/// video ya da sunum açıksa ya da Friday o an konuşuyorsa bekler. Moladan dönünce kaldığın yeri, odak bitince günün
/// özetini söyler. Günlük süreler D:\Friday\data\rhythm altında tutulur.
/// </summary>
public sealed partial class RhythmCoach : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RetryLater = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _folder;
    private readonly PrivacyLedger _ledger;
    private readonly RhythmCoachLines _lines = new(new Random());
    private readonly object _gate = new();
    private readonly List<IReadOnlyList<CoachStep>> _pending = [];
    private readonly RhythmTracker _tracker;
    private readonly bool _freshDay;
    private Timer? _timer;
    private string? _where;
    private bool _morning = true;
    private bool _greeted;
    private bool _announced;

    public RhythmCoach(string folder, PrivacyLedger ledger)
    {
        _folder = folder;
        _ledger = ledger;
        Directory.CreateDirectory(folder);
        var today = Load(DateOnly.FromDateTime(DateTime.Now));
        _freshDay = today is null;
        _tracker = new RhythmTracker(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5), today);
        RestoreLive();
        Prune();
    }

    /// <summary>Okunacak cümleler (öneri, hoş geldin, odak bitti). Friday boştaysa okur.</summary>
    public event EventHandler<IReadOnlyList<CoachStep>>? Speak;

    /// <summary>Odak süresi verilince o sürede neyle uğraşıldığını söyleyen cümle (ekran süresi açıksa); yoksa null.</summary>
    public Func<TimeSpan, string?>? FocusInsight { get; set; }

    /// <summary>Günün ilk oturuşu: Friday sabah karşılamasını yapar.</summary>
    public event EventHandler? MorningGreeting;

    /// <summary>Panel için: günün süreleri değişti.</summary>
    public event EventHandler? Changed;

    /// <summary>Friday konuşmuyor ve dinlemiyorsa true; öneriler ancak o zaman söylenir.</summary>
    public Func<bool> CanSpeak { get; set; } = () => true;

    public bool Enabled { get; private set; }

    /// <summary>Panel için günün kopyası.</summary>
    public RhythmDay Today
    {
        get
        {
            lock (_gate)
            {
                var day = _tracker.Today;
                return new RhythmDay
                {
                    Date = day.Date,
                    ActiveMinutes = day.ActiveMinutes,
                    FocusMinutes = day.FocusMinutes,
                    Breaks = day.Breaks,
                    LongestStretchMinutes = day.LongestStretchMinutes,
                    Water = day.Water,
                    Segments = [.. day.Segments],
                    Sessions = [.. day.Sessions]
                };
            }
        }
    }

    public TimeSpan CurrentStretch
    {
        get
        {
            lock (_gate)
            {
                return _tracker.CurrentStretch(DateTime.Now);
            }
        }
    }

    public DateTime? FocusEnd
    {
        get
        {
            lock (_gate)
            {
                return _tracker.FocusEnd;
            }
        }
    }

    /// <summary>Panelin canlı geri sayımı için o anki durum.</summary>
    public RhythmSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new RhythmSnapshot(Enabled, _tracker.StretchStart, _tracker.FocusStart, _tracker.FocusEnd, _tracker.BreakStart,
                _tracker.BreakEvery, _tracker.CurrentBreakLength, _tracker.IsAway, _tracker.SnoozedUntil, _tracker.FocusPausedAt,
                _tracker.SeriesRound, _tracker.SeriesRounds);
        }
    }

    public void Apply(FridaySettings settings)
    {
        lock (_gate)
        {
            _tracker.BreakEvery = TimeSpan.FromMinutes(Math.Clamp(settings.BreakEveryMinutes, 15, 240));
            _tracker.BreakLength = TimeSpan.FromMinutes(Math.Clamp(settings.BreakMinutes, 1, 60));
            _morning = settings.MorningGreeting;
            Enabled = settings.Rhythm;
            if (Enabled && _timer is null)
            {
                _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TickInterval);
            }
            else if (!Enabled)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        if (Enabled && !_announced)
        {
            _announced = true;
            _ledger.Record(DataFlow.Local, "Ritim",
                "Yalnızca son klavye/fare kullanım zamanı okunuyor; tuşlar, ekran ve pencere içerikleri okunmuyor");
        }
    }

    /// <summary>Mola başlar; süren odak biter, pomodoro serisi durur (seri kendi molalarını kendisi verir).</summary>
    public QuickReply StartBreak()
    {
        bool stoppedSeries;
        lock (_gate)
        {
            _where = CurrentWindow();
            stoppedSeries = _tracker.SeriesRound > 0;
            _tracker.StopSeries();
            _tracker.StartBreak(DateTime.Now);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        lock (_gate)
        {
            return new QuickReply((stoppedSeries ? "Pomodoro serisini durdurdum. " : "") + RhythmCoachLines.BreakStarted(_tracker.BreakLength));
        }
    }

    /// <summary>"Moladan döndüm": mola erken biter, kaldığın yer söylenir.</summary>
    public QuickReply EndBreak()
    {
        TimeSpan spent;
        string? where;
        lock (_gate)
        {
            // Seri molası erken bitirilirse sıradaki tur hemen başlar.
            if (_tracker.SeriesRound > 0 && _tracker.BreakStart is not null)
            {
                _tracker.NextRound(DateTime.Now);
                var round = (_tracker.SeriesRound, _tracker.SeriesRounds);
                Save();
                Changed?.Invoke(this, EventArgs.Empty);
                return new QuickReply($"Pomodoro {round.SeriesRound}/{round.SeriesRounds}: 25 dakika odak, başla.");
            }

            spent = _tracker.EndBreak(DateTime.Now);
            where = _where;
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(spent <= TimeSpan.Zero
            ? "Şu an bir molada değilsin."
            : $"Hoş geldin, {RhythmCoachLines.Span(spent)} mola verdin." + (where is null ? "" : $" Kaldığın yer: {where}."));
    }

    /// <param name="task">Odağın işi; oturum bu adla kaydedilir ("bu hafta rapora ne kadar çalıştım").</param>
    public QuickReply StartFocus(double minutes, string task = "")
    {
        var length = TimeSpan.FromMinutes(Math.Clamp(minutes <= 0 ? 25 : minutes, 5, 240));
        lock (_gate)
        {
            _tracker.StopSeries();
            _tracker.StartFocus(DateTime.Now, length, task);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(RhythmCoachLines.FocusStarted(length));
    }

    /// <summary>Odak (seri sürüyorsa bütün seri) biter.</summary>
    public QuickReply EndFocus()
    {
        TimeSpan spent;
        bool hadFocus;
        int series;
        lock (_gate)
        {
            hadFocus = _tracker.InFocus;
            series = _tracker.SeriesRound;
            _tracker.StopSeries();
            spent = _tracker.EndFocus(DateTime.Now);
            if (series > 0 && _tracker.BreakStart is not null)
            {
                _tracker.EndBreak(DateTime.Now);
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        if (series > 0 && !hadFocus)
        {
            return new QuickReply("Pomodoro serisini durdurdum.");
        }

        var lead = series > 0 ? "Pomodoro serisini durdurdum. " : "";
        return new QuickReply(!hadFocus
            ? "Şu an açık bir odak oturumu yok."
            : spent < TimeSpan.FromMinutes(1)
            ? lead + "Odak bitti."
            : $"{lead}Odak bitti; {RhythmCoachLines.Span(spent)} odaklandın.{(FocusInsight?.Invoke(spent) is { } insight ? " " + insight : "")}");
    }

    /// <summary>Odağı duraklatır; kalan süre korunur.</summary>
    public QuickReply PauseFocus()
    {
        bool paused;
        TimeSpan left = TimeSpan.Zero;
        lock (_gate)
        {
            paused = _tracker.PauseFocus(DateTime.Now);
            SaveQuietly();
            if (_tracker.FocusEnd is { } end && _tracker.FocusPausedAt is { } at)
            {
                left = end - at;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(paused
            ? $"Odağı duraklattım; {RhythmCoachLines.Span(left)} kaldı. Hazır olunca \"odağa devam\" de."
            : left > TimeSpan.Zero ? "Odak zaten duraklatılmış." : "Şu an açık bir odak oturumu yok.");
    }

    /// <summary>Duraklatılan odak kaldığı yerden sürer.</summary>
    public QuickReply ResumeFocus()
    {
        bool resumed;
        DateTime? end;
        lock (_gate)
        {
            resumed = _tracker.ResumeFocus(DateTime.Now);
            SaveQuietly();
            end = _tracker.FocusEnd;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(resumed && end is { } at
            ? $"Devam; odak {at.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.TimeSuffix(at)} bitiyor."
            : end is not null ? "Odak zaten sürüyor." : "Duraklatılmış bir odak yok.");
    }

    /// <summary>Süren odağı uzatır ("10 dakika daha").</summary>
    public QuickReply ExtendFocus(double minutes)
    {
        var by = TimeSpan.FromMinutes(Math.Clamp(minutes <= 0 ? 10 : minutes, 1, 120));
        bool extended;
        DateTime? end;
        lock (_gate)
        {
            extended = _tracker.ExtendFocus(by);
            SaveQuietly();
            end = _tracker.FocusEnd;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(extended && end is { } at
            ? $"Odağı {RhythmCoachLines.Span(by)} uzattım; {at.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.TimeSuffix(at)} bitiyor."
            : "Şu an açık bir odak oturumu yok; \"25 dakika odaklanacağım\" diyerek başlatabilirsin.");
    }

    /// <summary>Süren molayı uzatır ("5 dakika daha mola").</summary>
    public QuickReply ExtendBreak(double minutes)
    {
        var by = TimeSpan.FromMinutes(Math.Clamp(minutes <= 0 ? 5 : minutes, 1, 60));
        bool extended;
        DateTime? end = null;
        lock (_gate)
        {
            extended = _tracker.ExtendBreak(by);
            if (_tracker.BreakStart is { } start)
            {
                end = start + _tracker.CurrentBreakLength;
            }

            SaveQuietly();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(extended && end is { } at
            ? $"Molayı {RhythmCoachLines.Span(by)} uzattım; {at.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.ToSuffix(at)} kadar."
            : "Şu an bir molada değilsin.");
    }

    /// <summary>Pomodoro serisi (25 dk odak + 5 dk mola); turlar ritim koçunun saatiyle ilerler.</summary>
    public QuickReply StartSeries(int rounds, string task = "")
    {
        rounds = Math.Clamp(rounds, 1, 8);
        lock (_gate)
        {
            _tracker.StartSeries(DateTime.Now, rounds, task);
            SaveQuietly();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(rounds == 1
            ? "Bir pomodoro başladı: 25 dakika odak."
            : $"{rounds} pomodoro başladı: 25 dakika odak, 5 dakika mola. İlk tur şimdi.");
    }

    /// <summary>
    /// Odak oturumlarının dökümü: iş verilirse o işe harcanan süre ("bu hafta rapora ne kadar çalıştım"), verilmezse işlere
    /// göre dağılım. Dönem: bugun, dun, hafta (son 7 gün) ya da ay (son 30 gün).
    /// </summary>
    public QuickReply FocusSessions(string task, string period)
    {
        var (days, label) = period switch
        {
            "dun" => (History(2).Take(1).ToList(), "Dün"),
            "hafta" => (History(7).ToList(), "Bu hafta"),
            "ay" => (History(30).ToList(), "Son 30 günde"),
            _ => (History(1).ToList(), "Bugün")
        };
        var sessions = days.SelectMany(day => day.Sessions).ToList();
        lock (_gate)
        {
            // Süren odak da (bitmemiş olsa bile) sayılır.
            if (period is not "dun" && _tracker.FocusStart is { } start && (_tracker.FocusPausedAt ?? DateTime.Now) - start is { TotalMinutes: >= 1 } running)
            {
                sessions.Add(new FocusSession(start, Math.Round(running.TotalMinutes, 1), _tracker.FocusTask));
            }
        }

        return new QuickReply(FocusLog.Describe(sessions, task, label));
    }

    /// <summary>Pomodoro serisi ve süren turu (odak ya da mola) biter.</summary>
    public QuickReply StopSeries()
    {
        int round;
        lock (_gate)
        {
            round = _tracker.SeriesRound;
        }

        return round == 0 ? new QuickReply("Süren bir pomodoro serisi yok.") : EndFocus();
    }

    public QuickReply Snooze(double minutes)
    {
        var length = TimeSpan.FromMinutes(Math.Clamp(minutes <= 0 ? 15 : minutes, 5, 120));
        lock (_gate)
        {
            _tracker.Snooze(DateTime.Now, length);
            SaveQuietly();
        }

        return new QuickReply(RhythmCoachLines.Snoozed(length));
    }

    /// <summary>
    /// Mola önerileri bir süre susar (süre yoksa yarın sabaha kadar); çalışma süresi ölçülmeye devam eder. Eskiden en fazla
    /// iki saat ertelenebiliyordu ve panelden ertelemeyi geri almak mümkün değildi.
    /// </summary>
    public QuickReply PauseSuggestions(TimeSpan? length)
    {
        var now = DateTime.Now;
        var until = length is { } span ? now + span : now.Date.AddDays(1).AddHours(5);
        lock (_gate)
        {
            _tracker.Snooze(now, until - now);
            SaveQuietly();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(length is null ? "Mola önerileri bugünlük kapalı." : $"Mola önerileri {until.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.ToSuffix(until)} kadar kapalı.");
    }

    /// <summary>Durdurulan öneriler hemen sürer.</summary>
    public QuickReply ResumeSuggestions()
    {
        lock (_gate)
        {
            _tracker.Snooze(DateTime.Now, TimeSpan.Zero);
            SaveQuietly();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply("Mola önerileri yeniden açık.");
    }

    public QuickReply Water()
    {
        int glasses;
        lock (_gate)
        {
            glasses = _tracker.DrinkWater();
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(RhythmCoachLines.Water(glasses));
    }

    public static QuickReply Breathe() => new("Nefes egzersizi başlıyor.", Script: RhythmCoachLines.Breathing());

    public static QuickReply Stretch() => new("Esneme başlıyor.", Script: RhythmCoachLines.Stretch());

    public QuickReply Status()
    {
        lock (_gate)
        {
            return new QuickReply(RhythmCoachLines.Status(_tracker.Today, _tracker.CurrentStretch(DateTime.Now)));
        }
    }

    /// <summary>Son 7 günün ritmi (bugün dahil), eskiden yeniye.</summary>
    public IReadOnlyList<RhythmDay> Week()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var days = new List<RhythmDay>();
        for (var offset = 6; offset >= 0; offset--)
        {
            var date = today.AddDays(-offset);
            days.Add(offset == 0 ? Today : Load(date) ?? new RhythmDay { Date = date });
        }

        return days;
    }

    public QuickReply WeekSummary() => new(RhythmCoachLines.Week(Week()));

    /// <summary>Son N gün, eskiden yeniye (son eleman bugün); seri hesabı için.</summary>
    public IReadOnlyList<RhythmDay> History(int days)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var history = new List<RhythmDay>();
        for (var offset = Math.Max(1, days) - 1; offset >= 0; offset--)
        {
            var date = today.AddDays(-offset);
            history.Add(offset == 0 ? Today : Load(date) ?? new RhythmDay { Date = date });
        }

        return history;
    }

    /// <summary>Son moladan önce öndeki pencere ("Visual Studio Code, FridayAssistant.cs"); yoksa null.</summary>
    public string? LastPlace
    {
        get
        {
            lock (_gate)
            {
                return _where;
            }
        }
    }

    public QuickReply Where()
    {
        lock (_gate)
        {
            return new QuickReply(RhythmCoachLines.Where(_where));
        }
    }

    private void Tick()
    {
        try
        {
            var now = DateTime.Now;
            var idle = IdleTime();
            IReadOnlyList<RhythmEvent> events;
            lock (_gate)
            {
                events = _tracker.Update(now, idle);
                Save();
            }

            // Uygun olmayan anda söylenemeyen cümleler bir sonraki yoklamada yeniden denenir.
            if (_pending.Count > 0 && CanSpeak() && !IsFullScreen())
            {
                var next = _pending[0];
                _pending.RemoveAt(0);
                Speak?.Invoke(this, next);
            }

            foreach (var item in events)
            {
                Handle(item, now);
            }

            // Uygulama günün ilk saatlerinde açıldıysa sabah karşılaması ilk etkinlikte yapılır.
            if (_freshDay && !_greeted && idle < TimeSpan.FromMinutes(1) && now.Hour is >= 5 and < 12)
            {
                Greet();
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            FridayLog.Error("Ritim kaydedilemedi", exception);
        }
    }

    private void Handle(RhythmEvent item, DateTime now)
    {
        switch (item.Kind)
        {
            case RhythmEventKind.SuggestBreak:
                if (!CanSpeak() || IsFullScreen())
                {
                    lock (_gate)
                    {
                        _tracker.Postpone(now, RetryLater);
                    }

                    return;
                }

                lock (_gate)
                {
                    _where = CurrentWindow();
                    Speak?.Invoke(this, _lines.Suggestion(item.Length, now, _tracker.Today));
                }

                break;
            case RhythmEventKind.FocusEnded:
                Say(RhythmCoachLines.FocusEnded(item.Length, Today, _tracker.BreakLength) + (FocusInsight?.Invoke(item.Length) is { } insight ? " " + insight : ""), mustTell: true);
                break;
            case RhythmEventKind.BreakOver:
                Say(RhythmCoachLines.BreakOver(_where), mustTell: false);
                break;
            case RhythmEventKind.BackFromBreak:
                Say(RhythmCoachLines.Welcome(item.Length, _where), mustTell: false);
                break;
            case RhythmEventKind.DayStarted:
                Greet();
                break;
            case RhythmEventKind.SeriesBreak:
                lock (_gate)
                {
                    _where = CurrentWindow();
                }

                Say($"Pomodoro {item.Round}/{item.Rounds} bitti. 5 dakika mola: kalk, biraz esne, su iç.", mustTell: true);
                break;
            case RhythmEventKind.SeriesNext:
                Say($"Pomodoro {item.Round}/{item.Rounds}: 25 dakika odak, başla." + (_where is { } place ? $" Kaldığın yer: {place}." : ""), mustTell: true);
                break;
            case RhythmEventKind.SeriesDone:
                Say($"{item.Rounds} pomodoro tamam, harika iş! Bugün toplam {RhythmCoachLines.Span(TimeSpan.FromMinutes(Today.FocusMinutes))} odaklandın. "
                    + "Şimdi 15 dakikalık uzun bir molayı hak ettin.", mustTell: true);
                break;
        }
    }

    private void Greet()
    {
        _greeted = true;
        if (_morning)
        {
            MorningGreeting?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Say(string text, bool mustTell)
    {
        IReadOnlyList<CoachStep> script = [new CoachStep(text)];
        if (CanSpeak() && !IsFullScreen())
        {
            Speak?.Invoke(this, script);
        }
        else if (mustTell)
        {
            _pending.Add(script);
        }
    }

    private static string? CurrentWindow() =>
        Desktop.Foreground() is { } window ? RhythmCoachLines.DescribeWindow(window.Title, window.Process) : null;

    private string FileFor(DateOnly date) => Path.Combine(_folder, $"{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json");

    private RhythmDay? Load(DateOnly date)
    {
        var file = FileFor(date);
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<RhythmDay>(File.ReadAllText(file), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Save()
    {
        File.WriteAllText(FileFor(_tracker.Today.Date), JsonSerializer.Serialize(_tracker.Today, Json));
        SaveLive();
    }

    /// <summary>Panelden ya da sesle verilen komutlarda kayıt hatası komutu bozmaz; günlüğe yazılır.</summary>
    private void SaveQuietly()
    {
        try
        {
            Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FridayLog.Error("Ritim kaydedilemedi", exception);
        }
    }

    private string LiveFile => Path.Combine(_folder, "live-state.json");

    /// <summary>Odak, mola, seri ve susturma yeniden başlatmada kaybolmasın diye ayrı bir dosyada tutulur.</summary>
    private void SaveLive()
    {
        var live = _tracker.Live();
        var idle = live.FocusStart is null && live.BreakStart is null && live.SnoozedUntil is null;
        if (idle)
        {
            if (File.Exists(LiveFile))
            {
                File.Delete(LiveFile);
            }

            return;
        }

        File.WriteAllText(LiveFile, JsonSerializer.Serialize(live, Json));
    }

    private void RestoreLive()
    {
        try
        {
            if (File.Exists(LiveFile) && JsonSerializer.Deserialize<RhythmLive>(File.ReadAllText(LiveFile), Json) is { } live)
            {
                _tracker.Restore(live, DateTime.Now);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            FridayLog.Error("Ritim durumu okunamadı", exception);
        }
    }

    /// <summary>90 günden eski ritim dosyaları silinir.</summary>
    private void Prune()
    {
        foreach (var file in Directory.EnumerateFiles(_folder, "*.json")
                     .Where(file => File.GetLastWriteTime(file) < DateTime.Now.AddDays(-90)))
        {
            File.Delete(file);
        }
    }

    /// <summary>Son klavye ya da fare kullanımından bu yana geçen süre (Windows'un tuttuğu tek bir sayı).</summary>
    internal static TimeSpan IdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return NativeMethods.GetLastInputInfo(ref info)
            ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time))
            : TimeSpan.Zero;
    }

    /// <summary>Öndeki pencere bütün ekranı kaplıyorsa (oyun, film, sunum) araya girilmez.</summary>
    private static unsafe bool IsFullScreen()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var name = stackalloc char[64];
        var length = NativeMethods.GetClassNameW(window, name, 64);
        var className = new string(name, 0, Math.Max(0, length));
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return NativeMethods.GetWindowRect(window, out var rect)
               && NativeMethods.GetMonitorInfoW(NativeMethods.MonitorFromWindow(window, 2), ref monitor)
               && rect.Left <= monitor.Monitor.Left && rect.Top <= monitor.Monitor.Top
               && rect.Right >= monitor.Monitor.Right && rect.Bottom >= monitor.Monitor.Bottom;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            try
            {
                Save();
            }
            catch (IOException)
            {
                // Kapanışta yazılamazsa son 20 sn kaybolur.
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetLastInputInfo(ref LastInputInfo info);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        internal static unsafe partial int GetClassNameW(IntPtr window, char* name, int max);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(IntPtr window, out Rect rect);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    }
}
