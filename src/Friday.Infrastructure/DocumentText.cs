using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Friday.Infrastructure;

/// <summary>
/// Belgelerden düz metin çıkarır: PDF, Word (.docx), PowerPoint (.pptx), Excel (.xlsx), metin, Markdown, CSV, HTML,
/// RTF. Hiçbir ek kütüphane ya da indirme gerekmez; Office dosyaları zip içindeki XML'den, PDF kendi küçük
/// okuyucusundan okunur. Okunamayan dosya boş metin döndürür.
/// </summary>
public static partial class DocumentText
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".pptx", ".xlsx", ".txt", ".md", ".csv", ".log", ".html", ".htm", ".rtf", ".json", ".xml"
    };

    static DocumentText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Extract(string path, int maxCharacters = 150_000)
    {
        var text = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => PdfText.Extract(File.ReadAllBytes(path), maxCharacters),
            ".docx" => OfficeXml(path, entry => entry == "word/document.xml", "t", "p", "tab", "br"),
            ".pptx" => OfficeXml(path, entry => entry.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && entry.EndsWith(".xml", StringComparison.Ordinal), "t", "p", "tab", "br"),
            ".xlsx" => OfficeXml(path, entry => entry == "xl/sharedStrings.xml", "t", "si", "", ""),
            ".html" or ".htm" => Html(ReadText(path)),
            ".rtf" => Rtf(File.ReadAllText(path, Encoding.Latin1)),
            _ => ReadText(path)
        };
        return text.Length > maxCharacters ? text[..maxCharacters] : text;
    }

    /// <summary>UTF-8 (BOM'lu ya da BOM'suz) değilse Türkçe Windows kod sayfası (1254) olarak okunur.</summary>
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1254).GetString(bytes);
        }
    }

    /// <summary>Office Open XML: metin öğeleri, paragraf sonları satır, sekme boşluk olur.</summary>
    private static string OfficeXml(string path, Func<string, bool> wanted, string text, string paragraph, string tab, string lineBreak)
    {
        using var zip = ZipFile.OpenRead(path);
        var builder = new StringBuilder();
        var entries = zip.Entries.Where(entry => wanted(entry.FullName))
            .OrderBy(entry => int.TryParse(DigitsRegex().Match(entry.Name).Value, out var number) ? number : 0);
        foreach (var entry in entries)
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = false });
            var inText = false;
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element when reader.LocalName == text:
                        inText = !reader.IsEmptyElement;
                        break;
                    case XmlNodeType.EndElement when reader.LocalName == text:
                        inText = false;
                        break;
                    case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace when inText:
                        builder.Append(reader.Value);
                        break;
                    case XmlNodeType.Element when reader.LocalName.Length > 0 && (reader.LocalName == tab || reader.LocalName == lineBreak):
                        builder.Append(' ');
                        break;
                    case XmlNodeType.EndElement when reader.LocalName == paragraph:
                        builder.Append('\n');
                        break;
                }
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string Html(string html)
    {
        var text = ScriptRegex().Replace(html, " ");
        text = BlockTagRegex().Replace(text, "\n");
        text = TagRegex().Replace(text, " ");
        return WebUtility.HtmlDecode(text);
    }

    /// <summary>Kaba RTF temizliği: denetim sözcükleri ve gruplar atılır, \'xx kodları Türkçe kod sayfasıyla çözülür.</summary>
    private static string Rtf(string rtf)
    {
        var turkish = Encoding.GetEncoding(1254);
        var text = RtfHexRegex().Replace(rtf, match => turkish.GetString([byte.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)]));
        text = RtfIgnorableRegex().Replace(text, "");
        text = text.Replace("\\par", "\n", StringComparison.Ordinal);
        text = RtfControlRegex().Replace(text, "");
        return text.Replace("{", "", StringComparison.Ordinal).Replace("}", "", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsRegex();

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"</?(p|div|br|li|tr|h[1-6])[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\\'([0-9a-fA-F]{2})")]
    private static partial Regex RtfHexRegex();

    [GeneratedRegex(@"\{\\\*[^{}]*\}")]
    private static partial Regex RtfIgnorableRegex();

    [GeneratedRegex(@"\\[a-zA-Z]+-?\d* ?")]
    private static partial Regex RtfControlRegex();
}

