using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Friday.Core;
using Friday.Infrastructure;

namespace Friday.Desktop;

public sealed partial class App : Application, IDisposable
{
    private static readonly string[] FallbackHotkeys = ["Ctrl+Alt+F", "Ctrl+Alt+J", "Ctrl+Shift+Space", "Win+Shift+F"];

    private HttpClient? _http;
    private FridayAssistant? _assistant;
    private AssistantTools? _tools;
    private TrayIcon? _tray;

    /// <summary>Rutinlerin genel kısayolları (Ayarlar'da değişince yeniden kaydedilir).</summary>
    private readonly List<GlobalHotkey> _routineKeys = [];

    private FridaySettings? _settings;

    private int _screenTimeTicks;

    /// <summary>Vurgu rengi ve arayüz boyutu. Fırça değiştirilir (dondurulmamışsa), böylece açık pencereler de hemen güncellenir.</summary>
    private void ApplyLook(FridaySettings settings)
    {
        try
        {
            if (ColorConverter.ConvertFromString(settings.AccentColor) is Color accent)
            {
                if (Resources["AccentBrush"] is SolidColorBrush { IsFrozen: false } brush)
                {
                    brush.Color = accent;
                }
                else
                {
                    Resources["AccentBrush"] = new SolidColorBrush(accent);
                }

                Resources["AccentColor"] = accent;
            }
        }
        catch (FormatException)
        {
            FridayLog.Warn($"Vurgu rengi anlaşılamadı: {settings.AccentColor}");
        }

        _window?.ApplyScale(Math.Clamp(settings.UiScale, 0.8, 1.4));
    }

    private (ulong Idle, ulong Total)? _cpuBefore;

    private GpuReading? _gpu;

    private DateTime _gpuAt;

