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
        var store = new JsonSettingsStore(paths);
        var settings = store.Load();
        var memory = new MemoryStore(Path.Combine(paths.Data, "memory.json"));
        var ledger = new PrivacyLedger(Path.Combine(paths.Data, "ledger"));
        var history = new ConversationLog(Path.Combine(paths.Data, "history"));

        // Yerel model yanıtları uzun sürebilir; zaman aşımını istekler kendi belirler.
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var ollama = new OllamaServer(paths, _http);
        var chat = new OllamaChatClient(_http, ollama);
        var assistant = _assistant = new FridayAssistant(
            paths,
            new MicrophoneRecorder(),
            new WhisperTranscriber(),
            ollama,
            chat,
            new Speaker(paths),
            new AssistantTools(_http, chat, memory, ledger, history),
            memory,
            ledger,
            history);

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
                assistant.ReconfigureVoice();
            }
        });
        var hud = new HudViewModel();
        panel.LoadHistory(history.Read(history.Today));
        panel.ShowMemories(memory.Items);
        panel.ShowLedger(ledger.TodayEntries);

        void Ui(Action action) => Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
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

    private static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
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
