using System.Globalization;

namespace Friday.Core;

public enum RhythmSegmentKind
{
    Work,
    Focus
}

/// <summary>Günün zaman çizelgesindeki bir çalışma ya da odak aralığı; aralardaki boşluklar moladır.</summary>
public sealed record RhythmSegment(DateTime Start, DateTime End, RhythmSegmentKind Kind);

/// <summary>Bir günün ritmi (D:\Friday\data\rhythm\yyyy-aa-gg.json). Yalnızca süreler tutulur; ne yapıldığı tutulmaz.</summary>
public sealed class RhythmDay
{
    public DateOnly Date { get; set; }

    public double ActiveMinutes { get; set; }

    public double FocusMinutes { get; set; }

    public int Breaks { get; set; }

    public double LongestStretchMinutes { get; set; }

    public int Water { get; set; }

    public List<RhythmSegment> Segments { get; set; } = [];

    /// <summary>Biten odak oturumları ve (söylendiyse) hangi iş için oldukları.</summary>
    public List<FocusSession> Sessions { get; set; } = [];
}

public enum RhythmEventKind
{
    /// <summary>Uzun süredir aralıksız çalışılıyor; bir mola önerilmeli.</summary>
    SuggestBreak,

    /// <summary>Odak oturumu doldu.</summary>
    FocusEnded,

    /// <summary>Verilen mola süresi doldu ama kullanıcı bilgisayardan hiç kalkmadı.</summary>
    BreakOver,

    /// <summary>Kullanıcı önerilen ya da istediği bir moladan döndü.</summary>
    BackFromBreak,

    /// <summary>Günün ilk oturuşu (en az 4 saatlik aradan sonra, sabah).</summary>
    DayStarted,

    /// <summary>Pomodoro serisinde bir tur bitti; kısa mola kendiliğinden başladı.</summary>
    SeriesBreak,

    /// <summary>Seri molası bitti; sıradaki odak turu kendiliğinden başladı.</summary>
    SeriesNext,

    /// <summary>Pomodoro serisinin son turu bitti.</summary>
    SeriesDone
}

/// <param name="Round">Pomodoro serisindeki tur (seri olaylarında); diğerlerinde 0.</param>
public sealed record RhythmEvent(RhythmEventKind Kind, TimeSpan Length, int Round = 0, int Rounds = 0);

/// <summary>
/// Çalışma ritminin saf mantığı: son klavye/fare kullanımından bu yana geçen süreye bakarak aralıksız çalışmayı, molaları,
/// odak oturumlarını ve günün özetini tutar. Saat dışarıdan verilir; bu sayede tamamen test edilebilir.
/// </summary>
public sealed class RhythmTracker
{
    /// <summary>Bu kadar etkinlik olmazsa kullanıcı bilgisayardan kalkmış sayılır.</summary>
    public static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan SuggestionGap = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan Insist = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan NaturalPause = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NightGap = TimeSpan.FromHours(4);

    private DateTime? _stretchStart;
    private DateTime? _awaySince;
    private DateTime _lastUpdate;

    public RhythmTracker(TimeSpan breakEvery, TimeSpan breakLength, RhythmDay? today = null)
    {
        BreakEvery = breakEvery;
        BreakLength = breakLength;
        Today = today ?? new RhythmDay { Date = DateOnly.FromDateTime(DateTime.Now) };
    }

    public TimeSpan BreakEvery { get; set; }

    public TimeSpan BreakLength { get; set; }

    public RhythmDay Today { get; private set; }

    public DateTime? FocusStart { get; private set; }

    public DateTime? FocusEnd { get; private set; }

    /// <summary>İstenen ya da önerilip başlatılan molanın başlangıcı.</summary>
    public DateTime? BreakStart { get; private set; }

    public DateTime? SnoozedUntil { get; private set; }

    public DateTime? LastSuggestion { get; private set; }

    public bool InFocus => FocusEnd is not null;

    /// <summary>Odak duraklatıldıysa ne zaman; duraklatılmış odakta süre işlemez, bitiş ileri kayar.</summary>
    public DateTime? FocusPausedAt { get; private set; }

