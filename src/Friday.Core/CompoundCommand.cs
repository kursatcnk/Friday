using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Birleşik bir komutun parçası: o parçanın tek başına söylenişi ve tanınan komut.</summary>
public sealed record CompoundPart(string Text, QuickCommand Command);

/// <summary>
/// Birleşik komutlar: "market listeme süt ekle, yarın 9'da dişçiyi aramamı hatırlat ve dolar kaç", "Spotify'ı açıp sesi
/// kıs", "Chrome'u ve Spotify'ı aç". Her parça tek başına bir hızlı komutsa parçalar sırayla, dil modeli beklenmeden
/// yapılır. Eskiden böyle bir cümle hızlı yola uymadığı için ajan moduna gidiyordu: ölçümde üç parçalı bir istekte ilk
/// cümle 23,6 sn sürdü, hatırlatma yerine iş eklendi, kur hiç sorulmadı.
/// Cümle bağlaçlardan ("ve", "sonra", "ardından", virgül, "-ıp/-ip" ulacı) atomlara bölünür; atomlar bitişik gruplara
/// her türlü ayrılarak (en fazla 6 atom, 32 bölünüş) her grubu tek başına tanınan en çok parçalı bölünüş seçilir.
/// Böylece "10 dakika sonra çayı hatırlat ve dolar kaç" içindeki "sonra" yanlışlıkla bölmez: o bölünüşün bir parçası
/// tanınmaz, "10 dakika sonra çayı hatırlat" bütün olarak tanınır.
/// </summary>
public static partial class CompoundCommand
{
    /// <summary>Bir istekte en fazla bu kadar komut.</summary>
    public const int MaxParts = 4;

    private const int MaxAtoms = 6;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Serbest metin alan komutlar: bütün cümle bunlardan birine uyuyorsa "ve" büyük olasılıkla metnin parçasıdır
    /// ("hatırlat bana süt ve ekmek al", "not al: toplantı perşembe ve cuma"); cümle bölünmez.
    /// </summary>
    private static readonly HashSet<string> FreeText = new(StringComparer.Ordinal)
    {
        "not", "hatirlatici", "kosullu_hatirlatici", "yaz", "kisaltma", "liste", "yapilacaklar", "hatirla", "eposta_taslagi",
        "internette_ara", "bilgi", "web_ara", "ozel_komut", "zamanlayici", "kontrol_listesi", "gunluk"
    };

    /// <summary>
    /// Cümle birden çok hızlı komutsa parçaları; değilse null. <paramref name="whole"/> bütün cümlenin tek komut olarak
    /// tanınışıdır: serbest metinli bir komutsa ona dokunulmaz, değilse (ör. "sesi kıs sonra müziği durdur" yalnızca
    /// "müziği durdur" diye tanınmış olabilir) birleşik okuma öne geçer.
    /// </summary>
    public static IReadOnlyList<CompoundPart>? Split(string text, QuickCommand? whole, Func<string, QuickCommand?> match)
    {
        if (whole is { Tentative: false } && FreeText.Contains(whole.Tool))
        {
            return null;
        }

        var atoms = Atoms(text);
        if (atoms.Count < 2 || atoms.Count > MaxAtoms)
        {
            return null;
        }

        // Paylaşılan fiil: "Chrome'u ve Spotify'ı aç" → "Chrome'u aç", "Spotify'ı aç".
        var verb = atoms[^1].Alone.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [_, .., var last] ? last : null;
        var cache = new Dictionary<string, CompoundPart?>(StringComparer.Ordinal);
        CompoundPart? Part(int from, int to)
        {
            var spoken = Join(atoms, from, to);
            if (!cache.TryGetValue(spoken, out var part))
            {
                part = Accept(spoken, match(spoken), whole is null);
                if (part is null && verb is not null && from == to && to < atoms.Count - 1 && spoken.Split(' ').Length <= 3)
                {
                    var completed = spoken + " " + verb;
                    part = Accept(completed, match(completed), whole is null);
                }

                cache[spoken] = part;
            }

            return part;
        }

        List<CompoundPart>? best = null;
        // Her ara bir sınır olabilir: bit i açıksa atom i ile i+1 arasında bölünür.
        for (var mask = 1; mask < 1 << (atoms.Count - 1); mask++)
        {
            var parts = new List<CompoundPart>();
            var start = 0;
            for (var index = 0; index < atoms.Count && parts is not null; index++)
            {
                if (index == atoms.Count - 1 || (mask & (1 << index)) != 0)
                {
                    if (Part(start, index) is { } part)
                    {
                        parts.Add(part);
                        start = index + 1;
                    }
                    else
                    {
                        parts = null;
                    }
                }
            }

            if (parts is { Count: >= 2 and <= MaxParts } && (best is null || parts.Count > best.Count))
            {
                best = parts;
            }
        }

        // "Dolar ve euro kaç" tek komut olarak zaten ikisini de veriyor: parçaların hepsi aynı araçsa bütün kalır.
        return best is not null && whole is { Tentative: false } && best.All(part => part.Command.Tool == whole.Tool) ? null : best;
    }

