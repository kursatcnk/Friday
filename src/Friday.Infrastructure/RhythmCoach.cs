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
        Prune();
    }

    /// <summary>Okunacak cümleler (öneri, hoş geldin, odak bitti). Friday boştaysa okur.</summary>
    public event EventHandler<IReadOnlyList<CoachStep>>? Speak;

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
                    Segments = [.. day.Segments]
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
                _tracker.BreakEvery, _tracker.BreakLength, _tracker.IsAway, _tracker.SnoozedUntil);
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

    public QuickReply StartBreak()
    {
        lock (_gate)
        {
            _where = CurrentWindow();
            _tracker.StartBreak(DateTime.Now);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        lock (_gate)
        {
            return new QuickReply(RhythmCoachLines.BreakStarted(_tracker.BreakLength));
        }
    }

    /// <summary>"Moladan döndüm": mola erken biter, kaldığın yer söylenir.</summary>
    public QuickReply EndBreak()
    {
        TimeSpan spent;
        string? where;
        lock (_gate)
        {
            spent = _tracker.EndBreak(DateTime.Now);
            where = _where;
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(spent <= TimeSpan.Zero
            ? "Şu an bir molada değilsin."
            : $"Hoş geldin, {RhythmCoachLines.Span(spent)} mola verdin." + (where is null ? "" : $" Kaldığın yer: {where}."));
    }

    public QuickReply StartFocus(double minutes)
    {
        var length = TimeSpan.FromMinutes(Math.Clamp(minutes <= 0 ? 25 : minutes, 5, 240));
        lock (_gate)
        {
            _tracker.StartFocus(DateTime.Now, length);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(RhythmCoachLines.FocusStarted(length));
    }

    public QuickReply EndFocus()
    {
        TimeSpan spent;
        lock (_gate)
        {
            spent = _tracker.EndFocus(DateTime.Now);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new QuickReply(spent < TimeSpan.FromMinutes(1)
            ? "Şu an açık bir odak oturumu yok."
            : $"Odak bitti; {RhythmCoachLines.Span(spent)} odaklandın.");
    }

    public QuickReply Snooze(double minutes)
    {
        var length = TimeSpan.FromMinutes(Math.Clamp(minutes <= 0 ? 15 : minutes, 5, 120));
        lock (_gate)
        {
            _tracker.Snooze(DateTime.Now, length);
        }

        return new QuickReply(RhythmCoachLines.Snoozed(length));
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
                Say(RhythmCoachLines.FocusEnded(item.Length, Today, _tracker.BreakLength), mustTell: true);
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

    private void Save() => File.WriteAllText(FileFor(_tracker.Today.Date), JsonSerializer.Serialize(_tracker.Today, Json));

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
