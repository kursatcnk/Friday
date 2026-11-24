using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Önündeki belge (bkz. <see cref="FrontDocument"/>): "bu ne", "bunu Türkçeye çevir", "bu sözleşmede depozito ne kadar".
/// Friday çağrıldığı anda öndeki pencere (<see cref="ActiveWindow"/>) bilinir; başlığındaki belge dosyası bulunursa metnin
/// tamamı okunur, bulunamazsa pencerenin görüntüsündeki yazı yerel görme modeliyle okunur. Hiçbir şey dışarı gitmez.
/// </summary>
public sealed partial class AssistantTools
{
    private const int FrontTextLimit = 60000;

    /// <summary>Son okunan belge: aynı pencerede "devamını çevir" kaldığı yerden sürer.</summary>
    private (string Key, string Name, string Text, int Offset)? _front;

    private async Task<string> FrontAsync(string action, string question, CancellationToken cancellationToken)
    {
        if (await FrontTextAsync(Simplify(action) == "devam", cancellationToken).ConfigureAwait(false) is not var (name, text))
        {
            return "Önünde okuyabileceğim bir belge ya da sayfa bulamadım. Belgeyi öne getirip yeniden söyle.";
        }

        var subject = name.Length > 0 ? $"\"{name}\"" : "Ekrandaki sayfa";
        switch (Simplify(action))
        {
            case "cevir" or "devam":
                var offset = Simplify(action) == "devam" && _front is { } current ? current.Offset : 0;
                var (chunk, next) = FrontDocument.Chunk(text, offset);
                if (chunk.Length == 0)
                {
                    return $"{subject} bitti; çevrilecek başka bir şey kalmadı.";
                }

                _front = _front is { } front ? front with { Offset = next } : null;
                if (FrontDocument.LooksTurkish(chunk))
                {
                    return $"{subject} zaten Türkçe. " + await FrontLlmAsync(
                        "Aşağıdaki Türkçe metnin ne anlattığını 2 cümleyle söyle; markdown yok.", chunk, cancellationToken).ConfigureAwait(false);
                }

                var translation = await FrontLlmAsync(
                    "Translate the user's text into natural, fluent Turkish. Output only the Turkish translation, keep numbers, " +
                    "names and dates exactly, no notes, no headings, no markdown.",
                    chunk, cancellationToken).ConfigureAwait(false);
                var more = next < text.Length ? " Devamı için \"devamını çevir\" de." : "";
                return translation + more;
            case "ozetle":
                return await FrontLlmAsync(
                    "Kullanıcının ekranında açık olan belge ya da sayfa aşağıda. Dili ne olursa olsun Türkçe, 3-4 cümleyle özetle: " +
                    "ne olduğu, ana fikir ve en önemli noktalar. Cevap sesli okunacak; düz cümleler, markdown yok.",
                    FrontDocument.Relevant(text, "", 6000), cancellationToken).ConfigureAwait(false);
            case "sor" when question.Trim().Length > 0:
                return await FrontLlmAsync(
                    "Kullanıcının ekranında açık olan belgenin ilgili bölümleri aşağıda. Soruyu yalnızca bu metne dayanarak, Türkçe ve " +
                    "kısa (en fazla 3 cümle) cevapla; metinde yoksa açıkça 'Belgede bununla ilgili bir şey göremedim' de. Sayıları ve " +
                    "tarihleri metindeki gibi ver. Markdown yok.\nSoru: " + question.Trim(),
                    FrontDocument.Relevant(text, question, 6000), cancellationToken).ConfigureAwait(false);
            default:
                // Bu belgenin önceki bir sürümü okunmuşsa (aynı dosya değişmiş ya da "teklif_v1.pdf") farkı sormak önerilir.
                return await FrontLlmAsync(
                    "Kullanıcı ekranında açık olan belge ya da sayfa için 'bu ne, açıkla' dedi. Dili ne olursa olsun Türkçe açıkla: " +
                    "ne olduğu, ne anlattığı ve kullanıcı için önemli olan noktalar (tarih, tutar, yapılması gereken bir şey varsa " +
                    "onu da söyle). 4-6 cümle, sade konuşma dili; cevap sesli okunacak, markdown yok.",
                    FrontDocument.Relevant(text, "", 6000), cancellationToken).ConfigureAwait(false) + VersionNote(name);
        }
    }

    private async Task<string> FrontLlmAsync(string instruction, string text, CancellationToken cancellationToken) =>
        (await chat.CompleteAsync(Settings.OllamaModel, instruction, text, null, null, cancellationToken).ConfigureAwait(false)).Trim();

    /// <summary>
    /// Öndeki pencerenin metni ve belgenin adı. Önce başlıktaki dosya aranır (belge dizini, İndirilenler, Masaüstü,
    /// Belgeler); yoksa pencerenin görüntüsündeki yazı okunur. <paramref name="reuse"/> ise son okunan metin kullanılır.
    /// </summary>
    private async Task<(string Name, string Text)?> FrontTextAsync(bool reuse, CancellationToken cancellationToken)
    {
        var window = ActiveWindow;
        var key = window is null ? "" : $"{window.Process}|{window.Title}";
        if (_front is { } known && (reuse || known.Key == key))
        {
            return (known.Name, known.Text);
        }

        if (window is not null && FrontDocument.FileName(window.Title) is { } file && FindFrontFile(file) is { } path)
        {
            try
            {
                var text = await Task.Run(() => DocumentText.Extract(path, FrontTextLimit), cancellationToken).ConfigureAwait(false);
                if (text.Trim().Length > 40)
                {
                    ledger.Record(DataFlow.Local, "Önündeki belge", $"{Path.GetFileName(path)} okundu");
                    RememberFront(path, text);
                    _front = (key, Path.GetFileName(path), text.Trim(), 0);
                    return (Path.GetFileName(path), text.Trim());
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
            {
                FridayLog.Warn($"Önündeki belge okunamadı: {exception.Message}");
            }
        }

        // Dosya yoksa (web sayfası, taranmış PDF, uygulama penceresi) pencerenin görüntüsündeki yazı okunur.
        var screen = ScreenCapture.Capture(false);
        ledger.Record(DataFlow.Local, "Ekran görüntüsü", $"{screen.Title} · yazısı okundu, görüntü kaydedilmedi");
        var read = (await chat.CompleteAsync(
            Settings.VisionModel,
            "Görseldeki bütün yazıyı olduğu gibi, satır düzenini koruyarak çıkar. Yorum ekleme; yalnızca yazıyı ver. Yazı yoksa 'Yazı yok' de.",
            "Görseldeki yazıyı çıkar.",
            [screen.Jpeg],
            "0",
            cancellationToken).ConfigureAwait(false)).Trim();
        if (read.Length < 20 || read.StartsWith("Yazı yok", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        _front = (key, "", read, 0);
        return ("", read);
    }

    /// <summary>Başlıktaki dosya adının diskteki yeri: belge dizini, sonra İndirilenler, Masaüstü ve Belgeler.</summary>
    private string? FindFrontFile(string file)
    {
        if (Documents?.Find(file) is { } indexed && File.Exists(indexed) && string.Equals(Path.GetFileName(indexed), file, StringComparison.OrdinalIgnoreCase))
        {
            return indexed;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] folders =
        [
            Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        ];
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        foreach (var folder in folders.Where(Directory.Exists))
        {
            if (Directory.EnumerateFiles(folder, file, options).FirstOrDefault() is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
