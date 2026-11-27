using System.Globalization;

namespace Friday.Core;

/// <summary>
/// Dünya saati: "Tokyo'da saat kaç", "Londra'da şu an saat kaç". Windows'un saat dilimi bilgisi kullanılır; internet
/// gerekmez, yaz saati kendiliğinden hesaba katılır.
/// </summary>
public static class WorldClock
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Şehir ya da ülke (küçük harf) → (söylenecek ad, Windows saat dilimi).</summary>
    private static readonly Dictionary<string, (string Name, string Zone)> Places = new(StringComparer.Ordinal)
    {
        ["londra"] = ("Londra", "GMT Standard Time"), ["ingiltere"] = ("İngiltere", "GMT Standard Time"),
        ["lizbon"] = ("Lizbon", "GMT Standard Time"), ["portekiz"] = ("Portekiz", "GMT Standard Time"),
        ["dublin"] = ("Dublin", "GMT Standard Time"), ["irlanda"] = ("İrlanda", "GMT Standard Time"),
        ["paris"] = ("Paris", "Romance Standard Time"), ["fransa"] = ("Fransa", "Romance Standard Time"),
        ["madrid"] = ("Madrid", "Romance Standard Time"), ["ispanya"] = ("İspanya", "Romance Standard Time"),
        ["barselona"] = ("Barselona", "Romance Standard Time"), ["brüksel"] = ("Brüksel", "Romance Standard Time"),
        ["belçika"] = ("Belçika", "Romance Standard Time"), ["kopenhag"] = ("Kopenhag", "Romance Standard Time"),
        ["danimarka"] = ("Danimarka", "Romance Standard Time"),
        ["berlin"] = ("Berlin", "W. Europe Standard Time"), ["almanya"] = ("Almanya", "W. Europe Standard Time"),
        ["münih"] = ("Münih", "W. Europe Standard Time"), ["frankfurt"] = ("Frankfurt", "W. Europe Standard Time"),
        ["amsterdam"] = ("Amsterdam", "W. Europe Standard Time"), ["hollanda"] = ("Hollanda", "W. Europe Standard Time"),
        ["roma"] = ("Roma", "W. Europe Standard Time"), ["milano"] = ("Milano", "W. Europe Standard Time"),
        ["italya"] = ("İtalya", "W. Europe Standard Time"), ["viyana"] = ("Viyana", "W. Europe Standard Time"),
        ["avusturya"] = ("Avusturya", "W. Europe Standard Time"), ["zürih"] = ("Zürih", "W. Europe Standard Time"),
        ["isviçre"] = ("İsviçre", "W. Europe Standard Time"), ["stockholm"] = ("Stockholm", "W. Europe Standard Time"),
        ["isveç"] = ("İsveç", "W. Europe Standard Time"), ["oslo"] = ("Oslo", "W. Europe Standard Time"),
        ["norveç"] = ("Norveç", "W. Europe Standard Time"),
        ["varşova"] = ("Varşova", "Central European Standard Time"), ["polonya"] = ("Polonya", "Central European Standard Time"),
        ["prag"] = ("Prag", "Central Europe Standard Time"), ["budapeşte"] = ("Budapeşte", "Central Europe Standard Time"),
        ["atina"] = ("Atina", "GTB Standard Time"), ["yunanistan"] = ("Yunanistan", "GTB Standard Time"),
        ["bükreş"] = ("Bükreş", "GTB Standard Time"), ["romanya"] = ("Romanya", "GTB Standard Time"),
        ["sofya"] = ("Sofya", "FLE Standard Time"), ["bulgaristan"] = ("Bulgaristan", "FLE Standard Time"),
        ["kiev"] = ("Kiev", "FLE Standard Time"), ["ukrayna"] = ("Ukrayna", "FLE Standard Time"),
        ["helsinki"] = ("Helsinki", "FLE Standard Time"), ["finlandiya"] = ("Finlandiya", "FLE Standard Time"),
        ["moskova"] = ("Moskova", "Russian Standard Time"), ["rusya"] = ("Moskova", "Russian Standard Time"),
        ["kahire"] = ("Kahire", "Egypt Standard Time"), ["mısır"] = ("Mısır", "Egypt Standard Time"),
        ["tiflis"] = ("Tiflis", "Georgian Standard Time"), ["gürcistan"] = ("Gürcistan", "Georgian Standard Time"),
        ["bakü"] = ("Bakü", "Azerbaijan Standard Time"), ["azerbaycan"] = ("Azerbaycan", "Azerbaijan Standard Time"),
        ["tahran"] = ("Tahran", "Iran Standard Time"), ["iran"] = ("İran", "Iran Standard Time"),
        ["riyad"] = ("Riyad", "Arab Standard Time"), ["suudi arabistan"] = ("Suudi Arabistan", "Arab Standard Time"),
        ["doha"] = ("Doha", "Arab Standard Time"), ["katar"] = ("Katar", "Arab Standard Time"),
        ["dubai"] = ("Dubai", "Arabian Standard Time"), ["abu dabi"] = ("Abu Dabi", "Arabian Standard Time"),
        ["birleşik arap emirlikleri"] = ("Birleşik Arap Emirlikleri", "Arabian Standard Time"),
        ["delhi"] = ("Delhi", "India Standard Time"), ["yeni delhi"] = ("Yeni Delhi", "India Standard Time"),
        ["mumbai"] = ("Mumbai", "India Standard Time"), ["hindistan"] = ("Hindistan", "India Standard Time"),
        ["bangkok"] = ("Bangkok", "SE Asia Standard Time"), ["tayland"] = ("Tayland", "SE Asia Standard Time"),
        ["singapur"] = ("Singapur", "Singapore Standard Time"),
        ["pekin"] = ("Pekin", "China Standard Time"), ["şanghay"] = ("Şanghay", "China Standard Time"),
        ["çin"] = ("Çin", "China Standard Time"), ["hong kong"] = ("Hong Kong", "China Standard Time"),
        ["tokyo"] = ("Tokyo", "Tokyo Standard Time"), ["japonya"] = ("Japonya", "Tokyo Standard Time"),
        ["seul"] = ("Seul", "Korea Standard Time"), ["kore"] = ("Kore", "Korea Standard Time"),
        ["güney kore"] = ("Güney Kore", "Korea Standard Time"),
        ["sidney"] = ("Sidney", "AUS Eastern Standard Time"), ["melbourne"] = ("Melbourne", "AUS Eastern Standard Time"),
        ["new york"] = ("New York", "Eastern Standard Time"), ["washington"] = ("Washington", "Eastern Standard Time"),
        ["boston"] = ("Boston", "Eastern Standard Time"), ["toronto"] = ("Toronto", "Eastern Standard Time"),
        ["miami"] = ("Miami", "Eastern Standard Time"),
        ["şikago"] = ("Şikago", "Central Standard Time"), ["chicago"] = ("Chicago", "Central Standard Time"),
        ["teksas"] = ("Teksas", "Central Standard Time"), ["dallas"] = ("Dallas", "Central Standard Time"),
        ["los angeles"] = ("Los Angeles", "Pacific Standard Time"), ["san francisco"] = ("San Francisco", "Pacific Standard Time"),
        ["kaliforniya"] = ("Kaliforniya", "Pacific Standard Time"), ["seattle"] = ("Seattle", "Pacific Standard Time"),
        ["vancouver"] = ("Vancouver", "Pacific Standard Time"),
        ["meksika"] = ("Meksika", "Central Standard Time (Mexico)"), ["meksiko"] = ("Meksiko", "Central Standard Time (Mexico)"),
        ["sao paulo"] = ("Sao Paulo", "E. South America Standard Time"), ["brezilya"] = ("Brezilya", "E. South America Standard Time"),
        ["buenos aires"] = ("Buenos Aires", "Argentina Standard Time"), ["arjantin"] = ("Arjantin", "Argentina Standard Time"),
        ["johannesburg"] = ("Johannesburg", "South Africa Standard Time"), ["güney afrika"] = ("Güney Afrika", "South Africa Standard Time"),
        ["nairobi"] = ("Nairobi", "E. Africa Standard Time"), ["kenya"] = ("Kenya", "E. Africa Standard Time"),
    };

    /// <summary>Söylenen yer biliniyor mu ("Tokyo", "londra", "Amerika" değil: birden çok saat dilimi var)?</summary>
    public static bool Knows(string place) => Find(place) is not null;

    private static (string Name, TimeZoneInfo Zone)? Find(string place)
    {
        var key = place.Trim().ToLower(Turkish);
        return Places.TryGetValue(key, out var entry) && TimeZoneInfo.TryFindSystemTimeZoneById(entry.Zone, out var zone)
            ? (entry.Name, zone)
            : null;
    }

    /// <summary>"Tokyo'da saat 04:12, cumartesi; Türkiye'den 6 saat ileride." Yer bilinmiyorsa null.</summary>
    public static string? Describe(string place, DateTime utcNow, TimeZoneInfo local)
    {
        if (Find(place) is not var (name, zone))
        {
            return null;
        }

        var there = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
        var here = TimeZoneInfo.ConvertTimeFromUtc(utcNow, local);
        var difference = (there - here).TotalHours;
        var day = there.Date == here.Date ? "" : there.Date > here.Date ? ", yarın " + there.ToString("dddd", Turkish).ToLower(Turkish) : ", dün " + there.ToString("dddd", Turkish).ToLower(Turkish);
        var hours = Math.Abs(difference).ToString("0.#", Turkish);
        var gap = Math.Abs(difference) < 0.01 ? "burayla aynı saat" : difference > 0 ? $"buradan {hours} saat ileride" : $"buradan {hours} saat geride";
        return $"{name}{Suffix(name)} saat {there.ToString("HH:mm", CultureInfo.InvariantCulture)}{day}; {gap}.";
    }

    /// <summary>Bulunma eki: "Tokyo'da", "Paris'te", "Berlin'de".</summary>
    private static string Suffix(string name)
    {
        var lower = name.ToLower(Turkish);
        var vowel = lower.LastOrDefault(letter => "aeıioöuü".Contains(letter, StringComparison.Ordinal));
        var back = vowel is 'a' or 'ı' or 'o' or 'u';
        var hard = "fstkçşhp".Contains(lower[^1], StringComparison.Ordinal);
        return "'" + (hard ? "t" : "d") + (back ? "a" : "e");
    }
}
