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
    DayStarted
}

public sealed record RhythmEvent(RhythmEventKind Kind, TimeSpan Length);

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

    public bool IsAway => _awaySince is not null;

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
                    else if (BreakStart is not null || suggested)
                    {
                        events.Add(new RhythmEvent(RhythmEventKind.BackFromBreak, gone));
                    }

                    BreakStart = null;
                }
            }

            _stretchStart ??= lastInput;
            Today.ActiveMinutes += elapsed.TotalMinutes;
            if (InFocus)
            {
                Today.FocusMinutes += elapsed.TotalMinutes;
            }

            Extend(now, elapsed, InFocus ? RhythmSegmentKind.Focus : RhythmSegmentKind.Work);
            Today.LongestStretchMinutes = Math.Max(Today.LongestStretchMinutes, CurrentStretch(now).TotalMinutes);
        }

        if (FocusEnd is { } end && now >= end)
        {
            events.Add(new RhythmEvent(RhythmEventKind.FocusEnded, end - FocusStart!.Value));
            FocusStart = null;
            FocusEnd = null;
            LastSuggestion = now;
        }

        if (BreakStart is { } started && _awaySince is null && now - started >= BreakLength)
        {
            events.Add(new RhythmEvent(RhythmEventKind.BreakOver, now - started));
            BreakStart = null;
            _stretchStart = now;
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

    public void StartBreak(DateTime now)
    {
        BreakStart = now;
        LastSuggestion = now;
        CloseStretch(now);
    }

    public void StartFocus(DateTime now, TimeSpan length)
    {
        FocusStart = now;
        FocusEnd = now + length;
    }

    /// <returns>Odakta geçen süre; odak yoksa sıfır.</returns>
    public TimeSpan EndFocus(DateTime now)
    {
        var spent = FocusStart is { } start ? now - start : TimeSpan.Zero;
        FocusStart = null;
        FocusEnd = null;
        return spent;
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
