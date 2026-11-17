using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Friday.Core;
using Friday.Infrastructure;

namespace Friday.Desktop;

public sealed partial class App : Application, IDisposable
{
    private static readonly string[] FallbackHotkeys = ["Ctrl+Alt+F", "Ctrl+Alt+J", "Ctrl+Shift+Space", "Win+Shift+F"];

    private HttpClient? _http;
    private FridayAssistant? _assistant;
    private TrayIcon? _tray;
    private HudWindow? _hud;
    private MainWindow? _window;
    private CancellationTokenSource? _startup;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private FridayPaths? _paths;
    private DispatcherTimer? _weatherClock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--snapshot", var folder, ..])
        {
            // Tasarım denetimi: arayüzü örnek verilerle PNG olarak çizer, asistanı başlatmaz.
            Snapshot.RenderAll(folder);
            Shutdown();
            return;
        }

        // Tek Friday: ikinci kopya Ollama portu, mikrofon ve kısayol için ilkiyle yarışırdı. İkinci açılış yalnızca
        // çalışan Friday'in panelini öne getirir.
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Friday.Assistant.Show", out var first);
        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        try
        {
            var paths = FridayPaths.Resolve();
            FridayLog.Initialize(paths.Logs);
            FridayLog.Info("Friday başlıyor");
            // Beklenmeyen bir hata Friday'i kapatmaz; günlüğe yazılır ve asistan çalışmaya devam eder.
            DispatcherUnhandledException += (_, args) =>
            {
                FridayLog.Error("Arayüz hatası", args.Exception);
                args.Handled = true;
            };
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                FridayLog.Error("Arka plan görevi hatası", args.Exception);
                args.SetObserved();
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception exception)
                {
                    FridayLog.Error("Ölümcül hata", exception);
                }
            };

            Start(paths);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) =>
                Dispatcher.BeginInvoke(() => _window?.ShowFromTray()), null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (Exception exception)
        {
            FridayLog.Error("Başlatılamadı", exception);
            MessageBox.Show(exception.Message, "Friday başlatılamadı", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void Start(FridayPaths paths)
    {
        _paths = paths;
        // Önceki bir oturumdan kalan sahipsiz sunucular sahiplenilmez; kapatılır ve her şey bu Friday'in işine bağlı başlar.
        if (ChildProcessJob.KillStrays(paths.Tools) is > 0 and var strays)
        {
            FridayLog.Warn($"Önceki oturumdan kalan {strays} yardımcı süreç kapatıldı");
        }

        var store = new JsonSettingsStore(paths);
        var settings = store.Load();
        var memory = new MemoryStore(Path.Combine(paths.Data, "memory.json"));
        var ledger = new PrivacyLedger(Path.Combine(paths.Data, "ledger"));
        var history = new ConversationLog(Path.Combine(paths.Data, "history"));
        var reminders = new ReminderStore(Path.Combine(paths.Data, "reminders.json"));
        var notes = new NoteStore(Path.Combine(paths.Data, "notes.md"));

        // Yerel model yanıtları uzun sürebilir; zaman aşımını istekler kendi belirler.
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var ollama = new OllamaServer(paths, _http);
        var chat = new OllamaChatClient(_http, ollama);
        var rhythm = new RhythmCoach(Path.Combine(paths.Data, "rhythm"), ledger);
        var tools = new AssistantTools(_http, chat, memory, ledger, history, reminders, notes);
        var assistant = _assistant = new FridayAssistant(
            paths,
            new MicrophoneRecorder(),
            new WhisperTranscriber(),
            ollama,
            chat,
            new Speaker(paths),
            tools,
            memory,
            ledger,
            history,
            new WakeWordListener(paths.WakeWordModels),
            rhythm);

        var panel = new PanelViewModel(settings, new PanelActions
        {
            Send = assistant.SubmitText,
            Remember = text =>
            {
                memory.Remember(text);
                ledger.Record(DataFlow.Local, "Hafıza", $"Panodan eklendi: {text}");
            },
            Forget = memory.Remove,
            OpenSettingsFile = () => Open(paths.SettingsFile),
            OpenDataFolder = () => Open(paths.Data),
            PreviewVoice = assistant.PreviewVoice,
            Save = changed =>
            {
                store.Save(changed);
                assistant.ApplySettings();
            },
            PickImportFile = PickMemoryFile,
            KnownMemories = () => memory.Items,
            ImportMemories = (facts, source) =>
            {
                var added = memory.Import(facts, source);
                ledger.Record(DataFlow.Local, "Hafıza", $"{source} kaynağından {added} bilgi aktarıldı");
                return added;
            },
            ExportMemories = () => ExportMemory(paths, memory, settings.AssistantName),
            DeleteReminder = id => reminders.Remove(id),
            OpenNotes = () =>
            {
                if (!File.Exists(notes.File))
                {
                    notes.Add("Notlarım burada; Friday'e \"not al: …\" de.", DateTime.Now);
                }

                Open(notes.File);
            },
            CopyMemoriesForAi = () =>
            {
                ClipboardText.Set(memory.ToPortablePrompt());
                ledger.Record(DataFlow.Local, "Hafıza", "Başka bir yapay zekâ için panoya kopyalandı");
            }
        });
        var hud = new HudViewModel();
        panel.LoadHistory(history.Read(history.Today));
        panel.ShowMemories(memory.Items);
        panel.ShowLedger(ledger.TodayEntries);

        void Ui(Action action) => Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        void ShowRhythm()
        {
            panel.ShowRhythm(rhythm.Today, rhythm.CurrentStretch, rhythm.FocusEnd, settings.BreakEveryMinutes);
            panel.ShowWeek(rhythm.Week());
        }

        rhythm.Changed += (_, _) => Ui(ShowRhythm);
        ShowRhythm();

        // "Bugün" ekranı: yaklaşanlar, notlar ve saatte bir yenilenen hava.
        void ShowToday() => panel.ShowToday(reminders.Upcoming, memory.Items);
        reminders.Changed += (_, _) => Ui(ShowToday);
        memory.Changed += (_, _) => Ui(ShowToday);
        notes.Changed += (_, _) => Ui(() => panel.ShowNotes(notes.All));
        ShowToday();
        panel.ShowNotes(notes.All);
        async Task RefreshWeatherAsync()
        {
            var card = await tools.WeatherCardAsync(CancellationToken.None).ConfigureAwait(true);
            panel.ShowWeather(card);
        }

        _weatherClock = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _weatherClock.Tick += async (_, _) => await RefreshWeatherAsync().ConfigureAwait(true);
        _weatherClock.Start();
        _ = RefreshWeatherAsync();
        memory.Changed += (_, _) => Ui(() => panel.ShowMemories(memory.Items));
        ledger.Added += (_, entry) => Ui(() => panel.AddLedger(entry));
        assistant.StatusChanged += (_, status) => Ui(() =>
        {
            if (status.State == AssistantState.Listening && hud.State != AssistantState.Listening)
            {
                hud.BeginTurn();
            }

            hud.State = status.State;
            panel.OnStatus(status);
            panel.EngineLabel = assistant.VoiceLabel;
            if (status.State == AssistantState.Error)
            {
                hud.SetReply(status.Message);
            }
        });
        assistant.UserSaid += (_, text) => Ui(() =>
        {
            hud.UserText = text;
            hud.Caption = "";
            hud.ToolLabel = "";
            panel.OnUserSaid(text);
        });
        assistant.AssistantSaid += (_, text) => Ui(() =>
        {
            hud.ToolLabel = "";
            hud.SetReply(text);
            panel.OnAssistantSaid(text);
        });
        assistant.ToolStarted += (_, tool) => Ui(() => hud.SetTool(tool));
        assistant.MicrophoneLevel += (_, level) => Ui(() => { hud.Level = level; panel.Level = level; });
        assistant.OutputLevel += (_, level) => Ui(() =>
        {
            if (hud.State == AssistantState.Speaking)
            {
                hud.Level = level;
                panel.Level = level;
            }
        });

        var requested = HotkeyGesture.Parse(settings.Hotkey);
        var candidates = new[] { requested }
            .Concat(FallbackHotkeys.Select(HotkeyGesture.Parse).Where(gesture => gesture != requested))
            .ToList();
        _window = new MainWindow(panel, candidates, assistant.OnHotkey);
        MainWindow = _window;
        _hud = new HudWindow(hud);
        _tray = new TrayIcon(settings.AssistantName, _window.ShowFromTray, ExitFriday);
        _window.Show();

        // Show() kısayolu kaydeder. Ayardaki kısayol başka bir uygulamadaysa yedeklerden biri kullanılır.
        if (_window.ActiveHotkey is { } active)
        {
            settings.Hotkey = active.ToString();
            panel.Hotkey = settings.Hotkey;
            if (active != requested)
            {
                panel.HotkeyNote = $"{requested} başka bir uygulamada; {active} kullanılıyor";
            }
        }
        else
        {
            panel.HotkeyNote = "Kısayolların hepsi başka uygulamalarda; settings.json'da Hotkey'i değiştir.";
        }

        _startup = new CancellationTokenSource();
        _ = InitializeAsync(assistant, settings, panel, _startup.Token);
    }

    private static async Task InitializeAsync(FridayAssistant assistant, FridaySettings settings, PanelViewModel panel, CancellationToken cancellationToken)
    {
        try
        {
            await assistant.InitializeAsync(settings, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            panel.State = AssistantState.Error;
            panel.StatusText = exception.Message;
        }
    }

    private void ExitFriday() => Shutdown();

    /// <summary>Başka bir yapay zekânın hafıza dosyası; vazgeçilirse null.</summary>
    private string? PickMemoryFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Hafıza dosyası seç",
            Filter = "Metin, Markdown ya da JSON|*.txt;*.md;*.markdown;*.json|Tüm dosyalar|*.*"
        };
        return dialog.ShowDialog(_window) == true ? dialog.FileName : null;
    }

    /// <summary>Hafızayı D:\Friday\data\exports altına Markdown olarak yazar ve dosyayı klasöründe gösterir.</summary>
    private static string? ExportMemory(FridayPaths paths, MemoryStore memory, string assistantName)
    {
        try
        {
            var folder = Path.Combine(paths.Data, "exports");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"hafiza-{DateTime.Now:yyyy-MM-dd-HHmm}.md");
            File.WriteAllText(file, memory.ToMarkdown(assistantName));
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true })?.Dispose();
            return file;
        }
        catch (IOException exception)
        {
            FridayLog.Error("Hafıza dışa aktarılamadı", exception);
            return null;
        }
    }

    private static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    /// <summary>
    /// Kapanış kesindir: arayüz kapanır, asistan sunucularını düzgünce kapatır (en fazla 5 sn), iş nesnesi kalan her
    /// alt süreci öldürür, araç klasörü son kez taranır ve süreç Environment.Exit ile biter. Arkada açık kalan bir
    /// iş parçacığı Friday'i hayatta tutamaz; RAM'in ve VRAM'in tamamı Windows'a döner.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        var assistant = _assistant;
        _assistant = null;
        Dispose();
        var graceful = Task.Run(() => assistant?.Dispose());
        if (!graceful.Wait(TimeSpan.FromSeconds(5)))
        {
            FridayLog.Warn("Asistan 5 sn içinde kapanmadı; yardımcı süreçler zorla kapatılıyor");
        }

        ChildProcessJob.Shared.Dispose();
        if (_paths is not null && ChildProcessJob.KillStrays(_paths.Tools) is > 0 and var strays)
        {
            FridayLog.Info($"Kapanışta {strays} yardımcı süreç son taramada kapatıldı");
        }

        FridayLog.Info("Friday kapandı");
        base.OnExit(e);
        Environment.Exit(e.ApplicationExitCode);
    }

    public void Dispose()
    {
        _weatherClock?.Stop();
        _startup?.Cancel();
        _startup?.Dispose();
        _tray?.Dispose();
        _hud?.Close();
        // Friday'in başlattığı Piper, XTTS ve Ollama (llama-server ile birlikte) burada kapanır ve beklenir.
        // Friday bu satıra gelemeden ölürse aynı süreçleri ChildProcessJob kapatır.
        _assistant?.Dispose();
        _http?.Dispose();
        _http = null;
        _assistant = null;
        _tray = null;
        _hud = null;
        _startup = null;
        _showWait?.Unregister(null);
        _showSignal?.Dispose();
        _showWait = null;
        _showSignal = null;
    }
}
