using System.Text;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed partial class AssistantTools
{
    /// <summary>dosya_kaydet'in klasörü (D:\Friday\Kaydedilenler); C: sürücüsüne yazılmaz.</summary>
    public string? SaveFolder { get; set; }

    /// <summary>Belge hafızası; ayarlarda kapalıysa null.</summary>
    public DocumentLibrary? Documents { get; set; }

    /// <summary>"belgelerde_ara": soruya en uygun parçalar, hangi dosyadan geldikleriyle.</summary>
    private string SearchDocuments(string question)
    {
        if (Documents is not { } library)
        {
            return "Belge hafızası kapalı; Ayarlar › Belge hafızası'ndan açılabilir.";
        }

        var hits = library.Search(question, 5);
        if (hits.Count == 0)
        {
            return library.IsScanning
                ? "Belgeler hâlâ taranıyor; bu soruya uyan bir parça henüz yok. Biraz sonra tekrar sorulabilir."
                : "Belgelerde bu soruya uyan bir şey bulamadım.";
        }

        ledger.Record(DataFlow.Local, "Belge hafızası", $"\"{question}\" için {hits.Count} parça okundu");
        var result = new StringBuilder("Belgelerdeki en ilgili parçalar (cevabı bunlara dayandır, dosya adını söyle; parçalarda yoksa bilmediğini söyle):\n");
        for (var index = 0; index < hits.Count; index++)
        {
            var (path, text) = hits[index];
            var clipped = text.Length > 700 ? text[..700] + "…" : text;
            result.Append('[').Append(index + 1).Append("] ").Append(Path.GetFileName(path))
                .Append(" (").Append(Path.GetFileName(Path.GetDirectoryName(path))).Append("): ").Append(clipped).Append('\n');
        }

        return result.ToString();
    }

    /// <summary>
    /// Belgelerle ilgili bir soruda, model daha düşünmeden ilgili parçalar bulunur ve talimata eklenir: araç turu ve
    /// düşünme adımı gerekmez (cevap ~2-3 sn erken gelir), model de sayı uyduramaz. Uyan parça yoksa null.
    /// </summary>
    public string? DocumentContext(string question)
    {
        if (Documents is not { } library || library.Search(question, 4) is not { Count: > 0 } hits)
        {
            return null;
        }

        ledger.Record(DataFlow.Local, "Belge hafızası", $"\"{question}\" için {hits.Count} parça okundu");
        var context = new StringBuilder("Kullanıcının belgelerinden bu soruyla ilgili olabilecek parçalar (ilgiliyse cevabı bunlara dayandır ve dosya adını söyle; ilgisizse yok say, gerekirse belgelerde_ara ya da belge_oku kullan):\n");
        for (var index = 0; index < hits.Count; index++)
        {
            var (path, text) = hits[index];
            context.Append('[').Append(index + 1).Append("] ").Append(Path.GetFileName(path)).Append(": ")
                .Append(text.Length > 650 ? text[..650] + "…" : text).Append('\n');
        }

        return context.ToString();
    }

    /// <summary>"belge_oku": adı verilen belgenin metni (özetlemek ya da soru cevaplamak için).</summary>
    private string ReadDocument(string name)
    {
        var path = File.Exists(name) ? name : Documents?.Find(name);
        if (path is null)
        {
            return Documents is null
                ? "Belge hafızası kapalı; dosyanın tam yolunu ver ya da Ayarlar'dan belge hafızasını aç."
                : $"\"{name}\" adlı bir belge bulamadım. dosya_bul ile aranabilir.";
        }

        try
        {
            var text = DocumentText.Extract(path, 7000);
            ledger.Record(DataFlow.Local, "Belge hafızası", $"{Path.GetFileName(path)} okundu");
            return text.Trim().Length == 0
                ? $"{Path.GetFileName(path)} açıldı ama içinde okunabilir metin yok (taranmış resim olabilir)."
                : $"{Path.GetFileName(path)} ({path}):\n{text}{(text.Length >= 7000 ? "\n…(belgenin devamı kısaltıldı)" : "")}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            return $"{Path.GetFileName(path)} okunamadı: {exception.Message}";
        }
    }

    /// <summary>"dosya_kaydet": metni Kaydedilenler klasörüne .md olarak kaydeder; var olan dosyanın üstüne yazmaz.</summary>
    private string SaveDocument(string name, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Kaydedilecek bir metin yok.";
        }

        var folder = SaveFolder ?? Path.Combine(AppContext.BaseDirectory, "Kaydedilenler");
        Directory.CreateDirectory(folder);
        var safe = string.Concat((string.IsNullOrWhiteSpace(name) ? $"Friday {DateTime.Now:yyyy-MM-dd HH.mm}" : name.Trim())
            .Select(letter => Path.GetInvalidFileNameChars().Contains(letter) ? '-' : letter)).Trim();
        if (safe.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || safe.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            safe = Path.GetFileNameWithoutExtension(safe);
        }

        var path = Path.Combine(folder, safe + ".md");
        for (var copy = 2; File.Exists(path); copy++)
        {
            path = Path.Combine(folder, $"{safe} ({copy}).md");
        }

        File.WriteAllText(path, content.Trim() + "\n", new UTF8Encoding(false));
        ledger.Record(DataFlow.Local, "Dosya", $"{Path.GetFileName(path)} Kaydedilenler klasörüne kaydedildi");
        return $"Kaydedildi: {path}";
    }
}
