using System.Diagnostics;
using System.Globalization;
using System.Text;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Friday'i benzerlerinden ayıran araçlar: ekranı görme, pano, hafıza, dosya radarı, rutinler, brifing.</summary>
public sealed partial class AssistantTools
{
    private const int MaxClipboardCharacters = 12_000;

    private IReadOnlyList<FoundFile> _lastFiles = [];

    private async Task<string> LookAtScreenAsync(string question, bool wholeScreen, CancellationToken cancellationToken)
    {
        var screen = ScreenCapture.Capture(wholeScreen);
        ledger.Record(DataFlow.Local, "Ekran görüntüsü",
            $"{screen.Title} · bellekte işlendi, kaydedilmedi, yerel modele verildi");
        // Görme modeli yalnızca bu istek için yüklenir ve hemen boşaltılır (keep_alive 0); sohbet modeli görme
        // modülü taşımadığı için sürekli ~1,2 GB daha az VRAM kullanır.
        var answer = await chat.CompleteAsync(
            Settings.VisionModel,
            "Kullanıcının bilgisayar ekranının görüntüsüne bakıyorsun. Soruyu ekranda gördüklerine göre Türkçe, kısa ve net " +
            "cevapla; cevabın sesli okunacak, en fazla 3 cümle kullan, markdown kullanma. Hata mesajı varsa ne anlama " +
            "geldiğini ve nasıl çözüleceğini söyle. Ekranda olmayan şeyleri uydurma.",
            string.IsNullOrWhiteSpace(question) ? "Ekranda ne var?" : question,
            [screen.Jpeg],
            "0",
            cancellationToken).ConfigureAwait(false);
        return answer;
    }

    /// <summary>
    /// Sonucu doğrudan kullanıcıya okunan araçlar. Ekran cevabı görme modelinden hazır gelir; sohbet modelini yeniden
    /// yükleyip aynı şeyi bir daha söyletmek ~3 sn kaybettirir.
    /// </summary>
    public static bool AnswersDirectly(string tool) => tool == "ekrana_bak";

    private async Task<string> ClipboardAsync(string action, string language, CancellationToken cancellationToken)
    {
        var text = ClipboardText.Get().Trim();
        if (text.Length == 0)
        {
            return "Panoda metin yok. Önce bir metni kopyalaması gerekiyor.";
        }

        if (text.Length > MaxClipboardCharacters)
        {
            text = text[..MaxClipboardCharacters];
        }

        ledger.Record(DataFlow.Local, "Pano", $"{text.Length} karakter okundu, yerel modelde işlendi");
        var operation = Simplify(action);
        if (operation is "oku" or "")
        {
            return $"Panodaki metin: {text}";
        }

        var target = string.IsNullOrWhiteSpace(language) ? "İngilizce" : language.Trim();
        var instruction = operation switch
        {
            "ozetle" => "Aşağıdaki metni metnin kendi dilinde kısa ve öz özetle. Yalnızca özeti yaz.",
            "cevir" => $"Aşağıdaki metni {target} diline çevir. Biçimi koru, yalnızca çeviriyi yaz.",
            "duzelt" => "Aşağıdaki metindeki yazım, noktalama ve dil bilgisi hatalarını düzelt. Anlamı, dili ve biçimi koru. Yalnızca düzeltilmiş metni yaz.",
            "resmilestir" => "Aşağıdaki metni aynı dilde, kibar ve profesyonel bir üslupla yeniden yaz (ör. iş e-postası). Yalnızca yeni metni yaz.",
            "maddele" => "Aşağıdaki metni aynı dilde, kısa madde işaretli bir listeye dönüştür. Yalnızca listeyi yaz.",
            "acikla" => "Aşağıdaki metni (kod, hata mesajı ya da yazı olabilir) Türkçe, sade ve kısa bir şekilde açıkla.",
            _ => null
        };
        if (instruction is null)
        {
            return $"Bilinmeyen pano işlemi: {action}";
        }

        var result = await chat.CompleteAsync(Settings.OllamaModel, instruction, text, null, null, cancellationToken).ConfigureAwait(false);
        if (operation == "acikla")
        {
            return $"Açıklama: {result}";
        }

        ClipboardText.Set(result);
        ledger.Record(DataFlow.Local, "Pano", $"Sonuç ({result.Length} karakter) panoya yazıldı");
        var preview = result.Length > 400 ? result[..400] + "…" : result;
        return $"Sonuç panoya kondu, Ctrl+V ile yapıştırabilir. Önizleme: {preview}";
    }

