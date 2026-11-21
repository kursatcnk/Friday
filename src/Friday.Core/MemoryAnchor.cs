using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Hafızaya yazılan göreli zamanları gerçek tarihe çevirir: "yarın Ahmet'le toplantım var" bir hafta sonra hâlâ
/// "yarın" diye okunursa model yanılır. Kayıt anında "22 Kasım'da Ahmet'le toplantım var" olur; böylece bilgi
/// zamanla bozulmaz, üstelik sabah brifingindeki "yaklaşanlar"a da kendiliğinden girer. "Bugün, yarın, öbür gün, dün"
/// ve "gelecek/bu/önümüzdeki/geçen + gün adı" çevrilir; "dünya", "yarınlar" gibi sözcüklere dokunulmaz.
/// </summary>
public static partial class MemoryAnchor
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly string[] Weekdays = ["pazar", "pazartesi", "salı", "çarşamba", "perşembe", "cuma", "cumartesi"];

    public static string Anchor(string text, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var anchored = NamedDay().Replace(text, match =>
        {
            var weekday = Array.IndexOf(Weekdays, match.Groups["w"].Value.ToLower(Turkish));
            if (weekday < 0)
            {
                return match.Value;
            }

            var prefix = match.Groups["p"].Value.ToLower(Turkish);
            var ahead = ((weekday - (int)today.DayOfWeek) + 7) % 7;
            var back = (((int)today.DayOfWeek - weekday) + 7) % 7;
            var day = prefix == "geçen"
                ? today.AddDays(back == 0 ? -7 : -back)
                : today.AddDays(ahead == 0 && prefix != "bu" ? 7 : ahead);
            var name = TurkishGrammar.Capitalize(Weekdays[weekday]);
            return $"{day.ToString("d MMMM", Turkish)} {name}{match.Groups["s"].Value}";
        });

        return RelativeDay().Replace(anchored, match =>
        {
            var offset = match.Groups["d"].Value.ToLower(Turkish) switch
            {
                "bugün" => 0,
                "yarın" => 1,
                "öbür gün" or "öbürgün" => 2,
                "dün" => -1,
                _ => (int?)null
            };
            if (offset is not { } days)
            {
                return match.Value;
            }

            var date = today.AddDays(days);
            var month = date.ToString("MMMM", Turkish);
            var suffix = match.Groups["s"].Value.ToLower(Turkish);
            var tail = suffix switch
            {
                "kü" or "ki" => TurkishGrammar.Locative(month)[month.Length..] + "ki",
                "a" or "e" => Dative(month),
                "" => TurkishGrammar.Locative(month)[month.Length..],
                _ => null
            };
            return tail is null ? match.Value : $"{date.Day} {month}{tail}";
        });
    }

    /// <summary>"Kasım" → "'a", "Eylül" → "'e" (ay adları ünsüzle biter).</summary>
    private static string Dative(string month)
    {
        var vowel = month.ToLower(Turkish).LastOrDefault(letter => "aıoueiöü".Contains(letter));
        return "aıou".Contains(vowel) ? "'a" : "'e";
    }

    // "yarın", "yarınki", "yarına", "dünkü", "bugün", "öbür gün"; ardından harf gelmez ("dünya", "yarınlar" hariç).
    [GeneratedRegex(@"(?<![\p{L}'’])(?<d>bugün|yarın|öbür ?gün|dün)(?<s>kü|ki|a|e)?(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeDay();

    // "gelecek cuma", "önümüzdeki salı", "bu pazartesi", "geçen çarşamba" (+ "günü").
    [GeneratedRegex(@"(?<![\p{L}])(?<p>gelecek|önümüzdeki|bu|geçen|haftaya) (?<w>pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar)(?<s> günü)?(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex NamedDay();
}
