using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Arka planda araştırma raporu, takvim, İndirilenler'i düzenleme, gün sonu özeti ve evet/hayır onayları.</summary>
public sealed partial class AssistantTools
{
    private static readonly TimeSpan ConfirmationLife = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions UndoJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly CancellationTokenSource _background = new();
    private Task _research = Task.CompletedTask;
    private string _researchTopic = "";
    private string? _lastReport;
    private CalendarService? _calendar;
    private IReadOnlyList<FileMove>? _pendingSort;
    private DateTime _pendingAt;

    /// <summary>Raporların yazılacağı klasör (D:\Friday\data\reports).</summary>
    public string ReportFolder { get; set; } = "";

    /// <summary>İndirilenler düzenlemesinin geri alma kaydı (D:\Friday\data\downloads-undo.json).</summary>
    public string UndoFile { get; set; } = "";

    /// <summary>Dil modelinin hazır olduğu an (ilk açılışta arkada yüklenir); arka plan raporu bunu bekler.</summary>
    public Task BrainReady { get; set; } = Task.CompletedTask;

    public CalendarService Calendar
    {
        get
        {
            if (_calendar is null)
            {
                _calendar = new CalendarService(http, ledger);
                _calendar.Alert += (_, message) => TimerElapsed?.Invoke(this, message);
            }

            return _calendar;
        }
    }

    /// <summary>"Evet" ya da "hayır" bekleyen bir soru var mı (ör. İndirilenler'i düzenleyeyim mi?).</summary>
    public bool HasPendingConfirmation => _pendingSort is not null && DateTime.Now - _pendingAt < ConfirmationLife;

    // ---------- Araştırma raporu ----------

    private QuickReply ResearchCommand(string action, string topic)
    {
        if (Simplify(action) == "ac")
        {
            var report = _lastReport ?? LatestReport();
            if (report is null)
            {
                return new QuickReply("Henüz hazırladığım bir rapor yok. \"X hakkında rapor hazırla\" diyebilirsin.");
            }

            OpenFile(report);
            return new QuickReply("Raporu açtım.", Silent: true);
        }

        if (!_research.IsCompleted)
        {
            return new QuickReply($"Şu an \"{_researchTopic}\" hakkında bir rapor hazırlıyorum; bitince haber vereceğim.");
        }

        _researchTopic = topic.Trim();
        _research = Task.Run(() => ResearchAsync(_researchTopic, _background.Token));
        return new QuickReply($"Tamam, \"{_researchTopic}\" konusunu araştırıyorum. Birkaç dakika sürebilir; sen işine devam et, hazır olunca haber veririm.");
    }