    private string Remember(string fact)
    {
        if (string.IsNullOrWhiteSpace(fact))
        {
            return "Hatırlanacak bir bilgi söylenmedi.";
        }

        memory.Remember(fact);
        ledger.Record(DataFlow.Local, "Hafıza", $"Kaydedildi: {fact}");
        return $"Hafızaya kaydedildi: {fact}";
    }

    private string Forget(string topic)
    {
        var removed = memory.Forget(topic);
        if (removed.Count == 0)
        {
            return $"Hafızada '{topic}' ile ilgili bir bilgi yok.";
        }

        ledger.Record(DataFlow.Local, "Hafıza", $"{removed.Count} bilgi silindi");
        return "Silindi: " + string.Join("; ", removed.Select(item => item.Text));
    }

    private string FindFiles(ToolCall call)
    {
        var text = Argument(call, "arama");
        var kind = Argument(call, "tur");
        _ = int.TryParse(Argument(call, "son_gun"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days);
        var folder = Argument(call, "klasor");
        _lastFiles = WindowsFileSearch.Find(text, kind, days, folder);
        ledger.Record(DataFlow.Local, "Dosya radarı",
            $"Windows Arama dizininde arandı: {string.Join(" ", new[] { text, kind, folder }.Where(part => part.Length > 0))}");
        if (_lastFiles.Count == 0)
        {
            return "Uygun dosya bulunamadı.";
        }

        if (Flag(call, "ac"))
        {
            return OpenFound("1", revealInFolder: false);
        }

        var lines = _lastFiles.Select((file, index) =>
            $"{index + 1}) {file.Name} — {Path.GetFileName(Path.GetDirectoryName(file.Path))} klasöründe, {Age(file.Modified)} değişti");
        return $"{_lastFiles.Count} dosya bulundu:\n" + string.Join('\n', lines);
    }

    private string OpenFound(string numberText, bool revealInFolder)
    {
        if (!int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || number < 1 || number > _lastFiles.Count)
        {
            return _lastFiles.Count == 0 ? "Önce dosya_bul ile arama yapılmalı." : $"1 ile {_lastFiles.Count} arasında bir numara söylenmeli.";
        }

        var file = _lastFiles[number - 1];
        if (revealInFolder)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.Path}\"") { UseShellExecute = true })?.Dispose();
        }
        else
        {
            Launch(file.Path);
        }

        ledger.Record(DataFlow.Local, "Dosya", $"{file.Name} {(revealInFolder ? "klasörde gösterildi" : "açıldı")}");
        return $"{file.Name} {(revealInFolder ? "klasöründe gösterildi" : "açıldı")}.";
    }

    private async Task<string> RunRoutineAsync(string name, CancellationToken cancellationToken)
    {
        var match = Settings.Routines.FirstOrDefault(routine => Simplify(routine.Key) == Simplify(name));
        if (match.Key is null)
        {
            return $"'{name}' adında bir rutin yok. Tanımlı rutinler: {string.Join(", ", Settings.Routines.Keys)}.";
        }

        var results = new List<string>();
        foreach (var text in match.Value)
        {
            var step = RoutineStep.Parse(text);
            results.Add(step.Action switch
            {
                RoutineAction.App => OpenApp(step.Value),
                RoutineAction.Website => OpenWebsite(step.Value),
                RoutineAction.Search => Search(step.Value),
                RoutineAction.Media => await MediaAsync(step.Value).ConfigureAwait(false),
                RoutineAction.Say => $"Kullanıcıya şunu söyle: {step.Value}",
                RoutineAction.Briefing => await BriefingAsync(cancellationToken).ConfigureAwait(false),
                RoutineAction.LinguaFlow => await LinguaFlowAsync("baslat", cancellationToken).ConfigureAwait(false),
                RoutineAction.Rhythm => RhythmStep(step.Value),
                RoutineAction.Volume => SystemVolume.Apply(Math.Clamp(step.Number, 0, 100)),
                RoutineAction.Wait => await WaitAsync(step.Number, cancellationToken).ConfigureAwait(false),
                RoutineAction.Remind => step.Value.Split('|', 2, StringSplitOptions.TrimEntries) is [var when, var what]
                    ? ReminderCommand("kur", when, what)
                    : "",
                _ => ""
            });
        }

        return $"'{match.Key}' rutini çalıştı. " + string.Join(" ", results);
    }

    private static async Task<string> WaitAsync(int seconds, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 60)), cancellationToken).ConfigureAwait(false);
        return "";
    }

    private async Task<string> BriefingAsync(CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Tarih: {DateTime.Now.ToString("d MMMM yyyy dddd, HH:mm", Turkish)}. ");
        try
        {
            text.Append("Hava: ").Append(await WeatherAsync("", cancellationToken).ConfigureAwait(false)).Append(' ');
        }
        catch (HttpRequestException)
        {
            text.Append("Hava durumu alınamadı (internet yok). ");
        }

        var upcoming = reminders.Upcoming.Where(item => item.Due.Date <= DateTime.Today.AddDays(1)).ToList();
        text.Append(upcoming.Count == 0
            ? "Bugün için kurulu hatırlatma yok. "
            : "Hatırlatmalar: " + string.Join(", ", upcoming.Select(item =>
                $"{(item.Text.Length > 0 ? item.Text : "zamanlayıcı")} ({TurkishTime.Describe(item.Due, DateTime.Now)})")) + ". ");

        // Tarihi belli olanlar kaç gün kaldığıyla, tarihsizler "yarın/hafta" geçiyorsa söylenir.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var soon = memory.Items
            .Select(item => (item.Text, Left: MemoryDates.NextOccurrence(item.Text, today) is { } date ? date.DayNumber - today.DayNumber : (int?)null))
            .Where(item => item.Left is <= 14 || (item.Left is null && MentionsUpcoming(item.Text)))
            .OrderBy(item => item.Left ?? 99)
            .Select(item => item.Left switch { 0 => $"{item.Text} (bugün)", 1 => $"{item.Text} (yarın)", { } days => $"{item.Text} ({days} gün kaldı)", _ => item.Text })
            .ToList();
        if (soon.Count > 0)
        {
            text.Append("Hafızadaki yaklaşan şeyler: ").AppendJoin(" ", soon).Append(' ');
        }

        var yesterday = history.Read(history.Today.AddDays(-1));
        if (yesterday.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"Dün {yesterday.Count} kez konuştunuz.");
        }

        return text.ToString();
    }

    private string History(string day, string query = "")
    {
        if (query.Trim().Length > 0)
        {
            var hits = history.Search(query, 8);
            ledger.Record(DataFlow.Local, "Geçmiş", $"Sohbetlerde \"{query.Trim()}\" arandı");
            return hits.Count == 0
                ? $"Geçmiş sohbetlerde '{query.Trim()}' geçmiyor."
                : $"'{query.Trim()}' geçen sohbetler:\n" + string.Join('\n', hits.Select(hit =>
                    $"{hit.Chat.Updated.ToString("d MMMM", Turkish)} · {hit.Chat.Title}: {hit.Snippet}"));
        }

        var date = Simplify(day) switch
        {
            "" or "bugun" => history.Today,
            "dun" => history.Today.AddDays(-1),
            _ => DateOnly.TryParse(day, CultureInfo.InvariantCulture, out var parsed) ? parsed : history.Today
        };
        var turns = history.Read(date);
        ledger.Record(DataFlow.Local, "Geçmiş", $"{date.ToString("d MMMM", Turkish)} sohbetleri okundu");
        if (turns.Count == 0)
        {
            return $"{date.ToString("d MMMM", Turkish)} için kayıtlı sohbet yok.";
        }

        // Model özetleyebilsin diye her tur kısaltılarak verilir.
        return $"{date.ToString("d MMMM", Turkish)} sohbetleri:\n" + string.Join('\n', turns.TakeLast(30).Select(turn =>
            $"{turn.Time:HH:mm} kullanıcı: {Shorten(turn.User, 160)} | {Settings.AssistantName}: {Shorten(turn.Assistant, 200)}"));
    }

    /// <summary>Ay adı ya da "yarın/hafta" geçen hatırlatmalar brifinge eklenir.</summary>
    private static bool MentionsUpcoming(string text)
    {
        var lower = text.ToLower(Turkish);
        var month = DateTime.Now.ToString("MMMM", Turkish).ToLower(Turkish);
        var next = DateTime.Now.AddMonths(1).ToString("MMMM", Turkish).ToLower(Turkish);
        return lower.Contains(month, StringComparison.Ordinal) || lower.Contains(next, StringComparison.Ordinal)
            || lower.Contains("yarın", StringComparison.Ordinal) || lower.Contains("hafta", StringComparison.Ordinal);
    }

    private static string Age(DateTime modified)
    {
        var days = (DateTime.Now.Date - modified.Date).Days;
        return days switch
        {
            0 => $"bugün {modified:HH:mm}'de",
            1 => "dün",
            < 7 => $"{days} gün önce",
            _ => modified.ToString("d MMMM yyyy", Turkish) + "'de"
        };
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}
