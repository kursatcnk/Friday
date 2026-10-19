using System.Text;

namespace Friday.Core;

/// <summary>
/// Dil modelinden parça parça gelen metni seslendirilecek cümlelere böler. İlk cümle hazır olur olmaz
/// seslendirmeye gider; cevabın tamamını beklemek gerekmez.
/// </summary>
public sealed class SentenceChunker
{
    private const int MinimumLength = 12;

    private readonly StringBuilder _buffer = new();

    /// <summary>Yeni metni ekler, tamamlanan cümleleri döndürür.</summary>
    public IReadOnlyList<string> Push(string text)
    {
        _buffer.Append(text);
        var sentences = new List<string>();
        var start = 0;
        var current = _buffer.ToString();

        for (var index = 0; index < current.Length; index++)
        {
            if (!IsBoundary(current, index))
            {
                continue;
            }

            var candidate = current[start..(index + 1)].Trim();
            if (candidate.Length >= MinimumLength || current[index] == '\n')
            {
                if (candidate.Length > 0)
                {
                    sentences.Add(candidate);
                }

                start = index + 1;
            }
        }

        _buffer.Remove(0, start);
        return sentences;
    }

    /// <summary>Akış bittiğinde kalan metni verir.</summary>
    public string? Flush()
    {
        var rest = _buffer.ToString().Trim();
        _buffer.Clear();
        return rest.Length > 0 ? rest : null;
    }

    private static bool IsBoundary(string text, int index)
    {
        var character = text[index];
        if (character == '\n')
        {
            return true;
        }

        if (character is not ('.' or '!' or '?' or '…' or ':' or ';'))
        {
            return false;
        }

        // Noktalamadan sonra boşluk gelmeli; "3.5" ya da "www.site.com" bölünmez. Metnin sonundaysa
        // devamının ne geleceği henüz bilinmediği için beklenir.
        return index + 1 < text.Length && char.IsWhiteSpace(text[index + 1]);
    }
}