    /// <summary>
    /// Parça olarak kabul: sohbet sözü değil. "Spotify'ı aç" gibi kesin olmayan komutlar (uygulama bulunamazsa dil
    /// modeline gider) yalnızca bütün cümle hiçbir komuta uymadıysa kabul edilir; yapılamayan parça modele tek başına sorulur.
    /// </summary>
    private static CompoundPart? Accept(string spoken, QuickCommand? command, bool tentativeAllowed) =>
        command is not null && (tentativeAllowed || !command.Tentative) && command.Tool is not ("sohbet" or "serbest_cevap")
            ? new CompoundPart(spoken, command)
            : null;

    /// <summary>Atom: tek başına söylenişi (ulaç fiil haline getirilmiş) ve cümledeki asıl hali ile ardından gelen bağlaç.</summary>
    private sealed record Atom(string Alone, string Original, string Separator);

    private static List<Atom> Atoms(string text)
    {
        var atoms = new List<Atom>();
        var flat = string.Join(' ', text.Trim().TrimEnd('.', '!', '?').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var position = 0;
        foreach (Match boundary in Boundary().Matches(flat))
        {
            if (boundary.Groups["v"].Success)
            {
                // "açıp" → "aç", "ekleyip" → "ekle": parça tek başına emir kipiyle söylenir.
                var word = boundary.Groups["v"].Value;
                var stem = word[..^2];
                if (stem.Length >= 3 && stem[^1] == 'y' && "aeıioöuü".Contains(stem[^2], StringComparison.Ordinal))
                {
                    stem = stem[..^1];
                }

                var end = boundary.Groups["v"].Index + word.Length;
                var before = flat[position..boundary.Groups["v"].Index];
                atoms.Add(new Atom((before + stem).Trim(), flat[position..end].Trim(), flat[end..(boundary.Index + boundary.Length)]));
            }
            else
            {
                atoms.Add(new Atom(flat[position..boundary.Index].Trim(), flat[position..boundary.Index].Trim(), boundary.Value));
            }

            position = boundary.Index + boundary.Length;
        }

        atoms.Add(new Atom(flat[position..].Trim(), flat[position..].Trim(), ""));
        return atoms.Any(atom => atom.Alone.Length == 0) ? [] : atoms;
    }

    /// <summary>Bitişik atomları cümledeki asıl halleriyle birleştirir; son atom tek başına söylenişiyle biter.</summary>
    private static string Join(List<Atom> atoms, int from, int to)
    {
        var text = "";
        for (var index = from; index < to; index++)
        {
            text += atoms[index].Original + atoms[index].Separator;
        }

        return text + atoms[to].Alone;
    }

    /// <summary>Bağlaçlar ve "-ıp/-ip/-up/-üp" ulaçları (ardından boşluk ve devam eden bir söz gelir).</summary>
    [GeneratedRegex(@"\s*[,;]\s*(?:(?:ve|sonra|ardından|ayrıca|bir de)\s+)?|\s+(?:ve(?:\s+(?:sonra|ardından))?|sonra|ardından|ayrıca|bir de)\s+|(?<=\s|^)(?<v>\p{L}{2,}(?:ıp|ip|up|üp))\s+", RegexOptions.IgnoreCase)]
    private static partial Regex Boundary();
}
