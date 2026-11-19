using System.Diagnostics;
using System.Text.Json;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Ekran süresini sayar: 15 sn'de bir öndeki uygulamanın adına bakar (kullanıcı 2 dakikadır klavyeye ya da fareye
/// dokunmadıysa saymaz). Tarayıcıda yalnızca başlıktan bilinen bir sitenin adı çıkarılır; başlık saklanmaz. Bir yoklama
/// mikrosaniyeler sürer; dosyaya 5 dakikada bir yazılır.
/// </summary>
public sealed class ActivityTracker : IDisposable
{
    private const int TickSeconds = 15;
    private static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(2);

    private readonly string _file;
    private readonly ScreenTimeLog _log;
    private readonly Dictionary<string, string?> _descriptions = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;
    private DateTime _saved = DateTime.Now;
    private int _distractedSeconds;
    private bool _warned;
    private bool _away;

    /// <summary>Son 4 saatin örnekleri (15 sn'de bir); odak oturumunun özetini çıkarmak için. Diske yazılmaz.</summary>
    private readonly Queue<(DateTime At, string Label)> _recent = new();
    private string _lastLabel = "";

    public ActivityTracker(string file)
    {
        _file = file;
        _log = Load(file);
    }

    /// <summary>Dikkat dağıtan bir sitede ~45 sn kalındı (YouTube, Netflix, Instagram…); site adıyla.</summary>
    public event EventHandler<string>? Distracted;

    /// <summary>Öne başka bir uygulama geldi: (gösterilen ad, süreç adı).</summary>
    public event EventHandler<(string Label, string Process)>? AppFocused;

    /// <summary>Kullanıcı en az 5 dakika uzakta kaldıktan sonra klavyeye ya da fareye döndü.</summary>
    public event EventHandler? Returned;

    /// <summary>Ekran süresi kaydı (kapalıyken yalnızca olaylar için yoklanır, süre yazılmaz).</summary>
    public bool Records { get; set; } = true;

    public bool IsRunning => _timer is not null;

    public void Start() => _timer ??= new Timer(_ => Tick(), null, TimeSpan.FromSeconds(TickSeconds), TimeSpan.FromSeconds(TickSeconds));

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        Save();
    }

    public IReadOnlyList<(string App, int Seconds)> Day(DateTime when)
    {
        lock (_log)
        {
            return _log.Day(when);
        }
    }

    /// <summary>Bu andan beri hangi uygulamada ne kadar kalındı (en çok önce).</summary>
    public IReadOnlyList<(string App, int Seconds)> Since(DateTime since)
    {
        lock (_log)
        {
            return [.. _recent.Where(item => item.At >= since).GroupBy(item => item.Label)
                .Select(group => (group.Key, group.Count() * TickSeconds)).OrderByDescending(item => item.Item2)];
        }
    }

    public string Describe(DateTime when, string dayName, string? app = null)
    {
        lock (_log)
        {
            return _log.Describe(when, dayName, app);
        }
    }

    private void Tick()
    {
        try
        {
            var idle = RhythmCoach.IdleTime();
            if (idle > AwayAfter || Desktop.Foreground() is not { } window)
            {
                _distractedSeconds = 0;
                _away |= idle > TimeSpan.FromMinutes(5);
                return;
            }

            if (_away)
            {
                _away = false;
                Returned?.Invoke(this, EventArgs.Empty);
            }

            var label = ScreenTimeLog.Label(window.Process, Description(window.ProcessId, window.Process), window.Title);
            if (label != _lastLabel)
            {
                _lastLabel = label;
                AppFocused?.Invoke(this, (label, window.Process));
            }

            if (Records)
            {
                lock (_log)
                {
                    _log.Add(DateTime.Now, label, TickSeconds);
                    _recent.Enqueue((DateTime.Now, label));
                    while (_recent.Count > 0 && _recent.Peek().At < DateTime.Now.AddHours(-4))
                    {
                        _recent.Dequeue();
                    }
                }
            }

            if (ScreenTimeLog.IsDistraction(label))
            {
                _distractedSeconds += TickSeconds;
                if (_distractedSeconds >= 45 && !_warned)
                {
                    _warned = true;
                    Distracted?.Invoke(this, label);
                }
            }
            else
            {
                _distractedSeconds = 0;
                _warned = false;
            }

            if (DateTime.Now - _saved > TimeSpan.FromMinutes(5))
            {
                Save();
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            FridayLog.Warn($"Ekran süresi: {exception.Message}");
        }
    }

    /// <summary>"Code" → "Visual Studio Code" (programın kendi açıklaması); süreç adına göre bir kez okunur.</summary>
    private string? Description(int processId, string process)
    {
        if (_descriptions.TryGetValue(process, out var cached))
        {
            return cached;
        }

        string? description = null;
        try
        {
            using var running = Process.GetProcessById(processId);
            if (running.MainModule?.FileName is { } path)
            {
                description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Yönetici olarak çalışan program: süreç adı kullanılır.
        }

        _descriptions[process] = description;
        return description;
    }

    private void Save()
    {
        try
        {
            string json;
            lock (_log)
            {
                json = _log.ToJson();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, json);
            _saved = DateTime.Now;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FridayLog.Warn($"Ekran süresi kaydedilemedi: {exception.Message}");
        }
    }

    private static ScreenTimeLog Load(string file)
    {
        try
        {
            return File.Exists(file) ? ScreenTimeLog.FromJson(File.ReadAllText(file)) : new ScreenTimeLog();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            FridayLog.Warn($"Ekran süresi okunamadı: {exception.Message}");
            return new ScreenTimeLog();
        }
    }

    public void Dispose() => Stop();
}
