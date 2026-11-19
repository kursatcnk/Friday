using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Durum raporu, bilgisayar kontrolü, nöbetçiler, şans, kronometre ve özel komutlar.</summary>
public sealed partial class AssistantTools
{
    private readonly Random _random = new();
    private DateTime? _stopwatch;
    private WatchService? _watches;

    /// <summary>Ekran görüntülerinin kaydedileceği klasör (D:\Friday\data\captures).</summary>
    public string CaptureFolder { get; set; } = "";

    /// <summary>Özel komut sesle eklenince ayarları diske yazar.</summary>
    public Action<FridaySettings>? SaveSettings { get; set; }

    /// <summary>Ayarlar sesle değişti (özel komut eklendi); panel rutin listesini yeniler.</summary>
    public event EventHandler? SettingsChanged;

    public WatchService Watches
    {
        get
        {
            if (_watches is null)
            {
                _watches = new WatchService(ledger);
                _watches.Fired += (_, message) => TimerElapsed?.Invoke(this, message);
            }

            return _watches;
        }
    }

    /// <summary>
    /// "Durum raporu": işlemci, bellek, ekran kartı, disk, sıradaki hatırlatma, günün ritmi, nöbetler ve kurulu kapanma;
    /// bir şey dikkat istiyorsa onu söyler, yoksa "her şey yolunda".
    /// </summary>
    public async Task<QuickReply> StatusReportAsync(CancellationToken cancellationToken)
    {
        var cpuTask = SystemProbe.CpuBusyAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
        var gpuTask = SystemProbe.GpuAsync(cancellationToken);
        var cpu = await cpuTask.ConfigureAwait(false);
        var gpu = await gpuTask.ConfigureAwait(false);
        var memory = SystemProbe.MemoryLoad();
        var now = DateTime.Now;
        ShowStatusCard(cpu, memory, gpu);
        var warnings = new List<string>();
        var parts = new List<string>
        {
            $"İşlemci yüzde {Math.Round(cpu)}, bellek yüzde {memory}" + (gpu is null ? "." : $", ekran kartı {gpu.Temperature} derece.")
        };

        if (cpu > 85)
        {
            warnings.Add("işlemci çok yüklü");
        }

        if (memory > 90)
        {
            warnings.Add("bellek neredeyse dolu");
        }

        if (gpu is { Temperature: > 83 })
        {
            warnings.Add("ekran kartı sıcak");
        }

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive is { IsReady: true, DriveType: DriveType.Fixed }))
        {
            var free = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
            if (free < 15 || drive.AvailableFreeSpace < drive.TotalSize / 10)
            {
                warnings.Add($"{drive.Name.TrimEnd('\\', ':')} diskinde yalnızca {Math.Round(free)} GB boş kaldı");
            }
        }

        if (reminders.Upcoming is [var next, ..] && next.Due.Date <= now.Date.AddDays(1))
        {
            parts.Add($"Sıradaki hatırlatma {TurkishTime.Describe(next.Due, now)}: {(next.Text.Length > 0 ? next.Text : "zamanlayıcı")}.");
        }

        if (Rhythm is { Enabled: true } coach && coach.Today.ActiveMinutes >= 1)
        {
            var stretch = coach.CurrentStretch;
            parts.Add($"Bugün {RhythmCoachLines.Span(TimeSpan.FromMinutes(coach.Today.ActiveMinutes))} bilgisayar başındasın" +
                      (stretch >= TimeSpan.FromMinutes(20) ? $", şu an {RhythmCoachLines.Span(stretch)} aralıksız." : "."));
        }

        if (_watches?.Active is { Count: > 0 } watching)
        {
            parts.Add($"Nöbette: {TurkishGrammar.Join([.. watching.Select(watch => watch.Describe())])} bekliyorum.");
        }

        if (SystemControl.ShutdownAt is { } shutdown && shutdown > now)
        {
            parts.Add($"Bilgisayar {shutdown.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.TimeSuffix(shutdown)} kapanacak.");
        }

        parts.Add(warnings.Count == 0 ? "Her şey yolunda." : $"Dikkat: {TurkishGrammar.Join(warnings)}.");
        var address = Settings.AddressAs.Trim();
        ledger.Record(DataFlow.Local, "Durum raporu", "İşlemci, bellek, ekran kartı ve disk ölçüldü");
        return new QuickReply((address.Length > 0 ? $"Durum raporu, {address}. " : "Durum raporu. ") + string.Join(" ", parts));
    }

    private QuickReply SystemCommand(string action, string minutesText)
    {
        var text = Simplify(action) switch
        {
            "kilitle" => SystemControl.Lock(),
            "uyku" => SystemControl.Sleep(),
            "ekrankapat" => SystemControl.MonitorOff(),
            "masaustu" => SystemControl.ShowDesktop(),
            "temakoyu" => SystemControl.SetTheme(dark: true),
            "temaacik" => SystemControl.SetTheme(dark: false),
            "ekrangoruntusu" => Screenshot(),
            "kapat" => SystemControl.ScheduleShutdown(
                int.TryParse(minutesText.Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ? minutes : 1),
            "kapatiptal" => SystemControl.CancelShutdown(),
            _ => $"Bilinmeyen bilgisayar işlemi: {action}"
        };
        ledger.Record(DataFlow.Local, "Bilgisayar", text);
        return new QuickReply(text);
    }

    private string Screenshot()
    {
        var folder = CaptureFolder.Length > 0 ? CaptureFolder : Path.Combine(AppContext.BaseDirectory, "captures");
        var file = SystemControl.Screenshot(folder);
        return $"Ekran görüntüsünü aldım: {Path.GetFileName(file)}. Friday'in veri klasöründeki captures klasöründe.";
    }

    private async Task<QuickReply> WatchCommandAsync(string action, string degrees, CancellationToken cancellationToken)
    {
        var text = Simplify(action) switch
        {
            "indirme" => await Watches.StartAsync(new WatchRequest(WatchKind.Download), cancellationToken).ConfigureAwait(false),
            "bosta" => await Watches.StartAsync(new WatchRequest(WatchKind.Idle), cancellationToken).ConfigureAwait(false),
            "internet" => await Watches.StartAsync(new WatchRequest(WatchKind.Internet), cancellationToken).ConfigureAwait(false),
            "gpu" => await Watches.StartAsync(new WatchRequest(WatchKind.GpuTemperature,
                double.TryParse(degrees, NumberStyles.Float, CultureInfo.InvariantCulture, out var limit) ? Math.Clamp(limit, 50, 100) : 85),
                cancellationToken).ConfigureAwait(false),
            "liste" => WatchList(),
            "iptal" => Watches.CancelAll(),
            _ => $"Bilinmeyen nöbet: {action}"
        };
        return new QuickReply(text);
    }

    private string WatchList()
    {
        ShowListCard("Nöbette", "\uE7B3", Watches.Active.Select(watch => new CardRow(TurkishGrammar.Capitalize(watch.Describe()), "bekliyor")));
        return Watches.List();
    }

    private QuickReply LuckCommand(string action, string count, string from, string to)
    {
        static int Number(string text, int fallback) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        return new QuickReply(Simplify(action) switch
        {
            "yazitura" => Chance.CoinFlip(_random),
            "zar" => Chance.Dice(_random, Number(count, 1)),
            _ => Chance.Number(_random, Number(from, 1), Number(to, 100))
        });
    }

    private QuickReply StopwatchCommand(string action)
    {
        var now = DateTime.Now;
        switch (Simplify(action))
        {
            case "baslat":
                _stopwatch = now;
                return new QuickReply("Kronometre başladı.");
            case "durdur" when _stopwatch is { } started:
                _stopwatch = null;
                ShowCard(new AnswerCard("Kronometre", "\uE916") { Big = RhythmText.Clock(now - started), BigCaption = "durdu" });
                return new QuickReply($"Kronometreyi durdurdum: {SpokenDuration.Of(now - started)}.");
            case "durum" when _stopwatch is { } started:
                ShowCard(new AnswerCard("Kronometre", "\uE916") { Big = RhythmText.Clock(now - started), BigCaption = "çalışıyor" });
                return new QuickReply($"Kronometre {SpokenDuration.Of(now - started)} gösteriyor.");
            default:
                return new QuickReply("Çalışan bir kronometre yok. \"Kronometreyi başlat\" diyebilirsin.");
        }
    }

    /// <summary>"Baha dediğimde 'x' de": özel komutu ayarlara ekler; aynı ifade varsa üzerine yazar.</summary>
    private QuickReply AddCustomCommand(string phrase, string step)
    {
        RoutineStep parsed;
        try
        {
            parsed = RoutineStep.Parse(step);
        }
        catch (FormatException)
        {
            return new QuickReply("Bu komutu anlayamadım; \"X dediğimde Y de\" ya da \"X dediğimde Spotify'ı aç\" gibi söyle.");
        }

        Settings.Triggers[phrase] = [parsed.Format()];
        SaveSettings?.Invoke(Settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        ledger.Record(DataFlow.Local, "Özel komut", $"\"{phrase}\" eklendi");
        // "Spotify uygulamasını aç" → "Spotify uygulamasını açacağım".
        var what = parsed.Action == RoutineAction.Say ? $"\"{parsed.Value}\" diyeceğim" : parsed.Describe() + "acağım";
        return new QuickReply($"Tamam, cümlende \"{phrase}\" geçince {what}. Rutinler sayfasından değiştirebilir ya da silebilirsin.");
    }

    /// <summary>Özel komutun adımlarını çalıştırır.</summary>
    private async Task<QuickReply?> TriggerReplyAsync(string name, CancellationToken cancellationToken)
    {
        var match = Settings.Triggers.FirstOrDefault(trigger => string.Equals(trigger.Key, name, StringComparison.OrdinalIgnoreCase));
        if (match.Key is null)
        {
            return null;
        }

        var reply = await RunStepsAsync(match.Value, cancellationToken).ConfigureAwait(false);
        ledger.Record(DataFlow.Local, "Özel komut", $"\"{match.Key}\" çalıştı");
        return reply is null ? null : reply.Text.Length > 0 || reply.Script is not null ? reply : reply with { Text = "Tamam." };
    }

    /// <summary>
    /// Rutin ya da özel komut adımlarını sırayla çalıştırır; söylenecekler aynı sırayla birleşir. Günün özeti adımı varsa
    /// null döner (özeti dil modeli kurar). Hatalı adım atlanır.
    /// </summary>
    private async Task<QuickReply?> RunStepsAsync(IEnumerable<string> texts, CancellationToken cancellationToken)
    {
        var steps = new List<RoutineStep>();
        foreach (var text in texts)
        {
            try
            {
                steps.Add(RoutineStep.Parse(text));
            }
            catch (FormatException)
            {
                FridayLog.Warn($"Geçersiz adım atlandı: {text}");
            }
        }

        if (steps.Any(step => step.Action == RoutineAction.Briefing))
        {
            return null;
        }

        var said = new List<string>();
        IReadOnlyList<CoachStep>? script = null;
        foreach (var step in steps)
        {
            switch (step.Action)
            {
                case RoutineAction.App:
                    OpenApp(step.Value);
                    break;
                case RoutineAction.Website:
                    OpenWebsite(step.Value);
                    break;
                case RoutineAction.Search:
                    Search(step.Value);
                    break;
                case RoutineAction.Media:
                    await MediaAsync(step.Value).ConfigureAwait(false);
                    break;
                case RoutineAction.LinguaFlow:
                    await LinguaFlowAsync("baslat", cancellationToken).ConfigureAwait(false);
                    break;
                case RoutineAction.Rhythm:
                    var parts = step.Value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var reply = RhythmCommand(parts[0], parts.Length > 1 ? parts[1] : "");
                    script ??= reply.Script;
                    if (reply.Script is null)
                    {
                        said.Add(reply.Text);
                    }

                    break;
                case RoutineAction.Say:
                    said.Add(step.Value);
                    break;
                case RoutineAction.Volume:
                    SystemVolume.Apply(Math.Clamp(step.Number, 0, 100));
                    break;
                case RoutineAction.Wait:
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(step.Number, 0, 60)), cancellationToken).ConfigureAwait(false);
                    break;
                case RoutineAction.Remind:
                    var remind = step.Value.Split('|', 2, StringSplitOptions.TrimEntries);
                    said.Add(ReminderCommand("kur", remind[0], remind[1]));
                    break;
                case RoutineAction.Ambient:
                    var ambient = step.Value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    _ = AmbientCommand(ambient[0] == "kapat" ? "kapat" : "ac", ambient[0], ambient.Length > 1 ? ambient[1] : "");
                    break;
                case RoutineAction.Close:
                    _ = CloseAppCommand(step.Value);
                    break;
                case RoutineAction.Open:
                    _ = OpenPath(step.Value);
                    break;
                case RoutineAction.Window:
                    _ = WindowCommand(step.Value);
                    break;
                case RoutineAction.Quiet:
                    _ = QuietControl?.Invoke(TimeSpan.FromMinutes(Math.Clamp(step.Number, 5, 24 * 60)));
                    break;
                case RoutineAction.Pomodoro:
                    said.Add(PomodoroControl?.Invoke(Math.Clamp(step.Number, 1, 8)) ?? "");
                    break;
                case RoutineAction.Workspace:
                    var workspace = await WorkspaceCommandAsync("kur", step.Value, cancellationToken).ConfigureAwait(false);
                    if (workspace.Text.Contains("yok", StringComparison.Ordinal))
                    {
                        said.Add(workspace.Text);
                    }

                    break;
            }
        }

        _ = TakeScript();
        return new QuickReply(string.Join(" ", said), Script: script);
    }
}