    /// <summary>Pomodoro serisinde kaçıncı tur (1'den başlar); seri yoksa 0.</summary>
    public int SeriesRound { get; private set; }

    public int SeriesRounds { get; private set; }

    /// <summary>Süren molanın uzunluğu: seri molası 5 dakika, diğerleri ayardaki mola süresi.</summary>
    public TimeSpan CurrentBreakLength => _breakFor ?? BreakLength;

    public static readonly TimeSpan SeriesFocus = TimeSpan.FromMinutes(25);

    public static readonly TimeSpan SeriesRest = TimeSpan.FromMinutes(5);

    private TimeSpan? _breakFor;

    /// <summary>Süren odağın işi ("Raporu bitir"); iş söylenmediyse boş.</summary>
    public string FocusTask { get; private set; } = "";

    private DateTime _focusBegan;

    public bool IsAway => _awaySince is not null;

    /// <summary>Şu anki aralıksız çalışmanın başlangıcı; çalışılmıyorsa null.</summary>
    public DateTime? StretchStart => _stretchStart;

    public TimeSpan CurrentStretch(DateTime now) => _stretchStart is { } start && now > start ? now - start : TimeSpan.Zero;

    /// <summary>Her yoklamada (ör. 20 sn'de bir) çağrılır; gereken olayları döndürür.</summary>
    /// <param name="idle">Son klavye ya da fare kullanımından bu yana geçen süre.</param>
    public IReadOnlyList<RhythmEvent> Update(DateTime now, TimeSpan idle)
    {
        var events = new List<RhythmEvent>();
        var elapsed = _lastUpdate == default ? TimeSpan.Zero : now - _lastUpdate;
        elapsed = elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromMinutes(5) ? TimeSpan.Zero : elapsed;
        _lastUpdate = now;
        var lastInput = now - idle;

        if (DateOnly.FromDateTime(now) != Today.Date)
        {
            Today = new RhythmDay { Date = DateOnly.FromDateTime(now) };
            _stretchStart = null;
            LastSuggestion = null;
            SnoozedUntil = null;
        }

        if (idle >= AwayAfter)
        {
            if (_awaySince is null)
            {
                _awaySince = lastInput;
                CloseStretch(lastInput);
            }
        }
        else
        {
            if (_awaySince is { } away)
            {
                _awaySince = null;
                var gone = lastInput - away;
                if (gone >= AwayAfter)
                {
                    Today.Breaks++;
                    var suggested = LastSuggestion is { } said && said >= away - TimeSpan.FromMinutes(10);
                    if (gone >= NightGap && now.Hour is >= 5 and < 12)
                    {
                        events.Add(new RhythmEvent(RhythmEventKind.DayStarted, gone));
                    }
                    else if (SeriesRound == 0 && (BreakStart is not null || suggested))
                    {
                        events.Add(new RhythmEvent(RhythmEventKind.BackFromBreak, gone));
                    }

                    if (SeriesRound == 0)
                    {
                        BreakStart = null;
                        _breakFor = null;
                    }
                }
            }

            _stretchStart ??= lastInput;
            Today.ActiveMinutes += elapsed.TotalMinutes;
            var focusing = InFocus && FocusPausedAt is null;
            if (focusing)
            {
                Today.FocusMinutes += elapsed.TotalMinutes;
            }

            Extend(now, elapsed, focusing ? RhythmSegmentKind.Focus : RhythmSegmentKind.Work);
            Today.LongestStretchMinutes = Math.Max(Today.LongestStretchMinutes, CurrentStretch(now).TotalMinutes);
        }

        if (FocusEnd is { } end && FocusPausedAt is null && now >= end)
        {
            var length = end - FocusStart!.Value;
            CloseFocus(end);
            LastSuggestion = now;
            if (SeriesRound == 0)
            {
                events.Add(new RhythmEvent(RhythmEventKind.FocusEnded, length));
            }
            else if (SeriesRound < SeriesRounds)
            {
                events.Add(new RhythmEvent(RhythmEventKind.SeriesBreak, length, SeriesRound, SeriesRounds));
                StartBreak(now, SeriesRest);
            }
            else
            {
                events.Add(new RhythmEvent(RhythmEventKind.SeriesDone, length, SeriesRound, SeriesRounds));
                StopSeries();
            }
        }

        if (BreakStart is { } started && _awaySince is null && now - started >= CurrentBreakLength)
        {
            BreakStart = null;
            _breakFor = null;
            _stretchStart = now;
            if (SeriesRound > 0)
            {
                NextRound(now);
                events.Add(new RhythmEvent(RhythmEventKind.SeriesNext, now - started, SeriesRound, SeriesRounds));
            }
            else
            {
                events.Add(new RhythmEvent(RhythmEventKind.BreakOver, now - started));
            }
        }

        var stretch = CurrentStretch(now);
        if (_awaySince is null && !InFocus && BreakStart is null && stretch >= BreakEvery
            && (SnoozedUntil is null || now >= SnoozedUntil)
            && (LastSuggestion is null || now - LastSuggestion >= SuggestionGap)
            // Yazarken araya girilmez; kısa bir duraksama beklenir. Çok uzarsa yine de söylenir.
            && (idle >= NaturalPause || stretch >= BreakEvery + Insist))
        {
            events.Add(new RhythmEvent(RhythmEventKind.SuggestBreak, stretch));
            LastSuggestion = now;
        }

        return events;
    }

