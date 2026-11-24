using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// "Bu ne?", "bunu Türkçeye çevir", "bu sözleşmede depozito ne kadar?": önündeki belgeye dair istekler. Friday çağrıldığı
/// anda öndeki pencerenin başlığından ("sözleşme.pdf - Adobe Acrobat Reader") dosya adını çıkarır; dosya bulunursa
/// metnin tamamı okunur (ekran görüntüsünden değil), bulunamazsa ekrandaki yazı okunur. Uzun belgeler parça parça
/// çevrilir ("devamını çevir").
/// </summary>
public static partial class FrontDocument
{
    /// <summary>Çeviride bir seferde ele alınan en fazla karakter (yaklaşık bir sayfa).</summary>
    public const int ChunkLength = 1800;

    /// <summary>Pencere başlığındaki belge dosyasının adı ("rapor.pdf"); yoksa null.</summary>
    public static string? FileName(string title)
    {
        // Başlık " - " ile bölünür ("rental-agreement.pdf - Adobe Acrobat Reader"); dosya adındaki tire bölünmez.
        foreach (var part in title.Split([" - ", " – ", " — ", " | "], StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Trim();
            name = name[(name.LastIndexOfAny(['\\', '/']) + 1)..];
            if (DocumentName().IsMatch(name))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>Uzun metnin <paramref name="offset"/>'ten başlayan, cümle sonunda biten parçası ve sonraki başlangıç.</summary>
    public static (string Chunk, int Next) Chunk(string text, int offset, int length = ChunkLength)
    {
        if (offset >= text.Length)
        {
            return ("", text.Length);
        }

        var end = Math.Min(text.Length, offset + length);
        if (end < text.Length)
        {
            // Cümle ortasında kesmemek için son cümle sonuna (yoksa satır sonuna) geri gelinir.
            var stop = text.LastIndexOfAny(['.', '!', '?', '\n'], end - 1, end - offset);
            if (stop > offset + (length / 2))
            {
                end = stop + 1;
            }
        }

        return (text[offset..end].Trim(), end);
    }

    /// <summary>
    /// Metin Türkçe mi? (Türkçeye özgü harfler ve sık sözcükler). Dil modeline "Türkçeyse çevirme" demek İngilizce bir
    /// sözleşmeye "Bu metin zaten Türkçe" dedirtiyordu; karar burada verilir.
    /// </summary>
    public static bool LooksTurkish(string text)
    {
        var sample = text.Length > 3000 ? text[..3000] : text;
        var letters = sample.Count(char.IsLetter);
        var turkish = sample.Count(letter => "çğıöşüÇĞİÖŞÜ".Contains(letter));
        var words = sample.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var english = words.Count(word => word is "the" or "and" or "of" or "to" or "is" or "for" or "with" or "that" or "this");
        return letters > 0 && (turkish * 100 / letters >= 2 || words.Count(word => word is "ve" or "bir" or "bu" or "için" or "ile") > english);
    }

    /// <summary>Soru için belgenin en ilgili parçaları (sözcük örtüşmesiyle), belgedeki sırasıyla; kısa belge bütün döner.</summary>
    public static string Relevant(string text, string question, int budget = 6000)
    {
        if (text.Length <= budget)
        {
            return text;
        }

        var asked = MemoryRecall.Words(question);
        var pieces = new List<(int Index, string Text, int Score)>();
        for (var offset = 0; offset < text.Length;)
        {
            var (chunk, next) = Chunk(text, offset, 1200);
            var words = MemoryRecall.Words(chunk);
            pieces.Add((pieces.Count, chunk, asked.Count(word => words.Any(other => MemoryRecall.Same(word, other)))));
            offset = next;
        }

        var chosen = pieces.OrderByDescending(piece => piece.Score).ThenBy(piece => piece.Index)
            .Take(Math.Max(1, budget / 1200)).OrderBy(piece => piece.Index);
        return string.Join("\n…\n", chosen.Select(piece => piece.Text));
    }

    [GeneratedRegex(@"^[^\\/:*?""<>|]+\.(?:pdf|docx?|pptx?|xlsx?|txt|md|rtf|odt|csv|html?)$", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentName();
}
