namespace Friday.Core;

/// <summary>Taşınacak bir dosya: nereden, hangi klasöre.</summary>
public sealed record FileMove(string From, string To);

/// <summary>
/// "İndirilenleri düzenle": İndirilenler klasörünün üst düzeyindeki dosyaları türlerine göre alt klasörlere ayıran plan.
/// Yarım indirmeler, son iki dakikada değişen dosyalar ve tanınmayan türler yerinde kalır. Plan önce gösterilir,
/// onaylanınca uygulanır ve geri alınabilir.
/// </summary>
public static class DownloadSorter
{
    private static readonly Dictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "Belgeler", [".doc"] = "Belgeler", [".docx"] = "Belgeler", [".xls"] = "Belgeler", [".xlsx"] = "Belgeler",
        [".ppt"] = "Belgeler", [".pptx"] = "Belgeler", [".txt"] = "Belgeler", [".md"] = "Belgeler", [".csv"] = "Belgeler",
        [".odt"] = "Belgeler", [".rtf"] = "Belgeler", [".epub"] = "Belgeler",
        [".jpg"] = "Resimler", [".jpeg"] = "Resimler", [".png"] = "Resimler", [".gif"] = "Resimler", [".webp"] = "Resimler",
        [".heic"] = "Resimler", [".bmp"] = "Resimler", [".svg"] = "Resimler",
        [".mp4"] = "Videolar", [".mkv"] = "Videolar", [".mov"] = "Videolar", [".avi"] = "Videolar", [".webm"] = "Videolar",
        [".mp3"] = "Müzik", [".wav"] = "Müzik", [".flac"] = "Müzik", [".m4a"] = "Müzik", [".ogg"] = "Müzik",
        [".zip"] = "Arşivler", [".rar"] = "Arşivler", [".7z"] = "Arşivler", [".tar"] = "Arşivler", [".gz"] = "Arşivler",
        [".exe"] = "Kurulumlar", [".msi"] = "Kurulumlar", [".msix"] = "Kurulumlar", [".appx"] = "Kurulumlar",
    };

    /// <summary>Klasör adları; yalnızca bunlar ayrıştırılır.</summary>
    public static IReadOnlyCollection<string> Folders => [.. Categories.Values.Distinct()];

    public static string? CategoryOf(string file) => Categories.GetValueOrDefault(Path.GetExtension(file));

    /// <param name="files">Klasördeki dosyalar ve son değişme zamanları.</param>
    public static IReadOnlyList<FileMove> Plan(string folder, IEnumerable<(string Path, DateTime Modified)> files, DateTime now)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moves = new List<FileMove>();
        foreach (var (path, modified) in files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (DownloadFiles.IsPartial(path) || now - modified < TimeSpan.FromMinutes(2) || CategoryOf(path) is not { } category)
            {
                continue;
            }

            var target = Path.Combine(folder, category, Path.GetFileName(path));
            var counter = 2;
            while (taken.Contains(target) || File.Exists(target))
            {
                target = Path.Combine(folder, category, $"{Path.GetFileNameWithoutExtension(path)} ({counter++}){Path.GetExtension(path)}");
            }

            taken.Add(target);
            moves.Add(new FileMove(path, target));
        }

        return moves;
    }

    /// <summary>"52 belge, 40 resim ve 12 video".</summary>
    public static string Summarize(IReadOnlyList<FileMove> moves)
    {
        var parts = moves
            .GroupBy(move => Path.GetFileName(Path.GetDirectoryName(move.To)) ?? "")
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Count()} {Singular(group.Key)}")
            .ToList();
        return TurkishGrammar.Join(parts);
    }

    private static string Singular(string folder) => folder switch
    {
        "Belgeler" => "belge",
        "Resimler" => "resim",
        "Videolar" => "video",
        "Müzik" => "müzik dosyası",
        "Arşivler" => "arşiv",
        "Kurulumlar" => "kurulum dosyası",
        _ => "dosya"
    };
}