    /// <summary>Öneri uygun olmayan bir anda geldiyse (tam ekran, Friday konuşuyor) birkaç dakika sonra yeniden denenir.</summary>
    public void Postpone(DateTime now, TimeSpan delay)
    {
        LastSuggestion = null;
        SnoozedUntil = now + delay;
    }

    /// <summary>Mola başlar; süren odak (duraklatılmış olsa da) biter.</summary>
    public void StartBreak(DateTime now, TimeSpan? length = null)
    {
        CloseFocus(now);
        BreakStart = now;
        _breakFor = length;
        LastSuggestion = now;
        CloseStretch(now);
    }

    /// <summary>Molayı erken bitirir ("moladan döndüm"); süre yeniden sayılmaya başlar.</summary>
    /// <returns>Molada geçen süre; mola yoksa sıfır.</returns>
    public TimeSpan EndBreak(DateTime now)
    {
        if (BreakStart is not { } started)
        {
            return TimeSpan.Zero;
        }

        BreakStart = null;
        _breakFor = null;
        _stretchStart = now;
        return now - started;
    }

    /// <summary>Odak başlar; süren mola biter, süren odak (varsa) kaydedilip yerini yenisine bırakır.</summary>
    /// <param name="task">Odağın işi ("Raporu bitir"); söylenmediyse boş.</param>
    public void StartFocus(DateTime now, TimeSpan length, string task = "")
    {
        if (BreakStart is not null)
        {
            BreakStart = null;
            _breakFor = null;
            _stretchStart = now;
        }

        CloseFocus(now);
        FocusStart = now;
        FocusEnd = now + length;
        FocusPausedAt = null;
        FocusTask = task.Trim();
        _focusBegan = now;
    }

    /// <returns>Odakta geçen süre (duraklatılan kısım hariç); odak yoksa sıfır.</returns>
    public TimeSpan EndFocus(DateTime now)
    {
        var spent = FocusStart is { } start ? (FocusPausedAt ?? now) - start : TimeSpan.Zero;
        CloseFocus(now);
        return spent;
    }

    /// <summary>Süren odak biter; bir dakikayı geçtiyse günün oturumlarına yazılır.</summary>
    private void CloseFocus(DateTime at)
    {
        if (FocusStart is { } start)
        {
            var minutes = ((FocusPausedAt ?? at) - start).TotalMinutes;
            if (minutes >= 1)
            {
                Today.Sessions.Add(new FocusSession(_focusBegan == default ? start : _focusBegan, Math.Round(minutes, 1), FocusTask));
            }
        }

        FocusStart = null;
        FocusEnd = null;
        FocusPausedAt = null;
        FocusTask = "";
    }