/// <summary>
/// Küçük bir PDF metin okuyucusu: nesneleri (sıkıştırılmış nesne akışları dahil) okur, sayfa ağacını dolaşır, her
/// sayfanın yazı tiplerinin ToUnicode tablolarıyla içerik akışındaki metin komutlarını (Tj, TJ, ', ") çözer. Word,
/// tarayıcı ve LibreOffice'in ürettiği metin PDF'lerinde çalışır; taranmış (resim) PDF'lerde metin yoktur.
/// </summary>
public static partial class PdfText
{
    private sealed record PdfObject(string Dictionary, byte[]? Stream);

    private sealed class FontMap
    {
        public int CodeBytes { get; set; } = 1;

        public Dictionary<int, string> Map { get; } = [];
    }

    public static string Extract(byte[] data, int maxCharacters = 150_000)
    {
        var objects = ReadObjects(data);
        var builder = new StringBuilder();
        foreach (var (page, resources) in Pages(objects))
        {
            var fonts = Fonts(objects, resources);
            foreach (var content in Contents(objects, page))
            {
                ReadContent(content, fonts, builder);
                if (builder.Length > maxCharacters)
                {
                    return builder.ToString();
                }
            }

            builder.Append('\n');
        }

        return MultiSpaceRegex().Replace(builder.ToString(), " ");
    }

    // ---------------------------------------------------------------- nesneler

