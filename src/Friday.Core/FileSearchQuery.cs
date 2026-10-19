using System.Globalization;
using System.Text;

namespace Friday.Core;

/// <summary>
/// "Dün indirdiğim PDF" gibi bir isteği Windows Arama dizininin SQL sorgusuna çevirir. Dizin Windows'un kendi
/// servisidir; Friday dosyaları ayrıca taramaz.
/// </summary>
public static class FileSearchQuery
{
    private static readonly Dictionary<string, string[]> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = [".pdf"],
        ["word"] = [".docx", ".doc", ".odt", ".rtf"],
        ["excel"] = [".xlsx", ".xls", ".csv", ".ods"],
        ["sunum"] = [".pptx", ".ppt", ".odp"],
        ["resim"] = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".bmp"],
        ["video"] = [".mp4", ".mkv", ".mov", ".avi", ".webm"],
        ["muzik"] = [".mp3", ".flac", ".wav", ".m4a", ".ogg"],
        ["arsiv"] = [".zip", ".rar", ".7z"],
        ["kod"] = [".cs", ".py", ".js", ".ts", ".json", ".xml", ".sql", ".md"],
        ["kurulum"] = [".exe", ".msi"],
    };

    public static IReadOnlyCollection<string> Kinds => Extensions.Keys;

    /// <param name="text">Dosya adında ya da içeriğinde geçen sözcükler; boş olabilir.</param>
    /// <param name="kind">pdf, word, excel… ya da boş.</param>
    /// <param name="days">Son kaç gün içinde değişmiş; 0 sınırsız.</param>
    /// <param name="folder">Arama yapılacak klasör ya da boş (tüm dizin).</param>
    public static string Build(string text, string kind, int days, string folder, DateTime now, int limit = 8)
    {
        var conditions = new List<string> { "System.FileAttributes <> ALL BITWISE 16" };
        if (!string.IsNullOrWhiteSpace(folder))
        {
            conditions.Add($"SCOPE='file:{Escape(folder.Replace('\\', '/'))}'");
        }

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Sorgu dilini bozacak karakterler atılır; kalan sözcük ad ya da içerikte aranır.
            var clean = new string(word.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.').ToArray());
            if (clean.Length < 2)
            {
                continue;
            }

            var escaped = Escape(clean);
            conditions.Add($"(System.FileName LIKE '%{escaped}%' OR CONTAINS(System.Search.Contents, '\"{escaped}*\"'))");
        }

        if (Extensions.TryGetValue(Simplify(kind), out var extensions))
        {
            conditions.Add("(" + string.Join(" OR ", extensions.Select(extension => $"System.FileExtension = '{extension}'")) + ")");
        }

        if (days > 0)
        {
            var since = now.Date.AddDays(-days).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            conditions.Add($"System.DateModified >= '{since}'");
        }

        var sql = new StringBuilder();
        sql.Append(CultureInfo.InvariantCulture, $"SELECT TOP {limit} System.ItemPathDisplay, System.ItemName, System.DateModified, System.Size FROM SYSTEMINDEX WHERE ");
        sql.Append(string.Join(" AND ", conditions));
        sql.Append(" ORDER BY System.DateModified DESC");
        return sql.ToString();
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string Simplify(string kind) => kind.Trim().ToLowerInvariant()
        .Replace('ü', 'u').Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g').Replace('ö', 'o').Replace('ç', 'c');
}