    /// <summary>Odak durur (telefon, kapıdaki kurye); kalan süre korunur.</summary>
    /// <returns>Duraklatılacak bir odak varsa true.</returns>
    public bool PauseFocus(DateTime now)
    {
        if (FocusEnd is null || FocusPausedAt is not null)
        {
            return false;
        }

        FocusPausedAt = now;
        return true;
    }

    /// <summary>Duraklatılan odak kaldığı yerden sürer: başlangıç ve bitiş, duraklamanın süresi kadar ileri kayar.</summary>
    /// <returns>Duraklatılmış bir odak varsa true.</returns>
    public bool ResumeFocus(DateTime now)
    {
        if (FocusPausedAt is not { } paused || FocusEnd is not { } end || FocusStart is not { } start)
        {
            return false;
        }

        var gap = now > paused ? now - paused : TimeSpan.Zero;
        FocusStart = start + gap;
        FocusEnd = end + gap;
        FocusPausedAt = null;
        return true;
    }

    /// <summary>Süren odağı uzatır.</summary>
    /// <returns>Uzatılacak bir odak varsa true.</returns>
    public bool ExtendFocus(TimeSpan by)
    {
        if (FocusEnd is not { } end)
        {
            return false;
        }

        FocusEnd = end + by;
        return true;
    }

    /// <summary>Pomodoro serisi: 25 dakika odak, 5 dakika mola; turlar kendiliğinden ilerler, duraklatma ve bitirme her an geçerlidir.</summary>
    public void StartSeries(DateTime now, int rounds, string task = "")
    {
        SeriesRounds = Math.Clamp(rounds, 1, 8);
        SeriesRound = 1;
        _seriesTask = task.Trim();
        StartFocus(now, SeriesFocus, _seriesTask);
    }

    private string _seriesTask = "";

    /// <summary>Seri biter; süren odak ya da mola olduğu gibi kalır (onları çağıran bitirir).</summary>
    public void StopSeries()
    {
        SeriesRound = 0;
        SeriesRounds = 0;
    }

    /// <summary>Seri molası bitti ya da atlandı: sıradaki tur başlar.</summary>
    public void NextRound(DateTime now)
    {
        if (SeriesRound == 0)
        {
            return;
        }

        SeriesRound = Math.Min(SeriesRound + 1, SeriesRounds);
        StartFocus(now, SeriesFocus, _seriesTask);
    }

    public void Snooze(DateTime now, TimeSpan length) => SnoozedUntil = now + length;

    public int DrinkWater() => ++Today.Water;

    private void CloseStretch(DateTime at)
    {
        if (_stretchStart is { } start && at > start)
        {
            Today.LongestStretchMinutes = Math.Max(Today.LongestStretchMinutes, (at - start).TotalMinutes);
        }

        _stretchStart = null;
    }

    /// <summary>Zaman çizelgesinde son aralığı uzatır ya da (tür değiştiyse, arada mola varsa) yenisini başlatır.</summary>
    private void Extend(DateTime now, TimeSpan elapsed, RhythmSegmentKind kind)
    {
        var segments = Today.Segments;
        if (segments.Count > 0 && segments[^1].Kind == kind && now - segments[^1].End <= TimeSpan.FromMinutes(2))
        {
            segments[^1] = segments[^1] with { End = now };
        }
        else
        {
            segments.Add(new RhythmSegment(now - elapsed, now, kind));
        }
    }
}

/// <summary>Koçun söylediği bir cümle ve ardından beklenecek süre (nefes egzersizi, göz molası).</summary>
public sealed record CoachStep(string Text, double PauseSeconds = 0);