    private static Dictionary<int, PdfObject> ReadObjects(byte[] data)
    {
        var text = Encoding.Latin1.GetString(data);
        var objects = new Dictionary<int, PdfObject>();
        foreach (Match match in ObjectRegex().Matches(text))
        {
            var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var start = match.Index + match.Length;
            var end = text.IndexOf("endobj", start, StringComparison.Ordinal);
            if (end < 0)
            {
                continue;
            }

            var body = text[start..end];
            byte[]? stream = null;
            var streamAt = body.IndexOf("stream", StringComparison.Ordinal);
            var dictionary = body;
            if (streamAt >= 0 && body.IndexOf("endstream", streamAt, StringComparison.Ordinal) is var streamEnd and > 0)
            {
                dictionary = body[..streamAt];
                var dataStart = streamAt + 6;
                if (dataStart < body.Length && body[dataStart] == '\r')
                {
                    dataStart++;
                }

                if (dataStart < body.Length && body[dataStart] == '\n')
                {
                    dataStart++;
                }

                var raw = Encoding.Latin1.GetBytes(body[dataStart..streamEnd]);
                stream = Decode(dictionary, raw);
            }

            objects[number] = new PdfObject(dictionary, stream);
        }

        // PDF 1.5 nesne akışları: içlerindeki nesneler ayrıca açılır.
        foreach (var holder in objects.Values.Where(item => item.Stream is not null && item.Dictionary.Contains("/ObjStm", StringComparison.Ordinal)).ToList())
        {
            var content = Encoding.Latin1.GetString(holder.Stream!);
            if (Number(holder.Dictionary, "/N") is not { } count || Number(holder.Dictionary, "/First") is not { } first || first > content.Length)
            {
                continue;
            }

            var header = content[..first].Split((char[])[' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index + 1 < header.Length && index / 2 < count; index += 2)
            {
                var number = int.Parse(header[index], CultureInfo.InvariantCulture);
                var offset = first + int.Parse(header[index + 1], CultureInfo.InvariantCulture);
                var next = index + 3 < header.Length ? first + int.Parse(header[index + 3], CultureInfo.InvariantCulture) : content.Length;
                if (offset <= content.Length && next <= content.Length && next > offset && !objects.ContainsKey(number))
                {
                    objects[number] = new PdfObject(content[offset..next], null);
                }
            }
        }

        return objects;
    }

    private static byte[] Decode(string dictionary, byte[] raw)
    {
        if (!dictionary.Contains("/FlateDecode", StringComparison.Ordinal))
        {
            return raw;
        }

        try
        {
            using var input = new MemoryStream(raw);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    // ---------------------------------------------------------------- sayfalar

    /// <summary>Katalogdan başlayıp sayfa ağacını sırayla dolaşır; kaynaklar üst düğümden miras alınır.</summary>
    private static List<(PdfObject Page, string Resources)> Pages(Dictionary<int, PdfObject> objects)
    {
        var pages = new List<(PdfObject, string)>();
        var root = objects.Values.FirstOrDefault(item => TypeRegex().Match(item.Dictionary) is { Success: true } type && type.Groups[1].Value == "Catalog");
        var visited = new HashSet<int>();

        void Walk(int number, string inherited)
        {
            if (!visited.Add(number) || !objects.TryGetValue(number, out var node))
            {
                return;
            }

            var resources = Value(node.Dictionary, "/Resources") ?? inherited;
            var type = TypeRegex().Match(node.Dictionary) is { Success: true } match ? match.Groups[1].Value : "";
            if (type == "Page")
            {
                pages.Add((node, resources));
                return;
            }

            if (Value(node.Dictionary, "/Kids") is { } kids)
            {
                foreach (Match kid in ReferenceRegex().Matches(kids))
                {
                    Walk(int.Parse(kid.Groups[1].Value, CultureInfo.InvariantCulture), resources);
                }
            }
        }

        if (root is not null && Value(root.Dictionary, "/Pages") is { } pagesRef && ReferenceRegex().Match(pagesRef) is { Success: true } first)
        {
            Walk(int.Parse(first.Groups[1].Value, CultureInfo.InvariantCulture), "");
        }

        if (pages.Count == 0)
        {
            // Bozuk ya da alışılmadık ağaç: sayfalar nesne sırasıyla alınır.
            foreach (var item in objects.OrderBy(pair => pair.Key).Select(pair => pair.Value))
            {
                if (TypeRegex().Match(item.Dictionary) is { Success: true } type && type.Groups[1].Value == "Page")
                {
                    pages.Add((item, Value(item.Dictionary, "/Resources") ?? ""));
                }
            }
        }

        return pages;
    }

    private static IEnumerable<byte[]> Contents(Dictionary<int, PdfObject> objects, PdfObject page)
    {
        if (Value(page.Dictionary, "/Contents") is not { } contents)
        {
            yield break;
        }

        foreach (Match reference in ReferenceRegex().Matches(contents))
        {
            if (objects.TryGetValue(int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture), out var item))
            {
                if (item.Stream is { } stream)
                {
                    yield return stream;
                }
                else if (item.Dictionary.TrimStart().StartsWith('['))
                {
                    // İçerik dizisi ayrı bir nesnede.
                    foreach (Match inner in ReferenceRegex().Matches(item.Dictionary))
                    {
                        if (objects.TryGetValue(int.Parse(inner.Groups[1].Value, CultureInfo.InvariantCulture), out var part) && part.Stream is { } data)
                        {
                            yield return data;
                        }
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- yazı tipleri

    private static Dictionary<string, FontMap> Fonts(Dictionary<int, PdfObject> objects, string resources)
    {
        var fonts = new Dictionary<string, FontMap>(StringComparer.Ordinal);
        var resourceDictionary = Resolve(objects, resources);
        if (Value(resourceDictionary, "/Font") is not { } fontValue)
        {
            return fonts;
        }

        foreach (Match entry in FontEntryRegex().Matches(Resolve(objects, fontValue)))
        {
            var map = new FontMap();
            if (objects.TryGetValue(int.Parse(entry.Groups[2].Value, CultureInfo.InvariantCulture), out var font))
            {
                if (Value(font.Dictionary, "/ToUnicode") is { } unicode && ReferenceRegex().Match(unicode) is { Success: true } reference
                    && objects.TryGetValue(int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture), out var cmap) && cmap.Stream is { } cmapData)
                {
                    ReadCMap(Encoding.Latin1.GetString(cmapData), map);
                }
                else if (font.Dictionary.Contains("/Type0", StringComparison.Ordinal))
                {
                    map.CodeBytes = 2;
                }
            }

            fonts[entry.Groups[1].Value] = map;
        }

        return fonts;
    }

    private static void ReadCMap(string cmap, FontMap map)
    {
        if (CodeSpaceRegex().Match(cmap) is { Success: true } space)
        {
            map.CodeBytes = Math.Max(1, space.Groups[1].Value.Length / 2);
        }

        foreach (Match block in BfCharBlockRegex().Matches(cmap))
        {
            foreach (Match pair in HexPairRegex().Matches(block.Groups[1].Value))
            {
                map.Map[Hex(pair.Groups[1].Value)] = Utf16(pair.Groups[2].Value);
            }
        }

        foreach (Match block in BfRangeBlockRegex().Matches(cmap))
        {
            foreach (Match range in BfRangeRegex().Matches(block.Groups[1].Value))
            {
                var low = Hex(range.Groups[1].Value);
                var high = Hex(range.Groups[2].Value);
                if (high - low > 5000)
                {
                    continue;
                }

                if (range.Groups[3].Success)
                {
                    var start = Utf16(range.Groups[3].Value);
                    for (var code = low; code <= high && start.Length > 0; code++)
                    {
                        map.Map[code] = start[..^1] + (char)(start[^1] + (code - low));
                    }
                }
                else
                {
                    var targets = HexStringRegex().Matches(range.Groups[4].Value);
                    for (var index = 0; index < targets.Count && low + index <= high; index++)
                    {
                        map.Map[low + index] = Utf16(targets[index].Groups[1].Value);
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- içerik akışı

    private static void ReadContent(byte[] content, Dictionary<string, FontMap> fonts, StringBuilder output)
    {
        var operands = new List<object>();
        FontMap? font = null;
        var position = 0;
        while (position < content.Length)
        {
            var current = content[position];
            if (IsSpace(current))
            {
                position++;
            }
            else if (current == '%')
            {
                while (position < content.Length && content[position] is not ((byte)'\n' or (byte)'\r'))
                {
                    position++;
                }
            }
            else if (current == '(')
            {
                operands.Add(LiteralString(content, ref position));
            }
            else if (current == '<' && position + 1 < content.Length && content[position + 1] == '<')
            {
                position += 2;
            }
            else if (current == '>' && position + 1 < content.Length && content[position + 1] == '>')
            {
                position += 2;
            }
            else if (current == '<')
            {
                var end = Array.IndexOf(content, (byte)'>', position);
                end = end < 0 ? content.Length : end;
                operands.Add(HexBytes(Encoding.Latin1.GetString(content, position + 1, end - position - 1)));
                position = end + 1;
            }
            else if (current == '[')
            {
                operands.Add('[');
                position++;
            }
            else if (current == ']')
            {
                var start = operands.LastIndexOf('[');
                var array = start < 0 ? [] : operands.Skip(start + 1).ToList();
                if (start >= 0)
                {
                    operands.RemoveRange(start, operands.Count - start);
                }

                operands.Add(array);
                position++;
            }
            else if (current == '/')
            {
                var start = ++position;
                while (position < content.Length && !IsSpace(content[position]) && !IsDelimiter(content[position]))
                {
                    position++;
                }

                operands.Add("/" + Encoding.Latin1.GetString(content, start, position - start));
            }
            else if (current is (byte)'-' or (byte)'+' or (byte)'.' || char.IsAsciiDigit((char)current))
            {
                var start = position++;
                while (position < content.Length && (char.IsAsciiDigit((char)content[position]) || content[position] == '.'))
                {
                    position++;
                }

                operands.Add(double.TryParse(Encoding.Latin1.GetString(content, start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0d);
            }
            else
            {
                var start = position;
                while (position < content.Length && !IsSpace(content[position]) && !IsDelimiter(content[position]))
                {
                    position++;
                }

                if (position == start)
                {
                    position++;
                    continue;
                }

                var op = Encoding.Latin1.GetString(content, start, position - start);
                switch (op)
                {
                    case "Tf" when operands.Count >= 2 && operands[^2] is string name:
                        font = fonts.GetValueOrDefault(name.TrimStart('/'));
                        break;
                    case "Tj" when operands.Count >= 1 && operands[^1] is byte[] bytes:
                        output.Append(Decode(bytes, font));
                        break;
                    case "'" or "\"" when operands.Count >= 1 && operands[^1] is byte[] bytes:
                        output.Append('\n').Append(Decode(bytes, font));
                        break;
                    case "TJ" when operands.Count >= 1 && operands[^1] is List<object> array:
                        foreach (var item in array)
                        {
                            if (item is byte[] part)
                            {
                                output.Append(Decode(part, font));
                            }
                            else if (item is double gap && gap < -180)
                            {
                                output.Append(' ');
                            }
                        }

                        break;
                    case "Td" or "TD" when operands.Count >= 2 && operands[^1] is double y:
                        // Yatay kayma çoğunlukla harf aralığı ya da bir sonraki parçanın yeridir; boşluklar metnin içinde
                        // ayrı karakter olarak gelir. Yalnızca dikey kayma yeni satırdır.
                        if (Math.Abs(y) > 0.5)
                        {
                            output.Append('\n');
                        }

                        break;
                    case "T*":
                        output.Append('\n');
                        break;
                    case "Tm" or "ET":
                        output.Append(' ');
                        break;
                    case "BI":
                        // Satır içi resim: "ID" ile "EI" arasındaki ikili veri atlanır.
                        var imageEnd = IndexOf(content, "EI", position);
                        position = imageEnd < 0 ? content.Length : imageEnd + 2;
                        break;
                }

                operands.Clear();
            }
        }
    }

    private static string Decode(byte[] bytes, FontMap? font)
    {
        var builder = new StringBuilder();
        var width = font?.CodeBytes ?? 1;
        for (var index = 0; index + width <= bytes.Length; index += width)
        {
            var code = width == 2 ? (bytes[index] << 8) | bytes[index + 1] : bytes[index];
            if (font is not null && font.Map.TryGetValue(code, out var mapped))
            {
                builder.Append(mapped);
            }
            else if (width == 1)
            {
                builder.Append((char)code);
            }
        }

        return builder.ToString();
    }

    private static byte[] LiteralString(byte[] content, ref int position)
    {
        var output = new List<byte>();
        var depth = 0;
        position++;
        while (position < content.Length)
        {
            var current = content[position++];
            if (current == '\\' && position < content.Length)
            {
                var escaped = content[position++];
                switch (escaped)
                {
                    case (byte)'n': output.Add((byte)'\n'); break;
                    case (byte)'r': output.Add((byte)'\r'); break;
                    case (byte)'t': output.Add((byte)'\t'); break;
                    case (byte)'b' or (byte)'f': break;
                    case (byte)'\r' or (byte)'\n': break;
                    case >= (byte)'0' and <= (byte)'7':
                        var value = escaped - '0';
                        for (var digits = 1; digits < 3 && position < content.Length && content[position] is >= (byte)'0' and <= (byte)'7'; digits++)
                        {
                            value = (value * 8) + (content[position++] - '0');
                        }

                        output.Add((byte)value);
                        break;
                    default: output.Add(escaped); break;
                }
            }
            else if (current == '(')
            {
                depth++;
                output.Add(current);
            }
            else if (current == ')')
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
                output.Add(current);
            }
            else
            {
                output.Add(current);
            }
        }

        return [.. output];
    }

    // ---------------------------------------------------------------- yardımcılar

    /// <summary>Sözlükteki bir anahtarın ham değeri: iç içe sözlük, dizi ya da "12 0 R" gibi tek değer.</summary>
    private static string? Value(string dictionary, string key)
    {
        var at = 0;
        while ((at = dictionary.IndexOf(key, at, StringComparison.Ordinal)) >= 0)
        {
            var next = at + key.Length;
            if (next < dictionary.Length && (char.IsLetterOrDigit(dictionary[next]) || dictionary[next] == '_'))
            {
                at = next;
                continue;
            }

            while (next < dictionary.Length && char.IsWhiteSpace(dictionary[next]))
            {
                next++;
            }

            if (next >= dictionary.Length)
            {
                return null;
            }

            if (dictionary[next] == '[' || (dictionary[next] == '<' && next + 1 < dictionary.Length && dictionary[next + 1] == '<'))
            {
                var (open, close) = dictionary[next] == '[' ? ("[", "]") : ("<<", ">>");
                var depth = 0;
                for (var index = next; index < dictionary.Length; index++)
                {
                    if (string.CompareOrdinal(dictionary, index, open, 0, open.Length) == 0)
                    {
                        depth++;
                        index += open.Length - 1;
                    }
                    else if (string.CompareOrdinal(dictionary, index, close, 0, close.Length) == 0)
                    {
                        depth--;
                        index += close.Length - 1;
                        if (depth == 0)
                        {
                            return dictionary[next..(index + 1)];
                        }
                    }
                }

                return dictionary[next..];
            }

            var match = SimpleValueRegex().Match(dictionary, next);
            return match.Success ? match.Value : null;
        }

        return null;
    }

    /// <summary>"12 0 R" ise o nesnenin sözlüğü, değilse kendisi.</summary>
    private static string Resolve(Dictionary<int, PdfObject> objects, string value) =>
        ReferenceRegex().Match(value) is { Success: true } reference && value.TrimStart().StartsWith(reference.Value, StringComparison.Ordinal)
        && objects.TryGetValue(int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture), out var item)
            ? item.Dictionary
            : value;

    private static int? Number(string dictionary, string key) =>
        Value(dictionary, key) is { } value && int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static int Hex(string hex) => int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string Utf16(string hex)
    {
        var bytes = HexBytes(hex);
        return bytes.Length % 2 == 0 ? Encoding.BigEndianUnicode.GetString(bytes) : Encoding.Latin1.GetString(bytes);
    }

    private static byte[] HexBytes(string hex)
    {
        var digits = new string(hex.Where(Uri.IsHexDigit).ToArray());
        if (digits.Length % 2 == 1)
        {
            digits += "0";
        }

        return Convert.FromHexString(digits);
    }

    private static int IndexOf(byte[] data, string token, int start)
    {
        var bytes = Encoding.ASCII.GetBytes(token);
        for (var index = start; index <= data.Length - bytes.Length; index++)
        {
            if (data.AsSpan(index, bytes.Length).SequenceEqual(bytes) && (index == 0 || IsSpace(data[index - 1])))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSpace(byte value) => value is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or (byte)'\f' or 0;

    private static bool IsDelimiter(byte value) => value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'/' or (byte)'%';

    [GeneratedRegex(@"(?<![\d])(\d+)\s+\d+\s+obj\b")]
    private static partial Regex ObjectRegex();

    [GeneratedRegex(@"(\d+)\s+\d+\s+R\b")]
    private static partial Regex ReferenceRegex();

    [GeneratedRegex(@"/Type\s*/(\w+)")]
    private static partial Regex TypeRegex();

    [GeneratedRegex(@"/([^\s/<>\[\]()]+)\s+(\d+)\s+\d+\s+R")]
    private static partial Regex FontEntryRegex();

    [GeneratedRegex(@"begincodespacerange\s*<([0-9A-Fa-f]+)>")]
    private static partial Regex CodeSpaceRegex();

    [GeneratedRegex(@"beginbfchar(.*?)endbfchar", RegexOptions.Singleline)]
    private static partial Regex BfCharBlockRegex();

    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]*)>")]
    private static partial Regex HexPairRegex();

    [GeneratedRegex(@"beginbfrange(.*?)endbfrange", RegexOptions.Singleline)]
    private static partial Regex BfRangeBlockRegex();

    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(?:<([0-9A-Fa-f]+)>|(\[[^\]]*\]))")]
    private static partial Regex BfRangeRegex();

    [GeneratedRegex(@"<([0-9A-Fa-f]+)>")]
    private static partial Regex HexStringRegex();

    [GeneratedRegex(@"\d+\s+\d+\s+R|/[^\s/<>\[\]()]+|[-+]?\d+(?:\.\d+)?|true|false|null")]
    private static partial Regex SimpleValueRegex();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpaceRegex();
}
