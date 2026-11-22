using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Bir bilginin anahtarı: kimin (kullanıcı, "Ayşe", "kızı", "arabası") hangi tek değerli özelliği (telefon, adres, okul,
/// işyeri, doğum günü…). "Ayşe'nin telefon numarası 0532…" ile "Ayşe'nin yeni numarası 0533…" aynı anahtardır: yenisi
/// eskisinin yerine geçer, hafızada çelişen iki numara kalmaz. Eski kural yalnızca neredeyse aynı söylenişi tanıyordu
/// (sözcüklerin %60'ı ortak); böyle bir güncellemede ortaklık %23'tü. Hobi, alerji, sevdikleri gibi birden çok değeri
/// olabilen özelliklere dokunulmaz; "eski adresi" gibi geçmişi anlatan bilgiler de güncelleme sayılmaz.
/// </summary>
public static partial class FactKey
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Tek değerli özellikler: sözcük başı → ortak ad. Kısa kökler yalnızca iyelik ekiyle eşleşir ("kilosu", "kilometre" değil).</summary>
    private static readonly (string Stem, string Name)[] Attributes =
    [
        ("telefon", "telefon"), ("numara", "numara"), ("adres", "adres"), ("okul", "okul"), ("işyer", "işyeri"), ("ofis", "işyeri"),
        ("maaş", "maaş"), ("kira", "kira"), ("plaka", "plaka"), ("muayene", "muayene"), ("e-posta", "e-posta"), ("eposta", "e-posta"),
        ("mail", "e-posta"), ("boy", "boy"), ("kilo", "kilo"), ("beden", "beden"), ("ad", "ad"), ("isim", "ad"), ("ism", "ad"),
        ("öğretmen", "öğretmen"), ("doktor", "doktor"), ("patron", "patron"), ("müdür", "müdür"), ("sınıf", "sınıf"), ("yaş", "yaş"),
        ("doğum", "doğum günü")
    ];

    /// <summary>Yer bildiren fiiller: "Maslak'ta çalışıyor" işyeri, "Kadıköy'de oturuyor" adres demektir.</summary>
    private static readonly (string Stem, string Name)[] Verbs =
    [
        ("çalışıyor", "işyeri"), ("çalışır", "işyeri"), ("çalışmakta", "işyeri"), ("oturuyor", "adres"), ("oturur", "adres"),
        ("yaşıyor", "adres"), ("yaşar", "adres"), ("taşındı", "adres"), ("gidiyor", "okul")
    ];

    /// <summary>Sahip olabilen adlar: "kızı", "oğlum", "arabası" bilginin kime ait olduğunu söyler.</summary>
    private static readonly string[] Owners =
        ["kız", "oğl", "eş", "anne", "baba", "abla", "abi", "ağabey", "kardeş", "araba", "kedi", "köpeğ", "sevgili", "patron", "müdür"];

    private static readonly string[] Possessives = ["sı", "si", "su", "sü", "ım", "im", "um", "üm", "m", "ı", "i", "u", "ü"];

    /// <summary>"Numarası"nın önündeki ad özelliği belirler: ayakkabı numarası telefon değildir.</summary>
    private static readonly HashSet<string> PhoneWords = new(StringComparer.Ordinal) { "telefon", "cep", "yeni", "güncel", "iş", "ev" };

    /// <summary>Bilginin anahtarı; tek değerli bir özellik bulunamazsa null.</summary>
    public static string? Of(string fact)
    {
        var original = fact.Trim();
        var text = original.ToLower(Turkish).Replace('’', '\'');
        if (Past().IsMatch(text))
        {
            return null;
        }

        var words = Split().Split(text).Where(word => word.Length > 0).ToList();
        var owners = new List<string>();
        var index = 0;
        // Tamlayan zinciri: "ayşe'nin", "kızının", "arabasının"; "kullanıcının" kullanıcının kendisidir.
        for (; index < words.Count && Genitive(words[index]) is { } owner; index++)
        {
            if (owner != "kullanıcı")
            {
                owners.Add(owner);
            }
        }

        if (index < words.Count && words[index] == "kullanıcı")
        {
            index++;
        }

        // "Kızı Kadıköy'deki okula gidiyor": iyelikli ad da sahiptir.
        if (index < words.Count && Owned(words[index]) is { } possessed && Attribute(words, index) is null)
        {
            owners.Add(possessed);
            index++;
        }

        // "Doruk 9 yaşında", "Ayşe Ataşehir'de oturuyor": büyük harfle başlayan ilk ad, ardından bir özellik gelirse sahiptir.
        if (owners.Count == 0 && index == 0 && words.Count > 1 && original.Length > 0 && char.IsUpper(original[0])
            && !Fillers.Contains(words[0]) && Attribute(words, 0) is null && !IsVerb(words[0]))
        {
            for (var at = 1; at < words.Count; at++)
            {
                if (Found(words, at) is { } later)
                {
                    return Key([words[0].Split('\'')[0]], later);
                }
            }
        }

        for (var at = index; at < words.Count; at++)
        {
            if (Found(words, at) is { } name)
            {
                return Key(owners, name);
            }
        }

        return null;
    }

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal) { "bugün", "yarın", "dün", "artık", "şimdi", "kullanıcı", "yeni", "bu" };

    private static bool IsVerb(string word) => Verbs.Any(verb => word.StartsWith(verb.Stem, StringComparison.Ordinal));

    private static string? Found(List<string> words, int at) =>
        Verbs.FirstOrDefault(verb => words[at].StartsWith(verb.Stem, StringComparison.Ordinal)).Name ?? Attribute(words, at);

    /// <summary>Yeni bilgi, eskisinin aynı özelliğini mi söylüyor (o halde yerine geçer)?</summary>
    public static bool Same(string older, string newer) =>
        Of(older) is { } key && key == Of(newer);

    /// <summary>
    /// İki bilgi bilinen farklı anahtarlara mı sahip? Sözcüklerin çoğu ortak olsa da ("Ayşe Ataşehir'de oturuyor",
    /// "Kullanıcı Ataşehir'de oturuyor") farklı kişilerin bilgisidir; eski kural bunları birleştirip birini siliyordu.
    /// </summary>
    public static bool Differ(string first, string second) =>
        Of(first) is { } one && Of(second) is { } two && one != two;

    private static string Key(List<string> owners, string name) => (owners.Count == 0 ? "ben" : string.Join(">", owners)) + "|" + name;

    private static string? Attribute(List<string> words, int at)
    {
        var word = words[at];
        foreach (var (stem, name) in Attributes)
        {
            if (!Inflected(word, stem))
            {
                continue;
            }

            if (name == "numara")
            {
                // "Telefon numarası", "yeni numarası" telefondur; "ayakkabı numarası" kendi özelliğidir.
                return at > 0 && !PhoneWords.Contains(words[at - 1]) && Genitive(words[at - 1]) is null ? words[at - 1] + " numarası" : "telefon";
            }

            var next = at + 1 < words.Count ? words[at + 1] : "";
            var day = next.StartsWith("gün", StringComparison.Ordinal) || next.StartsWith("tarih", StringComparison.Ordinal);
            if (name == "doğum günü")
            {
                if (!day)
                {
                    continue;
                }

                return name;
            }

            // "Maaş günü", "kira günü" maaşın kendisi değildir.
            return day ? name + " günü" : name;
        }

        return null;
    }

    /// <summary>Sözcük bu kökün kendisi ya da ekli hali mi? Dört harfe kadar kökler yalnızca iyelik/hal ekiyle kabul edilir.</summary>
    private static bool Inflected(string word, string stem)
    {
        if (!word.StartsWith(stem, StringComparison.Ordinal))
        {
            return false;
        }

        if (stem.Length > 4)
        {
            return true;
        }

        var rest = word[stem.Length..];
        var apostrophe = rest.StartsWith('\'');
        rest = rest.TrimStart('\'');
        return rest.Length == 0 || apostrophe || ShortEnding().IsMatch(rest);
    }

    /// <summary>"kızı" → "kız", "oğlum" → "oğl", "arabası" → "araba"; sahip adı değilse null.</summary>
    private static string? Owned(string word)
    {
        foreach (var owner in Owners)
        {
            if (word.StartsWith(owner, StringComparison.Ordinal) && Possessives.Contains(word[owner.Length..]))
            {
                return owner;
            }
        }

        return null;
    }

    /// <summary>Tamlayan (ilgi hali) sözcüğün kökü: "ayşe'nin" → "ayşe", "arabasının" → "araba", "kızımın" → "kız".</summary>
    private static string? Genitive(string word)
    {
        var apostrophe = word.IndexOf('\'', StringComparison.Ordinal);
        if (apostrophe > 0)
        {
            return word[(apostrophe + 1)..] is "nin" or "nın" or "nun" or "nün" or "in" or "ın" or "un" or "ün" ? word[..apostrophe] : null;
        }

        if (GenitiveEnd().Match(word) is not { Success: true } end || end.Index < 2 || word is "için")
        {
            return null;
        }

        var stem = word[..end.Index];
        if (stem == "kullanıcı")
        {
            return stem;
        }

        foreach (var possessive in Possessives)
        {
            if (stem.Length > possessive.Length + 1 && stem.EndsWith(possessive, StringComparison.Ordinal))
            {
                return stem[..^possessive.Length];
            }
        }

        return stem;
    }

    [GeneratedRegex(@"(?:nın|nin|nun|nün|ın|in|un|ün)$")]
    private static partial Regex GenitiveEnd();

    /// <summary>Kısa köklerin kabul edilen ekleri: iyelik ve hal ekleri ("boyu", "kilosu", "adı", "okula", "yaşında").</summary>
    [GeneratedRegex(@"^(?:s?[ıiuü]|[ıiuü]?m|[ıiuü]?n|s?[ıiuü]n?(?:d[ae]|[ae]|ı|i|u|ü)?|[ae]|d[ae]|l[ae]r[ıi]?)$")]
    private static partial Regex ShortEnding();

    [GeneratedRegex(@"[\s,.;:!?()""]+")]
    private static partial Regex Split();

    /// <summary>Geçmişi anlatan bilgi ("eski adresi", "önceden … çalışıyordu") bir güncelleme değildir.</summary>
    [GeneratedRegex(@"\b(?:eski|önceki|önceden|daha önce|eskiden)\b|\p{L}+yordu\b")]
    private static partial Regex Past();
}