/// <summary>
/// Ritim koçunun cümleleri. Aynı öneri art arda gelmez; günün saatine, aralıksız sürenin uzunluğuna ve içilen suya göre
/// seçilir. Göz molası, esneme ve nefes egzersizi sesli rehberdir: Friday söyler, bekler, sıradakini söyler.
/// </summary>
public sealed class RhythmCoachLines(Random random)
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private string _last = "";

    public static string Span(TimeSpan span) => TurkishGrammar.Duration(Math.Max(1, Math.Round(span.TotalMinutes)));

    public IReadOnlyList<CoachStep> Suggestion(TimeSpan stretch, DateTime now, RhythmDay today)
    {
        var length = Span(stretch);
        string kind;
        if (now.Hour >= 22 || now.Hour < 4)
        {
            kind = "gece";
        }
        else if (stretch >= TimeSpan.FromMinutes(90))
        {
            kind = "yuru";
        }
        else
        {
            var options = new List<string> { "goz", "esneme", "nefes" };
            if (now.Hour >= 10 && today.Water < 6)
            {
                options.Add("su");
                options.Add("su");
            }

            options.RemoveAll(option => option == _last);
            kind = options[random.Next(options.Count)];
        }

        _last = kind;
        return kind switch
        {
            "gece" => [new($"Saat {now.ToString("HH:mm", CultureInfo.InvariantCulture)} oldu ve bugün {Span(TimeSpan.FromMinutes(today.ActiveMinutes))} çalıştın. Yarına da biraz enerji bırakmaya ne dersin?")],
            "yuru" => [new($"Aralıksız {length} oldu ve hiç kalkmadın. Beş dakika yürü, biraz hava al; döndüğünde kaldığın yeri söylerim.")],
            "su" => [new(today.Water == 0
                ? $"Aralıksız {length} oldu. Kalkıp bir bardak su içmeye ne dersin? İçince bana su içtim de, sayayım."
                : $"Aralıksız {length} oldu. Bugün {today.Water} bardak su içtin; bir tane daha iyi gelir.")],
            "goz" =>
            [
                new($"Aralıksız {length} oldu. Gözlerini ekrandan ayır ve 20 saniye uzaktaki bir noktaya bak; ben sayıyorum.", 20),
                new("Tamam, gözlerin dinlendi. Kaldığın yerden devam.")
            ],
            "esneme" => Stretch($"Aralıksız {length} oldu, kısa bir esneme molası."),
            _ => [new($"Aralıksız {length} oldu. Bir dakikalık nefes molası iyi gelebilir; nefes egzersizi demen yeter.")]
        };
    }

    public static IReadOnlyList<CoachStep> Stretch(string opening = "Bir dakikalık esneme yapalım.") =>
    [
        new($"{opening} Omuzlarını beş kez yavaşça geriye doğru çevir.", 7),
        new("Şimdi başını sağa yatır, beş saniye tut.", 6),
        new("Sola.", 6),
        new("Son olarak parmaklarını kenetle ve kollarını yukarı uzat.", 5),
        new("Bu kadar. Vücudun teşekkür ediyor.")
    ];

    public static IReadOnlyList<CoachStep> Breathing()
    {
        var steps = new List<CoachStep> { new("Nefes egzersizine başlıyoruz. Rahat otur, omuzlarını bırak.", 1.5) };
        for (var round = 0; round < 3; round++)
        {
            steps.Add(new CoachStep(round == 0 ? "Burnundan derin bir nefes al." : "Nefes al.", 4));
            steps.Add(new CoachStep("Tut.", 4));
            steps.Add(new CoachStep(round == 0 ? "Şimdi ağzından yavaşça ver." : "Ver.", 6));
        }

        steps.Add(new CoachStep("Harika. Zihnin biraz daha berrak olmalı."));
        return steps;
    }

    public static string FocusStarted(TimeSpan length) =>
        $"Tamam, {Span(length)} odak. Bu sürede araya girmem; bitince haber veririm.";

    public static string FocusEnded(TimeSpan length, RhythmDay today, TimeSpan breakLength) =>
        $"{TurkishGrammar.Capitalize(Span(length))} odak tamamlandı, harika iş. Bugün toplam {Span(TimeSpan.FromMinutes(today.FocusMinutes))} odaklandın. " +
        $"Şimdi {Span(breakLength)} mola hak ettin.";

    public static string BreakStarted(TimeSpan length) =>
        $"Mola başladı. {TurkishGrammar.Capitalize(Span(length))} boyunca ekrandan uzaklaş; döndüğünde kaldığın yeri söylerim.";

    public static string BreakOver(string? where) =>
        "Mola süresi doldu. Hazırsan devam edelim." + (where is null ? "" : $" Kaldığın yer: {where}.");

    public static string Welcome(TimeSpan away, string? where) =>
        $"Tekrar hoş geldin, {Span(away)} mola verdin." + (where is null ? "" : $" Kaldığın yer: {where}.");

    public static string Snoozed(TimeSpan length) => $"Tamam, {Span(length)} sonra tekrar hatırlatırım.";

    public static string Water(int glasses) => glasses switch
    {
        1 => "İlk bardak, güzel başlangıç.",
        >= 8 => $"{glasses} bardak! Bugün su konusunda şampiyonsun.",
        < 4 => $"Bugün {glasses}. bardak, böyle devam.",
        _ => $"Bugün {glasses} bardak oldu, harika."
    };

    public static string Status(RhythmDay today, TimeSpan currentStretch)
    {
        if (today.ActiveMinutes < 1)
        {
            return "Bugün henüz bilgisayar başında pek vakit geçirmedin.";
        }

        var parts = new List<string> { $"Bugün {Span(TimeSpan.FromMinutes(today.ActiveMinutes))} aktiftin" };
        if (today.FocusMinutes >= 1)
        {
            parts.Add($"{Span(TimeSpan.FromMinutes(today.FocusMinutes))} odak");
        }

        parts.Add(today.Breaks == 0 ? "hiç mola vermedin" : $"{today.Breaks} mola verdin");
        var text = TurkishGrammar.Join(parts) + ".";
        if (today.LongestStretchMinutes >= 30)
        {
            text += $" En uzun aralıksız çalışman {Span(TimeSpan.FromMinutes(today.LongestStretchMinutes))}.";
        }

        if (currentStretch >= TimeSpan.FromMinutes(20))
        {
            text += $" Şu an aralıksız {Span(currentStretch)} oldu.";
        }

        if (today.Water > 0)
        {
            text += $" {today.Water} bardak su içtin.";
        }

        return text;
    }

    /// <summary>"Bu hafta 18 saat 20 dakika aktiftin, 4 saati odak. En yoğun günün salıydı (5 saat 10 dakika)."</summary>
    public static string Week(IReadOnlyList<RhythmDay> days)
    {
        var active = days.Sum(day => day.ActiveMinutes);
        if (active < 1)
        {
            return "Bu hafta için henüz bir ritim kaydım yok.";
        }

        var focus = days.Sum(day => day.FocusMinutes);
        var busiest = days.MaxBy(day => day.ActiveMinutes)!;
        var text = $"Bu hafta toplam {Span(TimeSpan.FromMinutes(active))} aktiftin";
        text += focus >= 1 ? $", {Span(TimeSpan.FromMinutes(focus))} odak." : ".";
        text += $" En yoğun günün {busiest.Date.ToString("dddd", Turkish).ToLower(Turkish)} ({Span(TimeSpan.FromMinutes(busiest.ActiveMinutes))}).";
        var breaks = days.Sum(day => day.Breaks);
        if (breaks > 0)
        {
            text += $" {breaks} mola verdin.";
        }

        return text;
    }

    public static string Where(string? where) =>
        where is null ? "Henüz bir mola kaydım yok, o yüzden bilmiyorum." : $"Son molandan önce {where} açıktı.";

    /// <summary>"FridayAssistant.cs - Friday - Visual Studio Code" → "Visual Studio Code, FridayAssistant.cs".</summary>
    public static string? DescribeWindow(string? title, string? process)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var parts = title.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[^1]}, {parts[0]}";
        }

        return string.IsNullOrWhiteSpace(process) ? title.Trim() : $"{process}, {title.Trim()}";
    }

    public static string DayLabel(DateOnly date) => date.ToString("d MMMM dddd", Turkish);
}