    /// <summary>Sistem kartı: işlemci (iki ölçüm arası), bellek, ekran kartı (nvidia-smi, 15 sn'de bir), Friday'in diski.</summary>
    private async Task RefreshSystemAsync(PanelViewModel panel, FridayPaths paths)
    {
        var now = SystemProbe.CpuTimes();
        var cpu = _cpuBefore is { } before ? SystemProbe.CpuBusy(before, now) : 0;
        _cpuBefore = now;
        if (DateTime.Now - _gpuAt > TimeSpan.FromSeconds(15))
        {
            _gpuAt = DateTime.Now;
            _gpu = await SystemProbe.GpuAsync(CancellationToken.None).ConfigureAwait(true);
        }

        var memory = SystemProbe.MemoryLoad();
        var totalGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0;
        var rows = new List<SystemRow>
        {
            new("İşlemci", $"%{cpu:0}", cpu / 100),
            new("Bellek", string.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), $"%{memory} · {totalGb * memory / 100:0.0}/{totalGb:0} GB"), memory / 100.0)
        };
        if (_gpu is { } gpu)
        {
            rows.Add(new("Ekran kartı", $"%{gpu.Utilization} · {gpu.Temperature}°C", gpu.Utilization / 100.0));
        }

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(paths.Home)!);
            var used = 1 - ((double)drive.AvailableFreeSpace / drive.TotalSize);
            rows.Add(new($"Disk {drive.Name.TrimEnd('\\')}", string.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), $"{drive.AvailableFreeSpace / 1073741824.0:0} GB boş"), used));
        }
        catch (IOException)
        {
            // Disk okunamadıysa satır gösterilmez.
        }

        using var self = Process.GetCurrentProcess();
        panel.ShowSystem(rows, $"Friday {self.WorkingSet64 / 1048576} MB bellek kullanıyor.");
    }

    /// <summary>Her rutinin kısayolu: basınca rutin, adı söylenmiş gibi çalışır; Friday arka plandayken de.</summary>
    private void RegisterRoutineKeys()
    {
        foreach (var key in _routineKeys)
        {
            key.Dispose();
        }

        _routineKeys.Clear();
        if (_window is null || _settings is null || _assistant is null)
        {
            return;
        }

        // Kısaltma kısayolu: imleçten önceki sözcük bir kısaltmanın adıysa yerine metni yazılır.
        if (_tools is { } tools && _settings.SnippetHotkey.Length > 0 && _settings.Snippets.Count > 0)
        {
            var expand = new GlobalHotkey(_window, () => _ = tools.ExpandAtCursorAsync(), 0x46FF);
            try
            {
                if (expand.Register(HotkeyGesture.Parse(_settings.SnippetHotkey)))
                {
                    _routineKeys.Add(expand);
                }
                else
                {
                    expand.Dispose();
                }
            }
            catch (FormatException)
            {
                expand.Dispose();
            }
        }

        var id = 0x4700;
        foreach (var (name, text) in _settings.RoutineHotkeys)
        {
            if (!_settings.Routines.ContainsKey(name))
            {
                continue;
            }

            var routine = name;
            var assistant = _assistant;
            var hotkey = new GlobalHotkey(_window, () => assistant.SubmitText(routine), id++);
            try
            {
                if (hotkey.Register(HotkeyGesture.Parse(text)))
                {
                    _routineKeys.Add(hotkey);
                    continue;
                }

                FridayLog.Warn($"Rutin kısayolu alınamadı (başka bir uygulamada olabilir): {text} → {name}");
            }
            catch (FormatException)
            {
                FridayLog.Warn($"Rutin kısayolu anlaşılamadı: {text}");
            }

            hotkey.Dispose();
        }
    }
    private HudWindow? _hud;
    private MainWindow? _window;
    private CancellationTokenSource? _startup;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private FridayPaths? _paths;
    private DispatcherTimer? _weatherClock;
    private DispatcherTimer? _rhythmClock;
    private QuickAskWindow? _quickAsk;
    private bool _startHidden;

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
        // Yeniden başlatmada eski kopya kapanana kadar beklenir (en fazla 15 sn).
        for (var attempt = 0; !first && e.Args.Contains("--restart", StringComparer.OrdinalIgnoreCase) && attempt < 50; attempt++)
        {
            _showSignal.Dispose();
            Thread.Sleep(300);
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Friday.Assistant.Show", out first);
        }

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

            _startHidden = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
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

        // Ayarlardan "Yedekten geri yükle" seçildiyse yedek, veriler okunmadan önce yerine konur.
        if (DataMaintenance.ApplyPendingRestore(paths.Data))
        {
            FridayLog.Info("Yedek geri yüklendi");
        }

        var store = new JsonSettingsStore(paths);
        var settings = store.Load();
        if (!settings.GpuRendering)
        {
            // Pencereler oluşturulmadan önce: bütün arayüz işlemciyle çizilir (bkz. FridaySettings.GpuRendering).
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }
        var memory = new MemoryStore(Path.Combine(paths.Data, "memory.json"));
        var ledger = new PrivacyLedger(Path.Combine(paths.Data, "ledger"));
        var chats = new ChatStore(Path.Combine(paths.Data, "chats"));
        if (chats.ImportLegacy(Path.Combine(paths.Data, "history")) is > 0 and var imported)
        {
            FridayLog.Info($"Eski günlük geçmişten {imported} sohbet oluşturuldu");
        }

        var reminders = new ReminderStore(Path.Combine(paths.Data, "reminders.json"));
        var notes = new NoteStore(Path.Combine(paths.Data, "notes.md"));
        var todos = new TodoStore(Path.Combine(paths.Data, "todos.json"));
        var habits = new HabitTracker(Path.Combine(paths.Data, "habits.json"));
        var habitBook = new HabitBook(Path.Combine(paths.Data, "habit-book.json"));
        var expenses = new ExpenseBook(Path.Combine(paths.Data, "expenses.json"));
        var vocabulary = new VocabularyDeck(Path.Combine(paths.Data, "vocabulary.json"));

        // Yerel model yanıtları uzun sürebilir; zaman aşımını istekler kendi belirler.
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var ollama = new OllamaServer(paths, _http);
        var chat = new OllamaChatClient(_http, ollama);
        var rhythm = new RhythmCoach(Path.Combine(paths.Data, "rhythm"), ledger);
        var tools = new AssistantTools(_http, chat, memory, ledger, chats, reminders, notes) { SaveSettings = store.Save, Todos = todos, Habits = habits, HabitBook = habitBook, Expenses = expenses, Vocabulary = vocabulary, Nudges = new NudgePolicy(Path.Combine(paths.Data, "nudges.json")) };
        _tools = tools;
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
            chats,
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
            ScanDocuments = () => _ = assistant.ScanDocumentsAsync(),
            Persist = store.Save,
            EndQuiet = () => assistant.SetQuiet(null),
            Save = changed =>
            {
                store.Save(changed);
                assistant.ApplySettings();
                RegisterRoutineKeys();
                ApplyLook(changed);
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
            },
            Chats = chats,
            OpenChat = assistant.OpenChat,
            DeleteChat = id =>
            {
                chats.Delete(id);
                ledger.Record(DataFlow.Local, "Sohbet", "Bir sohbet silindi");
            },
            ExportChat = id => ExportChat(paths, chats, id, settings.AssistantName),
            ApplyHotkey = text => ApplyHotkey(text, quickAsk: false),
            ApplyQuickAskHotkey = text => ApplyHotkey(text, quickAsk: true),
            SetStartWithWindows = StartupRegistration.Set,
            ClearData = kind => ClearData(kind, chats, memory, reminders, ledger),
            OpenLogs = () => Open(paths.Logs),
            CheckForUpdate = () => UpdateCheck.CheckAsync(_http!, typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0), ledger, CancellationToken.None),
            PiperVoices = PiperVoices(paths),
            SmartModelInstalled = SmartModelInstalled(paths, settings),
            BackupData = () => BackupData(paths, ledger),
            TestCalendar = url => tools.Calendar.TestAsync(url, CancellationToken.None),
            AddTodo = text => todos.Add(text, DateTime.Now),
            ToggleTodo = (id, done) =>
            {
                if (done)
                {
                    todos.Complete(id, DateTime.Now);
                }
                else
                {
                    todos.Reopen(id);
                }
            },
            RemoveTodo = id => todos.Remove(id),
            AddHabit = text => habitBook.Add(text, DateOnly.FromDateTime(DateTime.Now)),
            ToggleHabit = (name, done) => habitBook.Mark(name, DateOnly.FromDateTime(DateTime.Now), done),
            RemoveHabit = name => habitBook.Remove(name),
            RemoveExpense = id => expenses.Remove(id),
            RestoreData = () => RestoreData(paths),
            Restart = RestartFriday,
            ScanReclaimable = () => Task.Run(() => DataMaintenance.Reclaimable(paths, settings)),
            Reclaim = items => Task.Run(() =>
            {
                var freed = DataMaintenance.Reclaim(items);
                ledger.Record(DataFlow.Local, "Yer açma", $"{DataMaintenance.Describe(freed)} silindi: {string.Join(", ", items.Select(item => item.Label))}");
                return freed;
            })
        });
        var hud = new HudViewModel();
        // Son sohbet az önce konuşulduysa oradan devam edilir; değilse yeni, boş bir sohbetle açılır.
        if (chats.List().MaxBy(item => item.Updated) is { } last
            && DateTimeOffset.Now - last.Updated < TimeSpan.FromMinutes(Math.Max(1, settings.ConversationMemoryMinutes)))
        {
            chats.Open(last.Id);
        }

        panel.ShowChat(chats.CurrentTurns);
        panel.ShowMemories(memory.Items);
        panel.ShowLedger(ledger.TodayEntries);

        void Ui(Action action) => Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        void ShowRhythm()
        {
            panel.ShowRhythm(rhythm.Today, rhythm.Snapshot());
            panel.ShowGoal(rhythm.Today, rhythm.History(60));
            panel.ShowWeek(rhythm.Week());
        }

        rhythm.Changed += (_, _) => Ui(ShowRhythm);
        ShowRhythm();
        chats.Changed += (_, _) => Ui(panel.RefreshChats);
        tools.SettingsChanged += (_, _) => Ui(() =>
        {
            panel.ReloadRoutines();
            panel.ReloadSnippets();
            panel.ShowGoal(rhythm.Today, rhythm.History(60));
            RegisterRoutineKeys();
        });
        chats.CurrentChanged += (_, _) => Ui(() => panel.ShowChat(chats.CurrentTurns));

        // Ritim halkasının saniyelik sayacı yalnızca panel açık ve Ritim ya da Bugün sayfası görünürken çalışır.
        _rhythmClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _rhythmClock.Tick += (_, _) =>
        {
            if (_window is { IsVisible: true, WindowState: not WindowState.Minimized } && (panel.IsRhythm || panel.IsToday))
            {
                panel.TickRhythm(rhythm.Snapshot());
                // Odak sürerken Ritim sayfasında, o odakta hangi uygulamada ne kadar kalındığı 5 sn'de bir tazelenir.
                if (panel.IsRhythm && _screenTimeTicks % 5 == 0)
                {
                    var snapshot = rhythm.Snapshot();
                    panel.FocusApps = snapshot.FocusStart is { } focusStart && snapshot.FocusEnd > DateTime.Now && tools.Activity?.Since(focusStart) is { Count: > 0 } apps
                        ? "Bu odakta: " + string.Join(" · ", apps.Take(3).Select(item => $"{item.App} {ScreenTimeLog.Compact(item.Seconds)}"))
                        : "";
                }

                // Rahatsız etme süresi dolduysa rozet kalkar.
                if (panel.QuietBadge.Length > 0 && assistant.QuietUntil is null && assistant.Meeting is null)
                {
                    panel.QuietBadge = "";
                }

                // Ekran süresi kartı yarım dakikada bir tazelenir (sayaç 15 sn'de bir sayar).
                if (panel.IsToday && ++_screenTimeTicks % 30 == 1 && tools.Activity is { } activity)
                {
                    panel.ShowScreenTime(activity.Day(DateTime.Now));
                }

                // Sistem kartı yalnızca görünürken ve 3 sn'de bir ölçülür; ekran kartı 15 sn'de bir.
                if (panel.IsToday && _screenTimeTicks % 3 == 1 && panel.ShowsHomeCard("system"))
                {
                    _ = RefreshSystemAsync(panel, paths);
                }
            }
        };
        _rhythmClock.Start();

        // "Bugün" ekranı: yaklaşanlar, notlar ve saatte bir yenilenen hava.
        void ShowToday() => panel.ShowToday(reminders.Upcoming, memory.Items, tools.Calendar.Cached(DateTime.Now, DateTime.Now.AddDays(7)));
        tools.Calendar.Changed += (_, _) => Ui(ShowToday);
        reminders.Changed += (_, _) => Ui(ShowToday);
        memory.Changed += (_, _) => Ui(ShowToday);
        notes.Changed += (_, _) => Ui(() => panel.ShowNotes(notes.All));
        todos.Changed += (_, _) => Ui(() => panel.ShowTodos(todos.Visible(DateTime.Now)));
        panel.ShowTodos(todos.Visible(DateTime.Now));
        habitBook.Changed += (_, _) => Ui(() => panel.ShowHabits(habitBook.Habits, DateOnly.FromDateTime(DateTime.Now)));
        panel.ShowHabits(habitBook.Habits, DateOnly.FromDateTime(DateTime.Now));
        expenses.Changed += (_, _) => Ui(() => panel.ShowSpending(expenses, DateTime.Now));
        panel.ShowSpending(expenses, DateTime.Now);

        // Günün planı kartı: işler, alışkanlıklar, hatırlatmalar ya da takvim değişince ve 5 dakikada bir yeniden kurulur.
        void ShowPlan()
        {
            var now = DateTime.Now;
            var fixedItems = tools.Calendar.Cached(now, now.Date.AddDays(1)).Where(item => !item.AllDay).Select(item => new FixedItem(item.Start, item.End, item.Title))
                .Concat(reminders.Upcoming.Where(item => item.Trigger is null && item.Due.Date == now.Date && item.Due > now)
                    .Select(item => new FixedItem(item.Due, item.Due.AddMinutes(15), item.Text.Length > 0 ? TurkishGrammar.Capitalize(item.Text) : "Hatırlatma")));
            var plan = DayPlanner.Plan(now, fixedItems, todos.Open.Select(item => item.Text), habitBook.Pending(DateOnly.FromDateTime(now)).Select(habit => habit.Name),
                now.Hour >= 21 ? Math.Min(24, now.Hour + 2) : 22);
            panel.ShowPlan(plan, now);
        }

        // Selam kartındaki tek satırlık özet.
        void ShowSummary()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var (from, to, _) = ExpenseBook.Period("ay", DateTime.Now);
            panel.ShowTodaySummary(todos.Open, habitBook.Pending(today), vocabulary.Started ? vocabulary.Stats(today) : null, expenses.Summary(from, to).Total, today);
        }

        todos.Changed += (_, _) => Ui(ShowSummary);
        habitBook.Changed += (_, _) => Ui(ShowSummary);
        expenses.Changed += (_, _) => Ui(ShowSummary);
        vocabulary.Changed += (_, _) => Ui(ShowSummary);
        ShowSummary();
        todos.Changed += (_, _) => Ui(ShowPlan);
        habitBook.Changed += (_, _) => Ui(ShowPlan);
        reminders.Changed += (_, _) => Ui(ShowPlan);
        tools.Calendar.Changed += (_, _) => Ui(ShowPlan);
        ShowPlan();
        var planClock = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        planClock.Tick += (_, _) =>
        {
            ShowPlan();
            ShowSummary();
        };
        planClock.Start();
        vocabulary.Changed += (_, _) => Ui(() => panel.ShowVocabulary(vocabulary.Stats(DateOnly.FromDateTime(DateTime.Now)), vocabulary.Started));
        panel.ShowVocabulary(vocabulary.Stats(DateOnly.FromDateTime(DateTime.Now)), vocabulary.Started);
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
        assistant.DocumentsChanged += (_, status) => Ui(() => panel.DocumentStatus = status);
        assistant.LatencyMeasured += (_, latency) => Ui(() => panel.LastLatency = latency);
        void ShowQuiet() => panel.QuietBadge = assistant.Meeting is { } call
            ? $"Görüşmede · {call}"
            : assistant.QuietUntil is { } until ? $"Rahatsız etme · {until:HH:mm}" : "";
        assistant.QuietChanged += (_, _) => Ui(ShowQuiet);
        assistant.MeetingChanged += (_, _) => Ui(ShowQuiet);
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
        assistant.CardShown += (_, card) => Ui(() => hud.Card = card);
        assistant.UserSaid += (_, text) => Ui(() =>
        {
            hud.Card = null;
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
        _quickAsk = new QuickAskWindow(() => QuickAskItems(settings, chats), assistant.SubmitText);
        HotkeyGesture? quickGesture = null;
        if (settings.QuickAskHotkey.Length > 0)
        {
            try
            {
                quickGesture = HotkeyGesture.Parse(settings.QuickAskHotkey);
            }
            catch (FormatException exception)
            {
                FridayLog.Warn($"Hızlı komut kısayolu okunamadı: {exception.Message}");
            }
        }

        _settings = settings;
        _window = new MainWindow(panel, candidates, assistant.OnHotkey, quickGesture, () => _quickAsk.Toggle())
        {
            BoundsFile = Path.Combine(paths.Data, "window.txt")
        };
        MainWindow = _window;
        _hud = new HudWindow(hud);
        _tray = new TrayIcon(settings.AssistantName, _window.ShowFromTray, ExitFriday);
        _tray.AddSubmenu("Rutinler", () => settings.Routines.Keys, name => assistant.SubmitText(name));
        _tray.AddItem(
            () => assistant.QuietUntil is { } until ? $"Rahatsız etmeyi kapat ({until:HH:mm})" : "Rahatsız etme (1 saat)",
            () => assistant.SetQuiet(assistant.QuietUntil is null ? TimeSpan.FromHours(1) : null));
        ApplyLook(settings);
        if (_startHidden)
        {
            // Windows açılışı: pencere gösterilmez ama kısayollar için pencere tutamacı oluşturulur.
            new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
        }
        else
        {
            _window.Show();
        }

        RegisterRoutineKeys();

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

    /// <summary>Kendini "--restart" ile yeniden açar; yeni kopya bu kopya kapanana kadar bekler.</summary>
    private void RestartFriday()
    {
        if (Environment.ProcessPath is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();
        }

        Shutdown();
    }

    /// <summary>Kişisel verileri seçilen zip dosyasına yedekler (varsayılan klasör D:\Friday-yedekler, Friday klasörünün dışında).</summary>
    private string? BackupData(FridayPaths paths, PrivacyLedger ledger)
    {
        var folder = Path.Combine(Path.GetPathRoot(paths.Home) ?? "D:\\", "Friday-yedekler");
        Directory.CreateDirectory(folder);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Friday yedeği",
            Filter = "Zip dosyası|*.zip",
            FileName = $"friday-yedek-{DateTime.Now:yyyy-MM-dd}.zip",
            InitialDirectory = folder
        };
        if (dialog.ShowDialog(_window) != true)
        {
            return null;
        }

        try
        {
            var count = DataMaintenance.CreateBackup(paths.Data, dialog.FileName);
            ledger.Record(DataFlow.Local, "Yedek", $"{count} dosya {dialog.FileName} dosyasına yedeklendi");
            return $"{count} dosya yedeklendi: {dialog.FileName} ({DataMaintenance.Describe(new FileInfo(dialog.FileName).Length)}).";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FridayLog.Error("Yedeklenemedi", exception);
            return $"Yedeklenemedi: {exception.Message}";
        }
    }

    /// <summary>Seçilen yedeği doğrular ve Friday yeniden açılınca uygulanmak üzere bekletir.</summary>
    private string? RestoreData(FridayPaths paths)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Friday yedeğini seç", Filter = "Zip dosyası|*.zip" };
        if (dialog.ShowDialog(_window) != true)
        {
            return null;
        }

        try
        {
            return DataMaintenance.StageRestore(dialog.FileName, paths.Data)
                ? "Yedek hazır; Friday yeniden başlayınca geri yüklenecek. Şu anki verilerin data\\before-restore-… klasörüne taşınır."
                : "Bu dosya bir Friday yedeği değil.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            FridayLog.Error("Yedek okunamadı", exception);
            return $"Yedek okunamadı: {exception.Message}";
        }
    }

    /// <summary>Ayarlardan seçilen kısayolu canlı uygular; sorun yoksa null.</summary>
    private string? ApplyHotkey(string text, bool quickAsk)
    {
        if (_window is null)
        {
            return "Pencere hazır değil";
        }

        if (quickAsk && text.Length == 0)
        {
            _window.ChangeQuickAsk(null);
            return null;
        }

        HotkeyGesture gesture;
        try
        {
            gesture = HotkeyGesture.Parse(text);
        }
        catch (FormatException exception)
        {
            return exception.Message;
        }

        var taken = quickAsk ? !_window.ChangeQuickAsk(gesture) : !_window.ChangeHotkey(gesture);
        return taken ? $"{text} başka bir uygulamada; başka bir kısayol seç" : null;
    }

    /// <summary>Hızlı komut kutusunun önerileri: rutinler, son sorular ve hazır komutlar.</summary>
    private static List<QuickAskItem> QuickAskItems(FridaySettings settings, ChatStore chats)
    {
        var items = new List<QuickAskItem>();
        foreach (var name in settings.Routines.Keys.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            items.Add(new QuickAskItem("\uE945", $"{TurkishGrammar.Capitalize(name)} rutinini çalıştır", "Rutin"));
        }

        var recent = chats.Read(chats.Today).Concat(chats.Read(chats.Today.AddDays(-1)))
            .Select(turn => turn.User.Trim())
            .Where(text => text.Length is > 3 and < 80)
            .Reverse()
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(6);
        items.AddRange(recent.Select(text => new QuickAskItem("\uE81C", text, "Son sorulan")));
        string[] ideas =
        [
            "Durum raporu", "Yeni sohbet", "Saat kaç?", "Yarın hava nasıl?", "Dolar kaç lira?", "Hatırlatmalarım neler?", "Not al: ",
            "İndirme bitince haber ver", "Render bitince haber ver", "Bilgisayarı kilitle", "Karanlık moda geç", "Ekran görüntüsü al",
            "30 dakika sonra bilgisayarı kapat", "Kapatmayı iptal et", "Kronometreyi başlat", "Yazı tura at", "25 dakika odaklanacağım",
            "Mola ver", "Bugün ne kadar çalıştım?", "Panodaki metni özetle", "Ekranımda ne var?", "Şu an ne çalıyor?", "Sesi yüzde 30 yap",
        ];
        items.AddRange(ideas.Select(text => new QuickAskItem("\uE945", text, "Komut")));
        return items;
    }

    /// <summary>Akıllı mod modelinin (ör. gemma4:12b) Ollama model klasöründe kurulu olup olmadığı.</summary>
    private static bool SmartModelInstalled(FridayPaths paths, FridaySettings settings)
    {
        var parts = settings.SmartModel.Split(':', 2);
        var name = parts[0].Contains('/', StringComparison.Ordinal) ? parts[0] : $"library/{parts[0]}";
        return File.Exists(Path.Combine(paths.OllamaModels, "manifests", "registry.ollama.ai", name.Replace('/', '\\'), parts.Length > 1 ? parts[1] : "latest"));
    }

    private static IReadOnlyList<VoiceOption> PiperVoices(FridayPaths paths)
    {
        var folder = Path.Combine(paths.Models, "piper");
        return Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder, "*.onnx").Order(StringComparer.OrdinalIgnoreCase).Select(PanelViewModel.PiperVoiceOption)]
            : [];
    }

    /// <summary>Ayarlar › Verilerin: sohbetleri, hafızayı ya da hatırlatmaları siler; silinen sayıyı döndürür.</summary>
    private static int ClearData(string kind, ChatStore chats, MemoryStore memory, ReminderStore reminders, PrivacyLedger ledger)
    {
        int count;
        switch (kind)
        {
            case "chats":
                count = chats.DeleteAll();
                ledger.Record(DataFlow.Local, "Sohbet", $"Bütün sohbetler silindi ({count})");
                return count;
            case "memory":
                var items = memory.Items.ToList();
                foreach (var item in items)
                {
                    memory.Remove(item.Id);
                }

                ledger.Record(DataFlow.Local, "Hafıza", $"Hafıza silindi ({items.Count} bilgi)");
                return items.Count;
            case "reminders":
                count = reminders.Upcoming.Count(item => reminders.Remove(item.Id));
                ledger.Record(DataFlow.Local, "Hatırlatma", $"Hatırlatmalar silindi ({count})");
                return count;
            default:
                return 0;
        }
    }

    /// <summary>Sohbeti D:\Friday\data\exports altına Markdown olarak yazar ve klasöründe gösterir.</summary>
    private static string? ExportChat(FridayPaths paths, ChatStore chats, string id, string assistantName)
    {
        try
        {
            var folder = Path.Combine(paths.Data, "exports");
            Directory.CreateDirectory(folder);
            var title = chats.Find(id)?.Title ?? "sohbet";
            var safe = string.Concat(title.Select(letter => Path.GetInvalidFileNameChars().Contains(letter) ? '-' : letter)).Trim();
            var file = Path.Combine(folder, $"sohbet-{DateTime.Now:yyyy-MM-dd-HHmm}-{(safe.Length > 40 ? safe[..40] : safe)}.md");
            File.WriteAllText(file, chats.ToMarkdown(id, assistantName));
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true })?.Dispose();
            return file;
        }
        catch (IOException exception)
        {
            FridayLog.Error("Sohbet dışa aktarılamadı", exception);
            return null;
        }
    }

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
        _rhythmClock?.Stop();
        _quickAsk?.Close();
        _quickAsk = null;
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