    /// <summary>
    /// Arama → en iyi dört sayfanın okunması → yerel modelle kaynaklı bir Markdown raporu. Bitince sesli haber verilir.
    /// Rapor yalnızca D:\Friday\data\reports altına yazılır.
    /// </summary>
    private async Task ResearchAsync(string topic, CancellationToken cancellationToken)
    {
        try
        {
            await BrainReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            ledger.Record(DataFlow.Outbound, "Araştırma", $"duckduckgo.com · \"{topic}\"");
            var results = await _web.SearchAsync(topic, cancellationToken).ConfigureAwait(false);
            var readable = results.Where(result => !result.Url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
            if (readable.Count == 0)
            {
                TimerElapsed?.Invoke(this, $"\"{topic}\" için internette bir şey bulamadım.");
                return;
            }

            var pages = await Task.WhenAll(readable.Select(async result =>
            {
                try
                {
                    return (result, text: await _web.ReadAsync(new Uri(result.Url), 3500, cancellationToken, topic).ConfigureAwait(false));
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
                {
                    return (result, text: result.Snippet);
                }
            })).ConfigureAwait(false);

            var sources = new StringBuilder();
            for (var index = 0; index < pages.Length; index++)
            {
                ledger.Record(DataFlow.Outbound, "Web sayfası", $"{new Uri(pages[index].result.Url).Host} okundu (rapor)");
                sources.AppendLine(CultureInfo.InvariantCulture, $"[{index + 1}] {pages[index].result.Title} ({pages[index].result.Url})")
                    .AppendLine(pages[index].text).AppendLine();
            }

            var instruction =
                $"Aşağıdaki kaynaklara dayanarak \"{topic}\" hakkında Türkçe bir rapor yaz. Biçim Markdown olsun: önce '## Özet' başlığı " +
                "altında 3-4 cümlelik özet; sonra konuya uygun 3-5 başlık altında madde işaretli önemli noktalar (rakamları, tarihleri ve " +
                "isimleri koru); en sonda '## Sonuç' başlığı altında 2 cümle. Her bilgi kaynaklarda geçmeli, kaynaklarda olmayan bilgi " +
                "uydurma. Bilginin hangi kaynaktan geldiğini [1] biçiminde belirt.";
            var body = WithoutTitle(await chat.CompleteAsync(Settings.OllamaModel, instruction, sources.ToString(), null, null, cancellationToken).ConfigureAwait(false));
            var folder = ReportFolder.Length > 0 ? ReportFolder : Path.Combine(AppContext.BaseDirectory, "reports");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd-HHmm}-{Slug(topic)}.md");
            var document = new StringBuilder()
                .AppendLine(CultureInfo.InvariantCulture, $"# {TurkishGrammar.Capitalize(topic)}").AppendLine()
                .AppendLine(CultureInfo.InvariantCulture, $"_{DateTime.Now.ToString("d MMMM yyyy HH:mm", Turkish)} · Friday araştırma raporu_").AppendLine()
                .AppendLine(body).AppendLine()
                .AppendLine("## Kaynaklar").AppendLine();
            for (var index = 0; index < pages.Length; index++)
            {
                document.AppendLine(CultureInfo.InvariantCulture, $"{index + 1}. [{pages[index].result.Title}]({pages[index].result.Url})");
            }

            await File.WriteAllTextAsync(file, document.ToString(), cancellationToken).ConfigureAwait(false);
            _lastReport = file;
            ledger.Record(DataFlow.Local, "Rapor", $"\"{topic}\" raporu {Path.GetFileName(file)} olarak kaydedildi");
            var summary = Summary(body);
            TimerElapsed?.Invoke(this, $"\"{topic}\" raporu hazır. {summary} Görmek istersen raporu aç de.");
        }
        catch (OperationCanceledException)
        {
            // Friday kapandı.
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            FridayLog.Error("Rapor hazırlanamadı", exception);
            TimerElapsed?.Invoke(this, $"\"{topic}\" raporunu hazırlayamadım: {(exception is HttpRequestException ? "internete ulaşamadım" : "dosyaya yazamadım")}.");
        }
    }

    /// <summary>Modelin kendi eklediği "# Başlık" satırları atılır; raporun başlığını Friday koyar.</summary>
    public static string WithoutTitle(string markdown) =>
        string.Join('\n', markdown.Replace("\r", "", StringComparison.Ordinal).Split('\n')
            .SkipWhile(line => line.Trim().Length == 0 || (line.StartsWith("# ", StringComparison.Ordinal)))).Trim();

    /// <summary>Raporun "Özet" bölümünün ilk iki cümlesi (okumak için).</summary>
    public static string Summary(string markdown)
    {
        var lines = markdown.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimStart('#', ' ').StartsWith("Özet", StringComparison.OrdinalIgnoreCase));
        var text = string.Join(' ', lines.Skip(start + 1).TakeWhile(line => !line.StartsWith('#')).Select(line => line.Trim().TrimStart('-', '*', ' ')));
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\[\d+\]|\*\*|__", "").Trim();
        var sentences = text.Split(". ", StringSplitOptions.RemoveEmptyEntries).Take(2).ToList();
        return sentences.Count == 0 ? "" : string.Join(". ", sentences).TrimEnd('.') + ".";
    }

    /// <summary>"Yapay Zekâ Ajanları" → "yapay-zeka-ajanlari".</summary>
    public static string Slug(string text)
    {
        var map = new Dictionary<char, char> { ['ı'] = 'i', ['ş'] = 's', ['ğ'] = 'g', ['ü'] = 'u', ['ö'] = 'o', ['ç'] = 'c', ['â'] = 'a', ['î'] = 'i', ['û'] = 'u' };
        var slug = new StringBuilder();
        foreach (var letter in text.ToLower(Turkish))
        {
            var mapped = map.GetValueOrDefault(letter, letter);
            slug.Append(char.IsAsciiLetterOrDigit(mapped) ? mapped : '-');
        }

        var clean = System.Text.RegularExpressions.Regex.Replace(slug.ToString(), "-+", "-").Trim('-');
        return clean.Length == 0 ? "rapor" : clean[..Math.Min(clean.Length, 50)].TrimEnd('-');
    }

