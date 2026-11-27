using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// Hesap ve birim çevirme, dil modelsiz ve hatasız: "345 çarpı 12 kaç", "200'ün yüzde 15'i", "5 mil kaç kilometre",
/// "30 derece kaç fahrenheit". Küçük dil modelleri aritmetikte yanılabiliyor; bu sorular hiç modele gitmez.
/// </summary>
public static partial class SpokenMath
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Birim → (boyut, temel birime çarpan, okunuş).</summary>
    private static readonly Dictionary<string, (string Dimension, double Factor, string Name)> Units = new()
    {
        ["km"] = ("uzunluk", 1000, "kilometre"), ["kilometre"] = ("uzunluk", 1000, "kilometre"),
        ["metre"] = ("uzunluk", 1, "metre"), ["m"] = ("uzunluk", 1, "metre"),
        ["cm"] = ("uzunluk", 0.01, "santimetre"), ["santim"] = ("uzunluk", 0.01, "santimetre"), ["santimetre"] = ("uzunluk", 0.01, "santimetre"),
        ["mm"] = ("uzunluk", 0.001, "milimetre"), ["milimetre"] = ("uzunluk", 0.001, "milimetre"),
        ["mil"] = ("uzunluk", 1609.344, "mil"), ["inç"] = ("uzunluk", 0.0254, "inç"), ["feet"] = ("uzunluk", 0.3048, "feet"),
        ["fit"] = ("uzunluk", 0.3048, "feet"), ["ayak"] = ("uzunluk", 0.3048, "feet"), ["yarda"] = ("uzunluk", 0.9144, "yarda"),
        ["kg"] = ("ağırlık", 1, "kilogram"), ["kilo"] = ("ağırlık", 1, "kilogram"), ["kilogram"] = ("ağırlık", 1, "kilogram"),
        ["gram"] = ("ağırlık", 0.001, "gram"), ["gr"] = ("ağırlık", 0.001, "gram"), ["ton"] = ("ağırlık", 1000, "ton"),
        ["pound"] = ("ağırlık", 0.45359237, "pound"), ["libre"] = ("ağırlık", 0.45359237, "pound"), ["ons"] = ("ağırlık", 0.028349523, "ons"),
        ["litre"] = ("hacim", 1, "litre"), ["ml"] = ("hacim", 0.001, "mililitre"), ["mililitre"] = ("hacim", 0.001, "mililitre"),
        ["galon"] = ("hacim", 3.785411784, "galon"),
        // Mutfak ölçüleri (Türkiye'de yaygın değerler): su bardağı 200 ml, çay bardağı 100 ml, kaşıklar 15/10/5 ml.
        ["su bardağı"] = ("hacim", 0.2, "su bardağı"), ["çay bardağı"] = ("hacim", 0.1, "çay bardağı"),
        ["yemek kaşığı"] = ("hacim", 0.015, "yemek kaşığı"), ["tatlı kaşığı"] = ("hacim", 0.01, "tatlı kaşığı"),
        ["çay kaşığı"] = ("hacim", 0.005, "çay kaşığı"), ["kahve fincanı"] = ("hacim", 0.075, "kahve fincanı"),
        ["cc"] = ("hacim", 0.001, "mililitre"),
        ["celsius"] = ("sıcaklık", 1, "santigrat derece"), ["santigrat"] = ("sıcaklık", 1, "santigrat derece"), ["derece"] = ("sıcaklık", 1, "santigrat derece"),
        ["fahrenheit"] = ("sıcaklık", 1, "fahrenheit"), ["kelvin"] = ("sıcaklık", 1, "kelvin"),
    };

    /// <summary>Konuşulacak cevap; soru bir hesap değilse null.</summary>
    public static string? Answer(string sentence)
    {
        var text = Spaces().Replace(sentence.ToLower(Turkish).Trim().TrimEnd('?', '.', '!'), " ");
        text = Lead().Replace(text, "");
        return Vat(text) ?? Discount(text) ?? ShareOf(text) ?? Change(text) ?? Percent(text) ?? Convert(text) ?? Arithmetic(text);
    }

    /// <summary>
    /// İndirim ve zam: "1500 liranın yüzde 20 indirimlisi kaç" → 1.200; "2000 liraya yüzde 15 zam gelirse kaç olur" → 2.300.
    /// </summary>
    private static string? Discount(string text)
    {
        var match = DiscountRequest().Match(text);
        if (!match.Success || Number(match.Groups["a"].Value) is not { } amount || Number(match.Groups["p"].Value) is not { } percent
            || percent is <= 0 or >= 1000)
        {
            return null;
        }

        var raise = match.Groups["k"].Value.StartsWith("zam", StringComparison.Ordinal) || match.Groups["k"].Value.StartsWith("art", StringComparison.Ordinal);
        if (!raise && percent >= 100)
        {
            return null;
        }

        var delta = amount * percent / 100;
        return raise
            ? $"Yüzde {Format(percent)} zamla {Format(amount + delta)} olur; artış {Format(delta)}."
            : $"Yüzde {Format(percent)} indirimle {Format(amount - delta)} olur; indirim {Format(delta)}.";
    }

    /// <summary>"200, 800'ün yüzde kaçı" → yüzde 25; "800'de 200 yüzde kaç eder" de aynı.</summary>
    private static string? ShareOf(string text)
    {
        var match = ShareRequest().Match(text);
        if (!match.Success || Number(match.Groups["part"].Value) is not { } part || Number(match.Groups["whole"].Value) is not { } whole || whole == 0)
        {
            return null;
        }

        return $"{Format(part)}, {Format(whole)} sayısının yüzde {Format(Math.Round(part / whole * 100, 2))}'i eder.";
    }

    /// <summary>"500'den 650'ye artış yüzde kaç" → yüzde 30 artış; düşüşte "yüzde 20 azalış".</summary>
    private static string? Change(string text)
    {
        var match = ChangeRequest().Match(text);
        if (!match.Success || Number(match.Groups["from"].Value) is not { } from || Number(match.Groups["to"].Value) is not { } to || from == 0)
        {
            return null;
        }

        var change = (to - from) / from * 100;
        return Math.Abs(change) < 0.005
            ? "Değişmemiş; ikisi aynı."
            : $"{Format(from)} ile {Format(to)} arasında yüzde {Format(Math.Round(Math.Abs(change), 2))} {(change > 0 ? "artış" : "azalış")} var.";
    }

    /// <summary>
    /// KDV: "KDV dahil 1180 lira, yüzde 18 KDV'siz fiyatı ne" → 1.000; "1000 liraya yüzde 20 KDV eklenince kaç olur" → 1.200.
    /// Oran söylenmezse genel oran (yüzde 20) kullanılır ve söylenir. Ölçümde model 1180'in KDV'siz hâline 1.011,11 dedi.
    /// </summary>
    private static string? Vat(string text)
    {
        if (!text.Contains("kdv", StringComparison.Ordinal))
        {
            return null;
        }

        var toNet = VatOff().IsMatch(text);
        var toGross = !toNet && VatOn().IsMatch(text);
        if (!toNet && !toGross)
        {
            return null;
        }

        var rateMatch = VatRate().Match(text);
        var rate = rateMatch.Success ? Number(rateMatch.Groups["r"].Value) : 20;
        var rest = rateMatch.Success ? text.Remove(rateMatch.Index, rateMatch.Length) : text;
        if (rate is not (> 0 and < 100) || Amount().Match(rest) is not { Success: true } amount || Number(amount.Value) is not { } value || value <= 0)
        {
            return null;
        }

        var rateText = $"yüzde {Format(rate.Value)}" + (rateMatch.Success ? "" : ", genel oran");
        if (toNet)
        {
            var net = value / (1 + (rate.Value / 100));
            return $"KDV'siz fiyat {Format(Math.Round(net, 2))} lira; KDV {Format(Math.Round(value - net, 2))} lira ({rateText}).";
        }

        var gross = value * (1 + (rate.Value / 100));
        return $"KDV dahil {Format(Math.Round(gross, 2))} lira; KDV {Format(Math.Round(gross - value, 2))} lira ({rateText}).";
    }

    public static string Format(double value)
    {
        var rounded = Math.Abs(value) >= 100 ? Math.Round(value, 2) : Math.Round(value, 4);
        return rounded.ToString("#,0.####", Turkish);
    }

    private static string? Percent(string text)
    {
        var match = PercentOf().Match(text);
        if (!match.Success || Number(match.Groups["a"].Value) is not { } total || Number(match.Groups["p"].Value) is not { } percent)
        {
            return null;
        }

        return $"{Format(total)} sayısının yüzde {Format(percent)}'i {Format(total * percent / 100)} eder.";
    }

    private static string? Convert(string text)
    {
        var match = UnitQuestion().Match(text);
        if (!match.Success || Number(match.Groups["v"].Value) is not { } value
            || !Units.TryGetValue(match.Groups["from"].Value, out var from) || !Units.TryGetValue(match.Groups["to"].Value, out var to)
            || from.Dimension != to.Dimension)
        {
            return null;
        }

        double result;
        if (from.Dimension == "sıcaklık")
        {
            var celsius = from.Name switch { "fahrenheit" => (value - 32) * 5 / 9, "kelvin" => value - 273.15, _ => value };
            result = to.Name switch { "fahrenheit" => (celsius * 9 / 5) + 32, "kelvin" => celsius + 273.15, _ => celsius };
        }
        else
        {
            result = value * from.Factor / to.Factor;
        }

        return $"{Format(value)} {from.Name}, {Format(result)} {to.Name} eder.";
    }

    private static string? Arithmetic(string text)
    {
        var match = Expression().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var expression = match.Groups["e"].Value;
        var tokens = Token().Matches(Operators().Replace(expression, operation => operation.Value switch
        {
            "artı" => " + ",
            "eksi" => " - ",
            "çarpı" or "kere" or "x" or "×" => " * ",
            "bölü" or "÷" => " / ",
            _ => operation.Value
        })).Select(token => token.Value).ToList();
        if (tokens.Count < 3 || tokens.Count % 2 == 0)
        {
            return null;
        }

        try
        {
            var value = Evaluate(tokens);
            var spoken = Operators().Replace(expression, operation => operation.Value switch
            {
                "x" or "×" or "*" => "çarpı",
                "/" or "÷" => "bölü",
                "+" => "artı",
                "-" => "eksi",
                _ => operation.Value
            });
            return double.IsFinite(value) ? $"{Spaces().Replace(spoken, " ").Trim()}, {Format(value)} eder." : "Sıfıra bölünemez.";
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Çarpma ve bölme önce, sonra toplama ve çıkarma.</summary>
    private static double Evaluate(List<string> tokens)
    {
        var terms = new List<double> { Number(tokens[0]) ?? throw new FormatException() };
        var signs = new List<char>();
        for (var index = 1; index < tokens.Count; index += 2)
        {
            var operation = tokens[index][0];
            var operand = Number(tokens[index + 1]) ?? throw new FormatException();
            switch (operation)
            {
                case '*':
                    terms[^1] *= operand;
                    break;
                case '/':
                    terms[^1] = operand == 0 ? double.NaN : terms[^1] / operand;
                    break;
                default:
                    signs.Add(operation);
                    terms.Add(operand);
                    break;
            }
        }

        var result = terms[0];
        for (var index = 0; index < signs.Count; index++)
        {
            result = signs[index] == '+' ? result + terms[index + 1] : result - terms[index + 1];
        }

        return result;
    }

    private static double? Number(string text)
    {
        var clean = text.Trim().Replace(".", "", StringComparison.Ordinal).Replace(',', '.');
        if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return QuickIntents.ParseNumber(text);
    }

    // "KDV'siz", "KDV hariç", "KDV'yi çıkar", "matrahı" → verilen tutar KDV dahildir.
    [GeneratedRegex(@"kdv['’]?siz|kdvsiz|kdv hariç|kdv['’]?yi? (?:çıkar|düş)|matrah")]
    private static partial Regex VatOff();

    // "KDV'li", "artı KDV", "KDV ekle(nince)", "KDV dahil kaç" → verilen tutar KDV hariçtir.
    [GeneratedRegex(@"kdv['’]?li|artı kdv|\+ ?kdv|kdv (?:ekle|eklen|bindir)|kdv dahil (?:kaç|ne kadar|fiyat|hali|tutar)")]
    private static partial Regex VatOn();

    [GeneratedRegex(@"(?:yüzde|%) ?(?<r>\d+(?:[.,]\d+)?)|(?<r>\d+(?:[.,]\d+)?) ?%")]
    private static partial Regex VatRate();

    [GeneratedRegex(@"\d[\d.]*(?:,\d+)?")]
    private static partial Regex Amount();

    // "1500 liranın yüzde 20 indirimlisi kaç", "1500 tl'ye yüzde 20 indirim yapılırsa kaç olur", "2000 liraya yüzde 15 zam gelirse"
    [GeneratedRegex(@"^(?<a>[\d.,]+)(?: ?(?:lira|tl|₺))?(?:['’]?(?:nın|nin|nun|nün|ın|in|un|ün|ya|ye|a|e|lık|lik))? (?:yüzde|%) ?(?<p>[\d.,]+)(?:['’]?(?:lik|lık|luk|lük))? (?<k>indirim\w*|zam\w*|artış\w*)(?: (?:yapılırsa|uygulanırsa|gelirse|olursa|ile|la|le))?(?: (?:kaç|ne kadar|ne)(?: olur| eder| tutar)?| fiyatı(?: ne| kaç)?| hali(?: ne| kaç)?)?$")]
    private static partial Regex DiscountRequest();

    // "200, 800'ün yüzde kaçı", "200 sayısı 800'ün yüzde kaçıdır"
    [GeneratedRegex(@"^(?<part>[\d.,]+)(?: sayısı)?,? (?<whole>[\d.,]+)['’]?(?:ın|in|un|ün|nın|nin|nun|nün) yüzde kaçı(?:dır| eder| ediyor)?$")]
    private static partial Regex ShareRequest();

    // "500'den 650'ye artış yüzde kaç", "1200'den 900'e düşüş yüzde kaç", "fiyat 500'den 650'ye çıktı, yüzde kaç arttı"
    [GeneratedRegex(@"(?:^|, |\s)(?<from>[\d.,]+)['’]?(?:den|dan|ten|tan) (?<to>[\d.,]+)['’]?(?:ye|ya|e|a)(?: (?:çıktı|indi|düştü|yükseldi))?,? (?:artış|düşüş|azalış|değişim|fark)?(?: oranı)? ?yüzde kaç(?: arttı| azaldı| düştü| değişti)?$")]
    private static partial Regex ChangeRequest();

    [GeneratedRegex(@"^(?:hesapla|hesaplar mısın|bir hesap yap|söyle bakalım)[, ]+")]
    private static partial Regex Lead();

    // "200'ün yüzde 15'i kaç", "1500 liranın yüzde 18'i ne kadar"
    [GeneratedRegex(@"^(?<a>[\d.,]+)(?:['’]?(?:ın|in|un|ün|nın|nin|nun|nün)| liranın| doların)? yüzde (?<p>[\d.,]+)['’]?(?:i|ı|u|ü|si|sı|su|sü) (?:kaç|ne kadar|ne eder|nedir)$")]
    private static partial Regex PercentOf();

    // "5 mil kaç kilometre", "70 kilo kaç pound eder", "30 derece kaç fahrenheit"
    [GeneratedRegex(@"^(?<v>[\d.,]+|yarım|bir buçuk) (?<from>\p{L}+(?: \p{L}+)?) kaç (?<to>\p{L}+(?: \p{L}+)?)(?: eder| yapar| olur)?$")]
    private static partial Regex UnitQuestion();

    // "345 çarpı 12 kaç", "12 artı 7 eksi 3 kaç eder", "1500 bölü 4"
    [GeneratedRegex(@"^(?<e>[\d.,]+(?: ?(?:artı|eksi|çarpı|kere|bölü|[+\-*/x×÷]) ?[\d.,]+)+)(?: (?:kaç|kaç eder|ne eder|eder|eşittir|yapar|kaç yapar))?$")]
    private static partial Regex Expression();

    [GeneratedRegex(@"artı|eksi|çarpı|kere|bölü|[x×÷*/+\-]")]
    private static partial Regex Operators();

    [GeneratedRegex(@"[\d.,]+|[+\-*/]")]
    private static partial Regex Token();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

/// <summary>"Yılbaşına kaç gün kaldı", "29 Ekim'e kaç gün var", "doğum günüme kaç gün kaldı" (hafızadan).</summary>
public static partial class DayCountdown
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Dictionary<string, (int Month, int Day)> Named = new()
    {
        ["yılbaşı"] = (1, 1), ["yeni yıl"] = (1, 1), ["sevgililer günü"] = (2, 14), ["23 nisan"] = (4, 23), ["1 mayıs"] = (5, 1),
        ["19 mayıs"] = (5, 19), ["30 ağustos"] = (8, 30), ["29 ekim"] = (10, 29), ["cumhuriyet bayramı"] = (10, 29),
        ["zafer bayramı"] = (8, 30), ["çocuk bayramı"] = (4, 23), ["gençlik bayramı"] = (5, 19), ["öğretmenler günü"] = (11, 24),
        ["noel"] = (12, 25), ["anneler günü"] = (0, 0), ["babalar günü"] = (0, 0)
    };

    /// <summary>Hedefin adı (ör. "yılbaşı", "doğum günüm"); soru bu kalıpta değilse null.</summary>
    public static string? Target(string sentence)
    {
        var match = Question().Match(sentence.ToLower(Turkish).Trim().TrimEnd('?', '.', '!'));
        return match.Success ? match.Groups["t"].Value.Trim() : null;
    }

    /// <summary>Konuşulacak cevap. Hedef bilinen bir gün, bir tarih ya da hafızada tarihi geçen bir bilgiyse hesaplanır.</summary>
    public static string? Answer(string target, IEnumerable<MemoryItem> memories, DateOnly today)
    {
        var root = Root(target);
        DateOnly? date = null;
        string label = target;
        foreach (var (name, (month, day)) in Named)
        {
            if (month > 0 && (root.StartsWith(Root(name), StringComparison.Ordinal) || Root(name).StartsWith(root, StringComparison.Ordinal)))
            {
                date = Next(month, day, today);
                label = name;
                break;
            }
        }

        date ??= MemoryDates.NextOccurrence(target, today);
        if (date is null)
        {
            // "doğum günüm" → hafızada "doğum günü" geçen ve tarihi olan bilgi; kullanıcının kendisine ait olan
            // ("Kullanıcının doğum günü …") başkasınınkinden ("Kullanıcının kızının doğum günü …") önce gelir.
            var words = root.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => word.Length > 4 ? word[..4] : word).ToList();
            var candidates = memories
                .Where(memory => words.All(word => memory.Text.ToLower(Turkish).Contains(word, StringComparison.Ordinal))
                                 && MemoryDates.NextOccurrence(memory.Text, today) is not null)
                .ToList();
            var found = candidates.FirstOrDefault(memory => memory.Text.ToLower(Turkish).Contains("kullanıcının " + words[0], StringComparison.Ordinal))
                        ?? candidates.FirstOrDefault();
            if (found is not null)
            {
                date = MemoryDates.NextOccurrence(found.Text, today);
                label = target;
            }
        }

        if (date is not { } when)
        {
            return null;
        }

        var days = when.DayNumber - today.DayNumber;
        // "Annemin doğum günü" → "Annenin doğum günü": kullanıcının sözü Friday'in ağzına çevrilir.
        var spoken = TurkishGrammar.Capitalize(TurkishGrammar.ToYou(label));
        return days switch
        {
            0 => $"{spoken} bugün!",
            1 => $"{spoken} yarın.",
            _ => $"{spoken} için {days} gün kaldı; {when.ToString("d MMMM dddd", Turkish)}."
        };
    }

    private static DateOnly Next(int month, int day, DateOnly today)
    {
        var date = new DateOnly(today.Year, month, day);
        return date >= today ? date : date.AddYears(1);
    }

    /// <summary>Ekleri atar: "yılbaşına" → "yılbaşı", "doğum günüme" → "doğum günü".</summary>
    private static string Root(string text) => Suffix().Replace(text.ToLower(Turkish).Trim(), "");

    [GeneratedRegex(@"^(?<t>.+?)['’]?(?:ya|ye|na|ne|a|e)? kaç gün (?:kaldı|var)$")]
    private static partial Regex Question();

    [GeneratedRegex(@"(?:['’]?(?:ya|ye|na|ne|ına|ine|una|üne|ma|me|m|a|e))$")]
    private static partial Regex Suffix();
}
