using System.Diagnostics;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed partial class AssistantTools
{
    /// <summary>Masa düzenlerinin dosyası (data\\workspaces.json); asistan kurulurken verilir.</summary>
    public WorkspaceStore? Workspaces { get; set; }

    private async Task<QuickReply> WorkspaceCommandAsync(string action, string name, CancellationToken cancellationToken)
    {
        if (Workspaces is not { } store)
        {
            return new QuickReply("Masa düzenleri şu an kullanılamıyor.");
        }

        name = string.IsNullOrWhiteSpace(name) ? "masam" : name.Trim().ToLower(Turkish);
        var title = TurkishGrammar.Capitalize(name);
        switch (action)
        {
            case "kaydet":
                var layout = await Task.Run(Desktop.CaptureLayout, cancellationToken).ConfigureAwait(false);
                if (layout.Count == 0)
                {
                    return new QuickReply("Kaydedilecek açık bir program penceresi yok.");
                }

                store.Save(name, layout);
                ledger.Record(DataFlow.Local, "Masa düzeni", $"{title}: {layout.Count} pencerenin programı ve yeri kaydedildi (içerik değil)");
                var apps = string.Join(", ", layout.Select(window => window.Name).Distinct().Take(6));
                return new QuickReply($"{title} kaydedildi: {layout.Count} pencere ({apps}). \"{name} masasını kur\" dersen hepsini geri yerine koyarım.");
            case "listele":
                return new QuickReply(store.Names.Count == 0
                    ? "Kayıtlı masa düzeni yok. Pencereleri yerleştir ve \"kodlama masasını kaydet\" de."
                    : $"Kayıtlı masalar: {string.Join(", ", store.Names)}.");
            case "sil":
                return new QuickReply(store.Remove(name) ? $"{title} silindi." : $"\"{name}\" adında bir masa düzeni yok.");
            default:
                if (store.Find(name) is not { } saved)
                {
                    return new QuickReply(store.Names.Count == 0
                        ? $"\"{name}\" adında bir masa düzeni yok. Önce pencereleri yerleştirip \"{name} masasını kaydet\" de."
                        : $"\"{name}\" adında bir masa düzeni yok. Kayıtlı olanlar: {string.Join(", ", store.Names)}.");
                }

                var placed = await Desktop.RestoreLayoutAsync(saved, cancellationToken).ConfigureAwait(false);
                ledger.Record(DataFlow.Local, "Masa düzeni", $"{title} kuruldu");
                RecordHabit($"masa:{name}", $"{name} masası");
                return new QuickReply(placed == saved.Count
                    ? $"{title} hazır, {placed} pencere yerinde."
                    : $"{title} kuruldu: {placed}/{saved.Count} pencere yerleşti; açılamayanlar kaldırılmış ya da yönetici izni istiyor olabilir.");
        }
    }

    /// <summary>"rutin_zamanla": rutini her gün / hafta içi / hafta sonu belirli saatte ya da Friday açılınca çalıştırır.</summary>
    private string ScheduleRoutine(string name, string when)
    {
        var routine = Settings.Routines.Keys.FirstOrDefault(key => Simplify(key) == Simplify(name));
        if (routine is null)
        {
            return $"'{name}' adında bir rutin yok. Tanımlı rutinler: {string.Join(", ", Settings.Routines.Keys)}.";
        }

        if (Simplify(when) is "kapat" or "kaldir" or "iptal" or "")
        {
            Settings.RoutineSchedules.Remove(routine);
            SaveSettings?.Invoke(Settings);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return $"{TurkishGrammar.Capitalize(routine)} artık kendiliğinden çalışmayacak.";
        }

        if (RoutineSchedule.Parse(when) is not { } schedule)
        {
            return "Zamanı anlayamadım; '09:00 haftaici', '18:30 hergun' ya da 'acilis' biçiminde ver.";
        }

        Settings.RoutineSchedules[routine] = schedule.Format();
        SaveSettings?.Invoke(Settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return $"Tamam, {routine} {schedule.Describe().ToLower(Turkish)} kendiliğinden çalışacak.";
    }

    /// <summary>"dosya:" adımı: tam yol ya da masaüstü, belgeler, indirilenler, resimler, müzik, videolar.</summary>
    private static string OpenPath(string value)
    {
        var path = Simplify(value) switch
        {
            "masaustu" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "belgeler" or "belgelerim" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "indirilenler" or "indirmeler" => WatchService.DownloadsFolder(),
            "resimler" or "resimlerim" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "muzik" or "muziklerim" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videolar" or "videolarim" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            _ => Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'))
        };
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return $"{value} bulunamadı.";
        }

        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        var leaf = Path.GetFileName(path.TrimEnd('\\'));
        return $"{(leaf.Length > 0 ? leaf : path)} açıldı.";
    }
}