    private string? LatestReport()
    {
        var folder = ReportFolder.Length > 0 ? ReportFolder : Path.Combine(AppContext.BaseDirectory, "reports");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.md").OrderByDescending(File.GetLastWriteTime).FirstOrDefault() : null;
    }

    private static void OpenFile(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // .md dosyası bir uygulamayla ilişkilendirilmemişse Not Defteri açar.
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true })?.Dispose();
        }
    }

    // ---------- Takvim ----------

    private async Task<QuickReply> CalendarCommandAsync(string day, CancellationToken cancellationToken)
    {
        if (!Calendar.IsConfigured)
        {
            return new QuickReply("Takvimini henüz bağlamadık. Ayarlar › Takvim bölümüne Google ya da Outlook takviminin gizli iCal adresini yapıştırman yeter.");
        }

        var now = DateTime.Now;
        var (from, to, label) = Simplify(day) switch
        {
            "yarin" => (now.Date.AddDays(1), now.Date.AddDays(2), "Yarın"),
            "hafta" => (now, now.Date.AddDays(7), "Önümüzdeki 7 günde"),
            "sonraki" => (now, now.AddDays(30), ""),
            _ => (now, now.Date.AddDays(1), "Bugün")
        };
        var events = await Calendar.EventsAsync(from, to, cancellationToken).ConfigureAwait(false);
        if (Simplify(day) == "sonraki")
        {
            if (events.FirstOrDefault(item => !item.AllDay) is not { } next)
            {
                return new QuickReply("Önümüzdeki 30 günde takviminde bir toplantı görünmüyor.");
            }

            return new QuickReply($"Sıradaki etkinliğin {Friday.Core.Calendar.Describe(next, now)}: {next.Title}" + (next.Location is { } place ? $", {place}." : "."));
        }

        ShowListCard($"Takvim · {(label.Length > 0 ? label.ToLower(Turkish) : "sıradaki")}", "\uE787",
            events.Select(item => new CardRow(item.Title, Friday.Core.Calendar.Describe(item, now))));
        if (events.Count == 0)
        {
            return new QuickReply(Simplify(day) == "bugun" ? "Bugün için takviminde başka bir şey yok." : $"{label} takviminde bir şey yok.");
        }

        var listed = events.Take(4).Select(item => item.AllDay
            ? $"tüm gün {item.Title}"
            : $"{item.Start.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.TimeSuffix(item.Start)} {item.Title}").ToList();
        var more = events.Count > 4 ? $" ve {events.Count - 4} etkinlik daha" : "";
        return new QuickReply($"{label} {events.Count} etkinliğin var: {TurkishGrammar.Join(listed)}{more}.");
    }

    // ---------- İndirilenler ----------

    private QuickReply DownloadsCommand(string action)
    {
        if (Simplify(action) == "gerial")
        {
            return UndoSort();
        }

        var folder = WatchService.DownloadsFolder();
        if (!Directory.Exists(folder))
        {
            return new QuickReply("İndirilenler klasörünü bulamadım.");
        }

        var files = Directory.GetFiles(folder).Select(file => (file, File.GetLastWriteTime(file)));
        var plan = DownloadSorter.Plan(folder, files, DateTime.Now);
        if (plan.Count == 0)
        {
            return new QuickReply("İndirilenler zaten düzenli; ayıracak dosya yok.");
        }

        _pendingSort = plan;
        _pendingAt = DateTime.Now;
        ShowListCard("İndirilenler", "\uE896", plan
            .GroupBy(move => Path.GetFileName(Path.GetDirectoryName(move.To)) ?? "")
            .OrderByDescending(group => group.Count())
            .Select(group => new CardRow(group.Key, $"{group.Count()} dosya")));
        return new QuickReply($"İndirilenler'de {DownloadSorter.Summarize(plan)} var. Bunları Belgeler, Resimler gibi klasörlere ayırayım mı? Evet ya da hayır de.");
    }

    /// <summary>Bekleyen soruya verilen "evet" ya da "hayır".</summary>
    private QuickReply Confirm(bool yes)
    {
        var plan = _pendingSort;
        _pendingSort = null;
        if (plan is null || !yes)
        {
            return new QuickReply("Tamam, dokunmadım.");
        }

        var done = new List<FileMove>();
        foreach (var move in plan)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.To)!);
                File.Move(move.From, move.To);
                done.Add(move);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Açık ya da kilitli dosya yerinde kalır.
            }
        }

        if (UndoFile.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UndoFile)!);
            File.WriteAllText(UndoFile, JsonSerializer.Serialize(done, UndoJson));
        }

        ledger.Record(DataFlow.Local, "İndirilenler", $"{done.Count} dosya türlerine göre klasörlere taşındı (geri alınabilir)");
        return new QuickReply(done.Count == plan.Count
            ? $"{done.Count} dosyayı klasörlere ayırdım. Beğenmezsen \"düzenlemeyi geri al\" de."
            : $"{done.Count} dosyayı taşıdım; {plan.Count - done.Count} dosya açık olduğu için yerinde kaldı.");
    }

    private QuickReply UndoSort()
    {
        if (UndoFile.Length == 0 || !File.Exists(UndoFile))
        {
            return new QuickReply("Geri alınacak bir düzenleme yok.");
        }

        var moves = JsonSerializer.Deserialize<List<FileMove>>(File.ReadAllText(UndoFile)) ?? [];
        var restored = 0;
        foreach (var move in moves.Where(move => File.Exists(move.To) && !File.Exists(move.From)))
        {
            try
            {
                File.Move(move.To, move.From);
                restored++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Kilitli dosya taşındığı yerde kalır.
            }
        }

        // Boşalan tür klasörleri silinir.
        foreach (var folder in moves.Select(move => Path.GetDirectoryName(move.To)).OfType<string>().Distinct())
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }

        File.Delete(UndoFile);
        ledger.Record(DataFlow.Local, "İndirilenler", $"Düzenleme geri alındı, {restored} dosya eski yerinde");
        return new QuickReply(restored == 0 ? "Geri alınacak dosya kalmamış." : $"{restored} dosyayı eski yerine koydum.");
    }

    // ---------- Gün sonu ----------

    /// <summary>"Günü kapat": bugünün kısa özeti ve yarına bakış (hatırlatmalar, takvim, hava).</summary>
    private async Task<QuickReply> DayEndAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var parts = new List<string> { "Günün özeti." };
        var rows = new List<CardRow>();
        if (Rhythm is { Enabled: true } coach && coach.Today is { ActiveMinutes: >= 1 } today)
        {
            parts.Add(RhythmCoachLines.Status(today, TimeSpan.Zero));
            rows.Add(new CardRow("Bilgisayar başında", RhythmText.Compact(today.ActiveMinutes)));
            rows.Add(new CardRow("Mola", today.Breaks.ToString(CultureInfo.InvariantCulture)));
        }

        var talks = history.Read(history.Today).Count;
        if (talks > 0)
        {
            parts.Add($"Bugün benimle {talks} kez konuştun.");
            rows.Add(new CardRow("Sohbet", $"{talks} soru"));
        }

        var tomorrow = now.Date.AddDays(1);
        var dueTomorrow = reminders.Upcoming.Where(item => item.Due.Date == tomorrow).ToList();
        var events = Calendar.IsConfigured
            ? await Calendar.EventsAsync(tomorrow, tomorrow.AddDays(1), cancellationToken).ConfigureAwait(false)
            : [];
        if (events.Count > 0)
        {
            var first = events[0];
            parts.Add($"Yarın {events.Count} etkinliğin var; ilki {(first.AllDay ? "tüm gün" : first.Start.ToString("HH:mm", CultureInfo.InvariantCulture) + "'" + RhythmText.TimeSuffix(first.Start))} {first.Title}.");
            rows.Add(new CardRow("Yarın ilk etkinlik", first.AllDay ? first.Title : $"{first.Start:HH:mm} {first.Title}"));
        }

        if (dueTomorrow.Count > 0)
        {
            parts.Add($"Yarın için {dueTomorrow.Count} hatırlatma kurulu; ilki {dueTomorrow[0].Due.ToString("HH:mm", CultureInfo.InvariantCulture)}'{RhythmText.TimeSuffix(dueTomorrow[0].Due)}.");
            rows.Add(new CardRow("Yarın hatırlatma", $"{dueTomorrow.Count}"));
        }

        try
        {
            parts.Add(await WeatherSpeechAsync("", "yarin", rainQuestion: false, cancellationToken).ConfigureAwait(false));
            _ = TakeCard();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // İnternet yoksa hava atlanır.
        }

        var address = Settings.AddressAs.Trim();
        parts.Add((now.Hour >= 21 || now.Hour < 4 ? "İyi geceler" : "İyi akşamlar") + (address.Length > 0 ? $", {address}." : "."));
        ShowCard(new AnswerCard("Gün sonu", "\uE708") { Rows = rows });
        return new QuickReply(string.Join(" ", parts));
    }
}
