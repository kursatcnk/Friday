using System.Globalization;
using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Kalıbı belli bir komutun doğrudan çalıştırılacak aracı.</summary>
/// <param name="Tentative">
/// Kalıp tuttu ama komutun gerçekten yapılabileceği araçta anlaşılır (ör. "X'i aç"ta X bir uygulama mı?). Araç
/// yapamazsa soru dil modeline gider.
/// </param>
public sealed record QuickCommand(string Tool, IReadOnlyDictionary<string, string> Arguments, bool Tentative = false);

/// <summary>
/// Günlük komutların çoğu kalıptır: "saat kaç", "yarın hava nasıl", "dolar kaç", "sesi kıs", "10 dakika sonra
/// hatırlat", "Spotify'ı aç". Bunlar dil modeline hiç sorulmadan tanınır ve yarım saniyenin altında cevaplanır; model
/// böyle bir soruda önce düşünüp sonra araç çağırdığı için 3–8 sn harcıyordu. Kalıba uymayan her şey modele gider.
/// </summary>
public static partial class QuickIntents
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"dışarıda hava nasıl" gibi cümlelerde şehir sanılmaması gereken sözcükler.</summary>
    private static readonly HashSet<string> NotCities = ["dışarı", "bura", "ora", "şura", "ev", "iş", "ofis", "hava"];

    private static readonly Dictionary<string, int> Ones = new()
    {
        ["bir"] = 1, ["iki"] = 2, ["üç"] = 3, ["dört"] = 4, ["beş"] = 5, ["altı"] = 6, ["yedi"] = 7, ["sekiz"] = 8, ["dokuz"] = 9
    };

    private static readonly Dictionary<string, int> Tens = new()
    {
        ["on"] = 10, ["yirmi"] = 20, ["otuz"] = 30, ["kırk"] = 40, ["elli"] = 50, ["altmış"] = 60, ["yetmiş"] = 70, ["seksen"] = 80, ["doksan"] = 90
    };

    /// <param name="routines">Tanımlı rutinlerin adları ("çalışma modu", "mola"…).</param>
    /// <param name="triggers">Özel komutların ifadeleri ("baha"); cümlenin herhangi bir yerinde geçince çalışır.</param>
    public static QuickCommand? Match(string text, IEnumerable<string>? routines = null, IEnumerable<string>? triggers = null)
    {
        if (TypeText(text) is { } type)
        {
            return type;
        }

        if (ClipboardPasteOrdinal(text) is { } paste)
        {
            return paste;
        }

        if (SnippetDefinition(text) is { } snippet)
        {
            return Command("kisaltma", ("islem", "ekle"), ("ad", snippet.Name), ("metin", snippet.Text));
        }

        if (WordDefinition(text) is { } word)
        {
            return Command("kelime", ("islem", "ekle"), ("kelime", word.Front), ("anlam", word.Back));
        }

        if (CustomCommand(text) is { } custom)
        {
            return Command("ozel_komut", ("ifade", custom.Phrase), ("adim", custom.Step));
        }

        var sentence = Normalize(text);
        if (sentence.Length == 0)
        {
            return null;
        }

        return Chat(sentence)
            ?? Closing(sentence)
            ?? Trigger(sentence, triggers ?? [])
            ?? StatusReport(sentence)
            ?? SystemControl(sentence)
            ?? Watch(sentence)
            ?? Luck(sentence)
            ?? Stopwatch(sentence)
            ?? Selection(sentence)
            ?? Dictation(sentence)
            ?? Deep(sentence)
            ?? Creative(sentence)
            ?? Triggers(sentence)
            ?? Quiet(sentence)
            ?? ScreenTime(sentence)
            ?? Workspace(sentence)
            ?? WindowLayout(sentence)
            ?? News(sentence)
            ?? Research(sentence)
            ?? CalendarQuestion(sentence)
            ?? Downloads(sentence)
            ?? DayEnd(sentence)
            ?? Hearing(sentence)
            ?? Recall(sentence)
            ?? Undo(sentence)
            ?? Life(sentence)
            ?? Snippets(sentence)
            ?? Practice(sentence)
            ?? Bills(sentence)
            ?? Alerts(sentence)
            ?? Todo(sentence)
            ?? RecurringReminder(sentence)
            ?? Ambient(sentence)
            ?? ClipboardHistory(sentence)
            ?? Repeat(sentence)
            ?? CurrencyConversion(sentence)
            ?? WakeUp(sentence)
            ?? Video(sentence)
            ?? Clock(sentence)
            ?? Weather(sentence)
            ?? Currency(sentence)
            ?? AppVolume(sentence)
            ?? Volume(sentence)
            ?? Media(sentence)
            ?? ReminderAt(sentence)
            ?? Timer(sentence)
            ?? Reminders(sentence)
            ?? Notes(sentence)
            ?? Calculation(text)
            ?? Countdown(text)
            ?? Rhythm(sentence)
            ?? Routine(sentence, routines ?? [])
            ?? SnippetUse(sentence)
            ?? BillPaid(sentence)
            ?? HabitDone(sentence)
            ?? TodoDone(sentence)
            ?? CloseApp(sentence)
            ?? App(sentence);
    }

    /// <summary>"5", "beş", "on beş", "yarım", "bir buçuk", "iki buçuk" → sayı.</summary>
    public static double? ParseNumber(string text)
    {
        var words = text.Trim().ToLower(Turkish).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }

        if (words is ["yarım"])
        {
            return 0.5;
        }

        if (double.TryParse(words[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var digits))
        {
            return words.Length == 1 ? digits : words is [_, "buçuk"] ? digits + 0.5 : null;
        }

        double total = 0;
        var matched = false;
        foreach (var word in words)
        {
            if (word == "buçuk" && matched)
            {
                total += 0.5;
            }
            else if (Ones.TryGetValue(word, out var one))
            {
                total += one;
                matched = true;
            }
            else if (Tens.TryGetValue(word, out var ten))
            {
                total += ten;
                matched = true;
            }
            else
            {
                return null;
            }
        }

        return matched ? total : null;
    }

    /// <summary>
    /// Yalnızca son cümleye bakılır ("İyiyim. YouTube'dan X aç." → "YouTube'dan X aç"). Hitap, nezaket sözleri ve
    /// noktalama atılır; "dinlemek istiyorum", "açar mısın", "çal" gibi biçimler "aç"a çevrilir.
    /// </summary>
    internal static string Normalize(string text)
    {
        var sentences = SentenceBreak().Split(text.Trim()).Where(part => part.Trim().Length > 0).ToArray();
        var sentence = (sentences.Length > 0 ? sentences[^1] : text).Trim().TrimEnd('.', '!', '?', ' ').ToLower(Turkish);
        sentence = Address().Replace(sentence, "");
        sentence = Politeness().Replace(" " + sentence, " ");
        sentence = OpenVerbs().Replace(sentence, " aç");
        return Whitespace().Replace(sentence, " ").Trim().TrimEnd(',').Trim();
    }

    private static QuickCommand Command(string tool, params (string Key, string Value)[] arguments) =>
        new(tool, arguments.Where(pair => pair.Value.Length > 0).ToDictionary(pair => pair.Key, pair => pair.Value));

    /// <summary>"yeni sohbet", "konuyu değiştirelim" → yeni sohbet; "sohbeti temizle" → açık sohbeti sil.</summary>
    private static QuickCommand? Chat(string sentence) =>
        NewChat().IsMatch(sentence) ? Command("sohbet", ("islem", "yeni"))
        : ClearChat().IsMatch(sentence) ? Command("sohbet", ("islem", "temizle"))
        : null;

    private static QuickCommand? Video(string sentence)
    {
        foreach (var pattern in new[] { YouTubeFirst(), YouTubeLast(), VideoLast() })
        {
            var match = pattern.Match(sentence);
            if (match.Success && match.Groups["q"].Value.Trim() is { Length: > 1 } query)
            {
                return Command("video_ac", ("arama", query));
            }
        }

        return null;
    }

    private static QuickCommand? Clock(string sentence)
    {
        if (TimeQuestion().IsMatch(sentence))
        {
            return Command("saat_ve_tarih", ("odak", "saat"));
        }

        return DateQuestion().IsMatch(sentence) ? Command("saat_ve_tarih", ("odak", "tarih")) : null;
    }

    private static QuickCommand? Weather(string sentence)
    {
        var match = WeatherQuestion().Match(sentence);
        if (!match.Success)
        {
            return null;
        }

        var city = match.Groups["city"].Value.Trim();
        if (NotCities.Contains(city) || city.Split(' ').Length > 3)
        {
            city = "";
        }

        var when = (match.Groups["w1"].Value + match.Groups["w2"].Value).Trim();
        var (focus, days) = when switch
        {
            "yarın" => ("yarin", "2"),
            "bu hafta" or "haftaya" => ("hafta", "7"),
            "hafta sonu" => ("haftasonu", "7"),
            _ => ("bugun", "2")
        };
        var rain = match.Groups["rain"].Success ? "yagmur" : "";
        return Command("hava_durumu", ("sehir", city), ("gun", days), ("odak", focus), ("soru", rain));
    }

    private static QuickCommand? Currency(string sentence)
    {
        var match = CurrencyQuestion().Match(sentence);
        return match.Success ? Command("doviz", ("birim", match.Groups["c"].Value)) : null;
    }

    private static QuickCommand? Media(string sentence)
    {
        var command = sentence switch
        {
            _ when Pause().IsMatch(sentence) => "oynat_duraklat",
            _ when Next().IsMatch(sentence) => "sonraki",
            _ when Previous().IsMatch(sentence) => "onceki",
            _ when VolumeUp().IsMatch(sentence) => "ses_ac",
            _ when VolumeDown().IsMatch(sentence) => "ses_kis",
            _ when Mute().IsMatch(sentence) => "sessiz",
            _ when NowPlaying().IsMatch(sentence) => "ne_caliyor",
            _ => ""
        };
        return command.Length > 0 ? Command("medya", ("komut", command)) : null;
    }

    private static QuickCommand? Timer(string sentence)
    {
        var match = TimerRequest().Match(sentence);
        if (!match.Success || ParseNumber(match.Groups["n"].Value) is not { } amount || amount <= 0)
        {
            return null;
        }

        var minutes = match.Groups["u"].Value switch
        {
            "saniye" => amount / 60,
            "saat" => amount * 60,
            _ => amount
        };
        return Command("zamanlayici",
            ("dakika", minutes.ToString("0.###", CultureInfo.InvariantCulture)), ("not", match.Groups["note"].Value.Trim()));
    }

    private static QuickCommand? Volume(string sentence)
    {
        if (VolumeSet().Match(sentence) is { Success: true } set && int.TryParse(set.Groups["n"].Value, CultureInfo.InvariantCulture, out var level))
        {
            return Command("medya", ("komut", "ses_ayarla"), ("deger", Math.Clamp(level, 0, 100).ToString(CultureInfo.InvariantCulture)));
        }

        return VolumeQuestion().IsMatch(sentence) ? Command("medya", ("komut", "ses_seviyesi")) : null;
    }

    /// <summary>"yarın 9'da doktoru hatırlat", "akşam 8'de ilacımı hatırlat", "pazartesi 10:30'da toplantıyı hatırlat".</summary>
    private static QuickCommand? ReminderAt(string sentence)
    {
        var match = ReminderRequest().Match(sentence);
        if (!match.Success || match.Groups["when"].Value.Trim().Length == 0 || TurkishTime.Parse(match.Groups["when"].Value, DateTime.Now) is null)
        {
            return null;
        }

        return Command("hatirlatici", ("islem", "ekle"), ("zaman", match.Groups["when"].Value.Trim()), ("metin", match.Groups["note"].Value.Trim()));
    }

    private static QuickCommand? Reminders(string sentence)
    {
        if (ReminderList().IsMatch(sentence))
        {
            return Command("hatirlatici", ("islem", "listele"));
        }

        return ReminderCancel().Match(sentence) is { Success: true } cancel
            ? Command("hatirlatici", ("islem", "sil"), ("metin", cancel.Groups["x"].Value.Trim()))
            : null;
    }

    private static QuickCommand? Notes(string sentence)
    {
        if (NoteTake().Match(sentence) is { Success: true } take && take.Groups["x"].Value.Trim() is { Length: > 1 } note)
        {
            return Command("not", ("islem", "ekle"), ("metin", note));
        }

        return NoteRead().IsMatch(sentence) ? Command("not", ("islem", "oku")) : null;
    }

    private static QuickCommand? Calculation(string text) =>
        SpokenMath.Answer(Address().Replace(text.Trim().ToLower(Turkish), "")) is not null ? Command("hesap", ("soru", text.Trim())) : null;

    private static QuickCommand? Countdown(string text) =>
        DayCountdown.Target(Address().Replace(text.Trim().ToLower(Turkish), "")) is { Length: > 1 } target ? Command("gun_sayaci", ("hedef", target)) : null;

    private static QuickCommand? Rhythm(string sentence)
    {
        if (FocusStart().Match(sentence) is { Success: true } focus)
        {
            var minutes = ParseNumber(focus.Groups["n"].Value) is { } value ? value : 25;
            return Command("ritim", ("islem", "odak"), ("dakika", minutes.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        if (SnoozeRequest().Match(sentence) is { Success: true } snooze)
        {
            var minutes = ParseNumber(snooze.Groups["n"].Value) is { } value ? value : 15;
            return Command("ritim", ("islem", "ertele"), ("dakika", minutes.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        var action = sentence switch
        {
            _ when FocusEnd().IsMatch(sentence) => "odak_bitir",
            _ when BreakEnd().IsMatch(sentence) => "mola_bitir",
            _ when BreakRequest().IsMatch(sentence) => "mola",
            _ when BreathingRequest().IsMatch(sentence) => "nefes",
            _ when StretchRequest().IsMatch(sentence) => "esneme",
            _ when WaterLog().IsMatch(sentence) => "su",
            _ when DayStatus().IsMatch(sentence) => "durum",
            _ when WeekStatus().IsMatch(sentence) => "hafta",
            _ when WhereWasI().IsMatch(sentence) => "kaldigim_yer",
            _ => ""
        };
        return action.Length > 0 ? Command("ritim", ("islem", action)) : null;
    }

    private static QuickCommand? Routine(string sentence, IEnumerable<string> routines)
    {
        var opened = OpenObject().Match(sentence) is { Success: true } open ? open.Groups["x"].Value : null;
        var named = RoutineRun().Match(sentence) is { Success: true } run ? run.Groups["x"].Value : null;
        foreach (var routine in routines)
        {
            var name = routine.ToLower(Turkish);
            // "çalışma modu rutinini çalıştır", "sabah rutini aç" (panelin Çalıştır düğmesi de böyle gönderir).
            if (named == name)
            {
                return Command("rutin", ("ad", routine));
            }

            // "mola", "günaydın" tek başına; "çalışma modunu aç" → "çalışma modu"(nu).
            if (sentence == name || (opened is not null && (opened == name || (opened.StartsWith(name, StringComparison.Ordinal) && opened.Length - name.Length <= 3))))
            {
                return Command("rutin", ("ad", routine));
            }
        }

        return null;
    }

    private static QuickCommand? App(string sentence)
    {
        var match = OpenObject().Match(sentence);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["x"].Value.Trim();
        return name.Length < 2 || name.Split(' ').Length > 4
            ? null
            : new QuickCommand("uygulama_ac", new Dictionary<string, string> { ["ad"] = name }, Tentative: true);
    }

    // "youtube'da lofi aç", "youtube'dan power to resist şarkısını aç"
    [GeneratedRegex(@"^youtube['’]?(?:da|de|ta|te|dan|den|tan|ten)\s+(?<q>.+?)(?:\s+(?:videosunu|klibini|şarkısını|şarkısının klibini|parçasını|müziğini))?\s+aç$")]
    private static partial Regex YouTubeFirst();

    // "power to resist'i youtube'dan aç"
    [GeneratedRegex(@"^(?<q>.+?)\s+youtube['’]?(?:da|de|ta|te|dan|den|tan|ten)\s+aç$")]
    private static partial Regex YouTubeLast();

    // "tarkan'ın kuzu kuzu klibini aç", "kedi videosu aç", "naruto kokuten şarkısını aç"
    [GeneratedRegex(@"^(?<q>.+?)\s+(?:videosunu|videosu|klibini|klibi|şarkısını|şarkısının klibini|parçasını|müziğini)\s+aç$")]
    private static partial Regex VideoLast();

    [GeneratedRegex(@"^(?:şu an |şimdi )?(?:saat kaç(?: oldu)?|saati söyle(?:r misin)?|saat ne)$")]
    private static partial Regex TimeQuestion();

    [GeneratedRegex(@"^(?:bugün )?(?:ayın kaçı|günlerden ne|hangi gündeyiz|tarih ne|bugünün tarihi(?: ne)?|ne günü)$")]
    private static partial Regex DateQuestion();

    // "hava nasıl", "yarın hava nasıl olacak", "ankara'da hava nasıl", "bu hafta izmir'de hava", "yarın yağmur yağacak mı"
    [GeneratedRegex(@"^(?<w1>bugün |yarın |bu hafta |haftaya |hafta sonu )?(?:(?<city>[\p{L} ]+?)['’]?(?:da|de|ta|te) )?(?<w2>bugün |yarın |bu hafta |haftaya |hafta sonu )?(?:hava(?: durumu)?(?: nasıl(?: olacak| görünüyor)?| ne durumda)?|(?<rain>yağmur|kar) yağ(?:acak|ar|ıyor) mı)$")]
    private static partial Regex WeatherQuestion();

    // "dolar kaç", "euro ne kadar", "dolar ve euro kaç lira", "sterlin kuru"
    [GeneratedRegex(@"^(?:bugün |şu an )?(?<c>(?:dolar|euro|avro|sterlin|frank|yen|ruble)(?:(?: ve |, )(?:dolar|euro|avro|sterlin|frank|yen|ruble))*)(?: kuru)?(?: bugün| şu an)? (?:kaç(?: lira| tl| para)?|ne kadar|ne oldu|kaç oldu)$|^(?<c>dolar|euro|avro|sterlin) kuru$")]
    private static partial Regex CurrencyQuestion();

    [GeneratedRegex(@"^(?:(?:müziği|şarkıyı|videoyu|müzik|şarkı) )?(?:durdur|duraklat|dursun)$|^(?:müziği|şarkıyı|videoyu|müzik) (?:devam ettir|devam et|aç)$")]
    private static partial Regex Pause();

    [GeneratedRegex(@"^(?:bir )?(?:sonraki|diğer) (?:şarkı|parça)(?:ya geç)?$|^(?:şarkıyı|parçayı) (?:geç|değiştir|atla)$")]
    private static partial Regex Next();

    [GeneratedRegex(@"^(?:bir )?önceki (?:şarkı|parça)(?:ya (?:geç|dön))?$")]
    private static partial Regex Previous();

    [GeneratedRegex(@"^sesi (?:biraz )?(?:aç|artır|yükselt)$")]
    private static partial Regex VolumeUp();

    [GeneratedRegex(@"^sesi (?:biraz )?(?:kıs|azalt|düşür)$")]
    private static partial Regex VolumeDown();

    [GeneratedRegex(@"^(?:sesi (?:kapat|sustur)|sessize al)$")]
    private static partial Regex Mute();

    [GeneratedRegex(@"^(?:şu an )?(?:ne çalıyor|bu (?:şarkı|parça) ne|hangi (?:şarkı|parça) (?:çalıyor|bu)|çalan (?:şarkı|parça) ne)$")]
    private static partial Regex NowPlaying();

    // "10 dakika sonra beni uyar", "yarım saat sonra toplantıyı hatırlat", "beş dakikalık zamanlayıcı kur"
    [GeneratedRegex(@"^(?<n>[\p{L}\d,.]+(?: [\p{L}]+)?) (?<u>dakika|saniye|saat)(?:lık)? (?:(?:sonra|içinde) )?(?:(?<note>.+?) )??(?:için )?(?:beni |bana )?(?:uyar|uyandır|hatırlat|haber ver|alarm kur|(?:bir )?(?:zamanlayıcı|sayaç|alarm) kur)$")]
    private static partial Regex TimerRequest();

    // "odak modu", "25 dakika odaklanacağım", "pomodoro başlat", "elli dakikalık odak aç"
    [GeneratedRegex(@"^(?:(?<n>[\p{L}\d]+(?: [\p{L}]+)?) dakika(?:lık)? )?(?:odak(?: modu(?:nu)?)?(?: aç)?|odaklan(?:mam lazım|mak istiyorum|acağım|alım)|pomodoro(?: aç)?)$")]
    private static partial Regex FocusStart();

    [GeneratedRegex(@"^(?:odak(?: modu(?:nu)?)?|odağı) (?:bitir|kapat|durdur|sonlandır)$")]
    private static partial Regex FocusEnd();

    [GeneratedRegex(@"^(?:molayı (?:bitir|sonlandır|kapat)|moladan (?:döndüm|geldim)|mola bitti|(?:ben )?geri döndüm)$")]
    private static partial Regex BreakEnd();

    [GeneratedRegex(@"^yeni (?:bir )?(?:sohbet|konuşma)(?: aç)?$|^(?:sohbeti|konuşmayı) (?:sıfırla|yenile|baştan aç)$|^konuyu değiştir(?:elim)?$|^(?:hadi )?baştan başlayalım$")]
    private static partial Regex NewChat();

    [GeneratedRegex(@"^(?:bu )?(?:sohbeti|konuşmayı) (?:temizle|sil)$")]
    private static partial Regex ClearChat();

    [GeneratedRegex(@"^(?<x>.+?) (?:rutin(?:ini|i)?|protokol(?:ünü|ü)?) (?:çalıştır|aç|devreye sok)$")]
    private static partial Regex RoutineRun();

    [GeneratedRegex(@"^(?:bir )?mola(?: ver(?:elim|eyim|iyorum|mek istiyorum)?| zamanı)?$|^molaya (?:çıkıyorum|çıkalım|gidiyorum)$")]
    private static partial Regex BreakRequest();

    [GeneratedRegex(@"^(?:molayı |hatırlatmayı )?(?:(?<n>[\p{L}\d]+(?: [\p{L}]+)?) dakika )?ertele$|^(?:sonra hatırlat|şimdi değil)$")]
    private static partial Regex SnoozeRequest();

    [GeneratedRegex(@"^(?:bir )?nefes (?:egzersizi|çalışması|alalım)(?: yap(?:alım)?| aç)?$|^(?:beni )?rahatlat$")]
    private static partial Regex BreathingRequest();

    [GeneratedRegex(@"^(?:bir )?esneme(?: hareketi| hareketleri| molası)?(?: yap(?:alım)?| aç)?$")]
    private static partial Regex StretchRequest();

    [GeneratedRegex(@"^(?:bir )?(?:bardak )?su içtim$")]
    private static partial Regex WaterLog();

    [GeneratedRegex(@"^bugün (?:ne kadar (?:çalıştım|odaklandım|aktiftim)|kaç mola verdim)$|^(?:günüm nasıl geçti|ritmim nasıl)$")]
    private static partial Regex DayStatus();

    [GeneratedRegex(@"^(?:ne yapıyordum|nerede kalmıştım|kaldığım yer(?: neydi)?|en son ne yapıyordum)$")]
    private static partial Regex WhereWasI();

    // "sesi yüzde 30 yap", "sesi 40'a ayarla", "ses seviyesini yüzde 50 yap"
    [GeneratedRegex(@"^(?:sesi|ses seviyesini|sesin seviyesini) (?:yüzde )?(?<n>\d{1,3})['’]?(?:e|a|ye|ya)? (?:yap|ayarla|getir|indir|çıkar|düşür|kıs|aç)$")]
    private static partial Regex VolumeSet();

    [GeneratedRegex(@"^(?:ses seviyesi|ses) (?:kaç|ne kadar|yüzde kaç)$")]
    private static partial Regex VolumeQuestion();

    // "<zaman> <not> hatırlat"; zaman: "yarın 9'da", "saat 15:30'da", "akşam 8'de", "pazartesi sabah", "öğleden sonra 3'te"
    [GeneratedRegex(@"^(?<when>(?:(?:bugün|yarın|öbür gün|ertesi gün|pazartesi|salı|çarşamba|perşembe|cumartesi|cuma|pazar) ?)?(?:(?:sabah|öğleden sonra|öğlen|öğle|akşam|gece) ?)?(?:saat ?)?(?:\d{1,2}(?:[:.]\d{2})?(?:['’]?(?:de|da|te|ta))?)?) (?!(?:dakika|saniye|saat|gün|buçuk)\b)(?:bana )?(?<note>.+?) (?:için )?(?:beni |bana )?(?:hatırlat|hatırlatır mısın|uyar|haber ver)$")]
    private static partial Regex ReminderRequest();

    [GeneratedRegex(@"^(?:hatırlatmalarım|hatırlatıcılarım|kurulu hatırlatmalar|hangi hatırlatmalar var|hatırlatmalarımı (?:oku|söyle|listele))(?: neler| ne| var mı)?$")]
    private static partial Regex ReminderList();

    [GeneratedRegex(@"^(?<x>.+?) hatırlatma(?:sını|yı)? (?:iptal et|sil|kaldır)$")]
    private static partial Regex ReminderCancel();

    // "not al: süt al", "şunu not et: yarın bankaya git", "süt almayı not al"
    [GeneratedRegex(@"^(?:(?:bir )?not (?:al|et|ekle)|şunu not (?:al|et)|not tut)[:,]? (?<x>.+)$|^(?<x>.+?) (?:diye )?not (?:al|et)$")]
    private static partial Regex NoteTake();

    [GeneratedRegex(@"^(?:notlarımı (?:oku|söyle|göster)|notlarım (?:neler|ne)|son notum (?:ne|neydi)|notlarım)$")]
    private static partial Regex NoteRead();

    [GeneratedRegex(@"^bu hafta (?:ne kadar (?:çalıştım|odaklandım)|nasıl geçti|kaç mola verdim)$|^haftam nasıl geçti$")]
    private static partial Regex WeekStatus();

    // "spotify'ı aç", "not defterini aç", "çalışma modunu aç"
    [GeneratedRegex(@"^(?<x>[\p{L}\d .+-]{2,40}?)(?:['’]?(?:yı|yi|yu|yü|ı|i|u|ü|nı|ni|nu|nü))? aç$")]
    private static partial Regex OpenObject();

    [GeneratedRegex(@"[.!?]+\s+")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"^(?:hey\s+)?friday[,\s]+")]
    private static partial Regex Address();

    [GeneratedRegex(@"\s+(?:lütfen|bana|benim için)(?=\s|$)")]
    private static partial Regex Politeness();

    // "aç" anlamına gelen söyleyişler: oynat, çal, başlat, dinlemek istiyorum, izlemek istiyorum, açar mısın…
    [GeneratedRegex(@"\s+(?:açar mısın|açabilir misin|açsana|açıver|aç|oynatır mısın|oynatabilir misin|oynatsana|oynat|çalar mısın|çalabilir misin|çalsana|çal|başlat|başlatır mısın|dinlemek istiyorum|dinlemek isterim|dinleyelim|dinlet|izlemek istiyorum|izlemek isterim|izleyelim|izlet)$")]
    private static partial Regex OpenVerbs();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
