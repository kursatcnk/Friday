using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed partial class AssistantTools
{
    /// <summary>Pomodoro serisini başlatan/durduran asistan işlevi: (adet) → cevap; 0 adet durdurur.</summary>
    public Func<int, string>? PomodoroControl { get; set; }

    private (string Name, List<string> Items, int Index, List<string> Missing)? _checklist;

    /// <summary>Sesli kontrol listesi sürüyor mu (bir sonraki cümle cevaptır ve Friday dinlemeye devam eder)?</summary>
    public bool InChecklist => _checklist is not null;

    // ---------------------------------------------------------------- e-posta

    /// <summary>Taslak varsayılan e-posta uygulamasında açılır ve metin panoya kopyalanır; Friday göndermez.</summary>
    private string EmailDraft(string to, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Taslağın metni boş.";
        }

        ClipboardText.Set(body.Trim());
        var address = to.Contains('@', StringComparison.Ordinal) ? to.Trim() : "";
        var uri = $"mailto:{Uri.EscapeDataString(address)}?subject={Uri.EscapeDataString(subject.Trim())}&body={Uri.EscapeDataString(body.Trim().Length > 1800 ? body.Trim()[..1800] : body.Trim())}";
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Metni panoya kopyaladım; bu bilgisayarda varsayılan bir e-posta uygulaması yok, istediğin yere yapıştırabilirsin.";
        }

        ledger.Record(DataFlow.Local, "E-posta taslağı", "Taslak e-posta uygulamasında açıldı; gönderilmedi");
        return "Taslak e-posta uygulamanda açıldı ve metni panoya da kopyaladım; kontrol edip göndermek senin elinde.";
    }

    // ---------------------------------------------------------------- pomodoro

    private string PomodoroCommand(string action, string count)
    {
        if (PomodoroControl is not { } control)
        {
            return "Pomodoro için ritim koçunun açık olması gerekiyor.";
        }

        return control(Simplify(action) is "durdur" or "bitir" or "iptal" ? 0 : int.TryParse(count, out var value) && value > 0 ? Math.Min(value, 8) : 4);
    }

    // ---------------------------------------------------------------- ruh hali

    private string MoodCommand(string score, string word)
    {
        if (Journal is not { } journal)
        {
            return "Günlük şu an kullanılamıyor.";
        }

        var value = int.TryParse(score, out var parsed) ? Math.Clamp(parsed, 1, 10) : 5;
        journal.Add(DateTime.Now, $"Ruh hali: {value}/10{(string.IsNullOrWhiteSpace(word) || int.TryParse(word, out _) ? "" : $" ({word.Trim()})")}");
        ledger.Record(DataFlow.Local, "Günlük", "Ruh hali kaydedildi");
        return value switch
        {
            >= 8 => "Harika, not ettim. Böyle günler çoğalsın.",
            >= 6 => "Not ettim, iyi gidiyor.",
            >= 4 => "Not ettim. Kısa bir mola ya da yürüyüş iyi gelebilir.",
            _ => "Not ettim. Zor bir gün gibi; biraz nefes egzersizi yapmak ister misin? \"Nefes egzersizi\" demen yeter."
        };
    }

    // ---------------------------------------------------------------- temizlik

    /// <summary>Geçici dosyalar (1 günden eski) ve geri dönüşüm kutusu: boyutlar söylenir, "evet" denince temizlenir.</summary>
    private QuickReply CleanupOffer()
    {
        var temp = Path.GetTempPath();
        var cutoff = DateTime.Now.AddDays(-1);
        var oldFiles = new List<string>();
        long tempBytes = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(temp, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < cutoff)
                    {
                        oldFiles.Add(file);
                        tempBytes += info.Length;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Okunamayan dosya sayılmaz.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Geçici klasör okunamadıysa yalnızca geri dönüşüm kutusuna bakılır.
        }

        var bin = new RecycleBinInfo { Size = Marshal.SizeOf<RecycleBinInfo>() };
        var binBytes = NativeMethods.SHQueryRecycleBinW(null, ref bin) == 0 ? bin.Bytes : 0;
        if (tempBytes + binBytes < 50L * 1024 * 1024)
        {
            return new QuickReply("Temizlenecek kayda değer bir şey yok; geçici dosyalar ve geri dönüşüm kutusu zaten hafif.");
        }

        Ask("Temizlik", _ => Task.FromResult(Cleanup(oldFiles, binBytes > 0)));
        return new QuickReply($"Geçici dosyalarda bir günden eski {DataMaintenance.Describe(tempBytes)}, geri dönüşüm kutusunda {DataMaintenance.Describe(binBytes)} var. "
                              + "Temizleyeyim mi? Geri dönüşüm kutusundakiler kalıcı olarak silinir.");
    }

    private QuickReply Cleanup(IReadOnlyList<string> files, bool emptyBin)
    {
        long freed = 0;
        foreach (var file in files)
        {
            try
            {
                var length = new FileInfo(file).Length;
                File.Delete(file);
                freed += length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Kullanımdaki dosya atlanır.
            }
        }

        var bin = new RecycleBinInfo { Size = Marshal.SizeOf<RecycleBinInfo>() };
        if (emptyBin && NativeMethods.SHQueryRecycleBinW(null, ref bin) == 0 && bin.Bytes > 0
            && NativeMethods.SHEmptyRecycleBinW(IntPtr.Zero, null, 0x1 | 0x2 | 0x4) == 0)
        {
            freed += bin.Bytes;
        }

        ledger.Record(DataFlow.Local, "Temizlik", $"{DataMaintenance.Describe(freed)} boşaltıldı (geçici dosyalar ve geri dönüşüm kutusu)");
        return new QuickReply($"Tamam, {DataMaintenance.Describe(freed)} yer açıldı. Kullanımdaki birkaç dosya atlanmış olabilir.");
    }

    // ---------------------------------------------------------------- kontrol listesi

    /// <summary>"Seyahat listesini kontrol et": öğeler tek tek sorulur.</summary>
    private QuickReply StartChecklist(string name)
    {
        if (Lists is not { } lists)
        {
            return new QuickReply("Listeler şu an kullanılamıyor.");
        }

        var list = name.Trim().ToLower(Turkish);
        var items = lists.Items(list);
        if (items.Count == 0)
        {
            return new QuickReply($"\"{list}\" listesi boş. Önce \"{list} listesine pasaport, şarj aleti ekle\" gibi öğeler ekle.");
        }

        _checklist = (list, [.. items], 0, []);
        return new QuickReply($"{TurkishGrammar.Capitalize(list)} kontrol listesi, {items.Count} madde. Her birine tamam ya da yok de. İlki: {items[0]}?");
    }

    /// <summary>Kontrol listesinde bir cevap: tamam/var → sonraki, yok/eksik → eksiklere, bitir → kapat.</summary>
    public QuickReply ChecklistAnswer(string text)
    {
        if (_checklist is not { } state)
        {
            return new QuickReply("Açık bir kontrol listesi yok.");
        }

        var answer = Simplify(text);
        if (answer is "bitir" or "dur" or "iptal" or "vazgec" or "kapat")
        {
            _checklist = null;
            return new QuickReply("Kontrol listesini kapattım.");
        }

        if (answer.StartsWith("yok", StringComparison.Ordinal) || answer.Contains("eksik", StringComparison.Ordinal)
            || answer.Contains("unuttum", StringComparison.Ordinal) || answer.StartsWith("hayir", StringComparison.Ordinal) || answer.StartsWith("degil", StringComparison.Ordinal))
        {
            state.Missing.Add(state.Items[state.Index]);
        }

        var next = state.Index + 1;
        if (next >= state.Items.Count)
        {
            _checklist = null;
            return new QuickReply(state.Missing.Count == 0
                ? $"Hepsi tamam, {state.Name} listesi hazır!"
                : $"Kontrol bitti. Eksikler: {string.Join(", ", state.Missing)}.");
        }

        _checklist = (state.Name, state.Items, next, state.Missing);
        return new QuickReply($"{state.Items[next]}?");
    }

    // ---------------------------------------------------------------- hava uyarısı

    /// <summary>Yarın yağmur (≥%60), çok sıcak (≥35°) ya da don (≤0°) bekleniyorsa söylenecek cümle; yoksa null.</summary>
    public async Task<string?> TomorrowWeatherAlertAsync(CancellationToken cancellationToken)
    {
        var forecast = await FetchForecastAsync(Settings.City, 2, cancellationToken).ConfigureAwait(false);
        if (forecast is not { Days.Count: >= 2 })
        {
            return null;
        }

        var tomorrow = forecast.Days[1];
        var notes = new List<string>();
        if (tomorrow.RainChance >= 60)
        {
            notes.Add($"yarın %{tomorrow.RainChance:0} yağış ihtimali var, şemsiyeni hazırla");
        }

        if (tomorrow.Max >= 35)
        {
            notes.Add($"yarın hava {tomorrow.Max:0} dereceye kadar çıkacak, bol su iç");
        }

        if (tomorrow.Min <= 0)
        {
            notes.Add($"gece {tomorrow.Min:0} dereceye düşecek, sıkı giyin");
        }

        return notes.Count == 0 ? null : TurkishGrammar.Capitalize($"{forecast.Place} için {string.Join("; ", notes)}.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleBinInfo
    {
        public int Size;
        public long Bytes;
        public long Items;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int SHQueryRecycleBinW(string? rootPath, ref RecycleBinInfo info);

        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int SHEmptyRecycleBinW(IntPtr window, string? rootPath, uint flags);
    }
}
