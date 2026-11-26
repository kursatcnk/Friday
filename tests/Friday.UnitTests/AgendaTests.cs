using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

/// <summary>Haftalık tekrar eden bilgiler: "pazartesi ve çarşamba akşamları yüzmeye gidiyor".</summary>
public class WeeklyFactsTests
{
    // 24 Kasım 2026 salı.
    private static readonly DateOnly Tuesday = new(2026, 11, 24);

    [Theory]
    [InlineData("Pazartesi ve çarşamba akşamları yüzmeye gidiyor.", new[] { DayOfWeek.Monday, DayOfWeek.Wednesday })]
    [InlineData("Her cuma annesini arıyor", new[] { DayOfWeek.Friday })]
    [InlineData("Salıları pilatese gidiyor", new[] { DayOfWeek.Tuesday })]
    [InlineData("Cumartesi günleri pazara gidiyor", new[] { DayOfWeek.Saturday })]
    [InlineData("Hafta sonları koşuya çıkıyor", new[] { DayOfWeek.Saturday, DayOfWeek.Sunday })]
    [InlineData("Perşembe sabahları erkenden ofiste", new[] { DayOfWeek.Thursday })]
    public void ARepeatingFactIsReadWithItsDays(string text, DayOfWeek[] days)
    {
        var fact = WeeklyFacts.Parse(text);
        Assert.NotNull(fact);
        Assert.Equal(days.OrderBy(day => day), fact!.Days.OrderBy(day => day));
    }

    [Theory]
    [InlineData("Pazartesi toplantısı var")]
    [InlineData("Dün pazara gittim")]
    [InlineData("Kızının adı Ela, 7 yaşında")]
    [InlineData("Cuma günü doktora gidecek")]
    public void AOneOffDayIsNotARepeat(string text) => Assert.Null(WeeklyFacts.Parse(text));

    [Fact]
    public void TheNoteSaysWhetherTodayAndTomorrowMatch()
    {
        var fact = WeeklyFacts.Parse("Pazartesi ve çarşamba akşamları yüzmeye gidiyor")!;
        var note = WeeklyFacts.Note(fact, Tuesday);
        Assert.Contains("bugün (salı) yok", note, StringComparison.Ordinal);
        Assert.Contains("yarın (çarşamba) var", note, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenNeitherDayMatchesTheNextOneIsGiven()
    {
        var note = WeeklyFacts.Note(WeeklyFacts.Parse("Her cuma annesini arıyor")!, Tuesday);
        Assert.Contains("sıradaki cuma (3 gün sonra)", note, StringComparison.Ordinal);
    }

    [Fact]
    public void DatedMemoriesAreNotTreatedAsWeekly()
    {
        var memories = new[]
        {
            new MemoryItem("1", "Pazartesi ve çarşamba akşamları yüzmeye gidiyor", DateTimeOffset.Now),
            new MemoryItem("2", "Annesinin doğum günü 12 Mart", DateTimeOffset.Now),
        };
        Assert.Single(WeeklyFacts.From(memories, Tuesday));
    }
}

/// <summary>Bir günün gündemi: takvim, hatırlatmalar, son günü gelen işler, özel günler, ödemeler, haftalık tekrarlar.</summary>
public class DayAgendaTests
{
    // 1 Aralık 2026 salı; yakında resmî/özel gün yok.
    private static readonly DateTime Now = new(2026, 12, 1, 14, 0, 0);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private static Reminder At(DateTime due, string text, string? repeat = null) => new(Guid.NewGuid().ToString("N"), due, text, Now) { Repeat = repeat };

    private static TodoItem Todo(string text, DateOnly? due) => new(Guid.NewGuid().ToString("N"), text, Now.AddDays(-3), Due: due);

    [Fact]
    public void TomorrowWithoutACalendarStillHasRemindersDeadlinesAndRoutines()
    {
        var agenda = DayAgenda.Build(Today.AddDays(1), Today.AddDays(2), Now, [],
            [At(Now.Date.AddDays(1).AddHours(9), "Dişçi randevusu"), At(Now.Date.AddDays(3).AddHours(9), "Toplantı")],
            [Todo("Ela'nın okul formunu imzala", Today.AddDays(1)), Todo("Sunum slaytlarını bitir", Today.AddDays(3))],
            [new MemoryItem("1", "Pazartesi ve çarşamba akşamları yüzmeye gidiyor", DateTimeOffset.Now)], []);

        Assert.Equal([AgendaKind.Reminder, AgendaKind.Deadline, AgendaKind.Weekly], agenda.Select(item => item.Kind).OrderBy(kind => kind));
        var spoken = DayAgenda.Describe(agenda, Today.AddDays(1), Today.AddDays(2), Today, "Yarın");
        Assert.StartsWith("Yarın 09:00'da Dişçi randevusu var", spoken, StringComparison.Ordinal);
        Assert.Contains("son günü gelen iş: Ela'nın okul formunu imzala", spoken, StringComparison.Ordinal);
        Assert.Contains("haftalık düzenin: pazartesi ve çarşamba akşamları yüzmeye gidiyor", spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void LateWorkShowsUpOnlyForToday()
    {
        IReadOnlyList<TodoItem> todos = [Todo("Kirayı öde", Today.AddDays(-2))];
        Assert.Contains(DayAgenda.Build(Today, Today.AddDays(1), Now, [], [], todos, [], []), item => item.Kind == AgendaKind.Overdue);
        Assert.Empty(DayAgenda.Build(Today.AddDays(1), Today.AddDays(2), Now, [], [], todos, [], []));
    }

    [Fact]
    public void ADailyReminderAppearsOnEachDayOfTheWeek()
    {
        var agenda = DayAgenda.Build(Today, Today.AddDays(7), Now, [], [At(Now.Date.AddHours(21), "İlacını al", "gunluk")], [], [], []);
        Assert.Equal(7, agenda.Count(item => item.Text == "İlacını al"));
    }

    [Fact]
    public void PastRemindersOfTodayAreLeftOut()
    {
        var agenda = DayAgenda.Build(Today, Today.AddDays(1), Now, [], [At(Now.Date.AddHours(9), "Sabah toplantısı"), At(Now.Date.AddHours(18), "Mert'i ara")], [], [], []);
        Assert.Equal(["Mert'i ara"], agenda.Select(item => item.Text));
    }

    [Fact]
    public void BillsAndBirthdaysAreOnTheirDay()
    {
        var agenda = DayAgenda.Build(Today, Today.AddDays(7), Now, [], [], [],
            [new MemoryItem("1", "Mert'in doğum günü 4 Aralık", DateTimeOffset.Now)], [new RecurringBill("İnternet", 450, 3)]);
        Assert.Contains(agenda, item => item.Kind == AgendaKind.Bill && item.Day == new DateOnly(2026, 12, 3));
        Assert.Contains(agenda, item => item.Kind == AgendaKind.SpecialDay && item.Day == new DateOnly(2026, 12, 4));
    }

    [Fact]
    public void CalendarEventsAreTimedAndOrdered()
    {
        var agenda = DayAgenda.Build(Today, Today.AddDays(1), Now, [new CalendarEvent("Bütçe toplantısı", Now.Date.AddHours(16), Now.Date.AddHours(17), false, null)],
            [At(Now.Date.AddHours(15), "Su iç")], [], [], []);
        Assert.Equal(["Su iç", "Bütçe toplantısı"], agenda.Select(item => item.Text));
        Assert.Equal("Bugün 15:00'te Su iç ve 16:00'da Bütçe toplantısı var.", DayAgenda.Describe(agenda, Today, Today.AddDays(1), Today, "Bugün"));
    }

    [Fact]
    public void SeveralDaysAreToldDayByDay()
    {
        var agenda = DayAgenda.Build(Today, Today.AddDays(7), Now, [], [At(Now.Date.AddHours(18), "Mert'i ara"), At(Now.Date.AddDays(2).AddHours(10), "Kuaför")], [], [], []);
        var spoken = DayAgenda.Describe(agenda, Today, Today.AddDays(7), Today, "Önümüzdeki 7 günde");
        Assert.Contains("Bugün: 18:00'de Mert'i ara.", spoken, StringComparison.Ordinal);
        Assert.Contains("Perşembe: 10:00'da Kuaför.", spoken, StringComparison.Ordinal);
    }
}

/// <summary>Uçtan uca: takvim bağlı değilken "yarın neler var" hatırlatmaları ve işleri söyler; bağlam satırları tarih taşır.</summary>
public sealed class AgendaToolTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;
    private readonly ReminderStore _reminders;
    private readonly TodoStore _todos;
    private readonly MemoryStore _memory;

    public AgendaToolTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _reminders = new ReminderStore(Path.Combine(_temp.Path, "reminders.json"));
        _todos = new TodoStore(Path.Combine(_temp.Path, "todos.json"));
        _memory = new MemoryStore(Path.Combine(_temp.Path, "memory.json"));
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)), _memory,
            new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")), _reminders, new NoteStore(Path.Combine(_temp.Path, "notes.md")))
        {
            Todos = _todos
        };
    }

    [Fact]
    public async Task TomorrowsAgendaNeedsNoCalendar()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        _reminders.Add(tomorrow.AddHours(9), "Dişçi randevusu", DateTime.Now);
        _todos.Add("Ela'nın okul formunu imzala", DateTime.Now, DateOnly.FromDateTime(tomorrow));

        var reply = await _tools.RunQuickAsync(QuickIntents.Match("yarın neler var")!, CancellationToken.None);

        Assert.Contains("09:00", reply!.Text, StringComparison.Ordinal);
        Assert.Contains("Dişçi randevusu", reply.Text, StringComparison.Ordinal);
        Assert.Contains("okul formunu imzala", reply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("bağlamadık", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyDayMentionsTheCalendarOnlyOnce()
    {
        var reply = await _tools.RunQuickAsync(QuickIntents.Match("yarın neler var")!, CancellationToken.None);
        Assert.StartsWith("Yarın kayıtlarında bir şey yok.", reply!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenTaskCarriesItsDeadline()
    {
        _todos.Add("Ela'nın okul formunu imzala", DateTime.Now, DateOnly.FromDateTime(DateTime.Today.AddDays(3)));
        Assert.Contains("son gün", _tools.PersonalContext("Ela'nın okul formunu ne zamana kadar imzalamam lazım"), StringComparison.Ordinal);
    }

    [Fact]
    public void AWeeklyRoutineIsResolvedAgainstToday()
    {
        _memory.Remember("Pazartesi ve çarşamba akşamları yüzmeye gidiyor");
        var monday = new DateTime(2026, 11, 23, 12, 0, 0);
        Assert.Contains("bugün (pazartesi) var", _tools.WeeklyContext("bugün yüzmeye gidiyor muyum", monday), StringComparison.Ordinal);
        Assert.Contains("yarın (salı) yok", _tools.WeeklyContext("yarın akşam planım var mı", monday), StringComparison.Ordinal);
        Assert.Equal("", _tools.WeeklyContext("Ankara'nın nüfusu kaç", monday));
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}

/// <summary>Hızlı komutların ayrıntıları: yazılış, baştaki açıklama, "yaptım" sanılan sorular.</summary>
public class QuickDetailTests
{
    [Fact]
    public void AReminderKeepsTheWayNamesWereWritten()
    {
        var command = QuickIntents.Match("yarın akşam yedide Ela'yı yüzmeden almayı hatırlat");
        Assert.Equal("hatirlatici", command?.Tool);
        Assert.Equal("Ela'yı yüzmeden almayı", command!.Arguments["metin"]);
    }

    [Fact]
    public void ANoteKeepsItsCapitals() =>
        Assert.Equal("Mert'in toplantısı perşembeye kaydı", QuickIntents.Match("bunu not al: Mert'in toplantısı perşembeye kaydı")!.Arguments["metin"]);

    [Fact]
    public void ALeadInBeforeAListCommandIsNotAnItem()
    {
        var command = QuickIntents.Match("markete gitmem lazım, listeme ekmek ve yumurta ekle");
        Assert.Equal("liste", command?.Tool);
        Assert.Equal("ekmek ve yumurta", command!.Arguments["oge"]);
    }

    [Theory]
    [InlineData("fırındaki keki 25 dakika sonra kontrol etmemi söyle", "25", "fırındaki keki kontrol etmemi")]
    [InlineData("çayı demledim, on beş dakika sonra söyle", "15", "")]
    [InlineData("10 dakika sonra beni uyar", "10", "")]
    public void ATimerTakesItsTaskFromAroundTheTime(string said, string minutes, string note)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("zamanlayici", command?.Tool);
        Assert.Equal(minutes, command!.Arguments["dakika"]);
        Assert.Equal(note, command.Arguments.GetValueOrDefault("not", ""));
    }

    [Theory]
    [InlineData("dün akşam ne yemiştim")]
    [InlineData("bugün çok yoruldum")]
    [InlineData("bu akşam saat kaçta Mert'i arayacaktım")]
    [InlineData("Mert'i arayacaktım")]
    [InlineData("tam annemi arıyordum")]
    [InlineData("annemi aradım mı")]
    public void AQuestionOrAFeelingIsNotADoneTask(string said) => Assert.False(DoneStatement.IsDoneStatement(said));

    [Theory]
    [InlineData("annemi aradım")]
    [InlineData("saat üçte annemi aradım")]
    [InlineData("raporu gönderdim")]
    public void ADoneTaskIsStillRecognised(string said) => Assert.True(DoneStatement.IsDoneStatement(said));

    [Fact]
    public void ACountdownSpeaksInFridaysVoice()
    {
        var memories = new[] { new MemoryItem("1", "Kullanıcının annesinin doğum günü 12 Mart", DateTimeOffset.Now) };
        var answer = DayCountdown.Answer("annemin doğum günü", memories, new DateOnly(2026, 11, 24));
        Assert.StartsWith("Annenin doğum günü için 108 gün kaldı", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void ATurnWithoutThinkingAsksForADirectAnswer() => Assert.Contains("Düşünmeden", AgentPlan.NoThinking, StringComparison.Ordinal);
}

/// <summary>Modelin taşkınlıklarına karşı: metin olarak yazılmış araç çağrısı, üretim sınırına takılan cevap.</summary>
public class ModelGuardTests
{
    private static readonly HashSet<string> Names = ["eposta_taslagi", "not", "hatirlatici"];

    [Fact]
    public void AToolCallWrittenAsTextBecomesACall()
    {
        var call = TextToolCall.Parse("eposta_taslagi(alici=\"Ahmet Bey\", konu=\"İzin Talebi, Proje\", metin=\"Sayın Ahmet Bey,\nYarın izinliyim.\")", Names);
        Assert.Equal("eposta_taslagi", call?.Name);
        Assert.Equal("Ahmet Bey", call!.Arguments["alici"]);
        Assert.Equal("İzin Talebi, Proje", call.Arguments["konu"]);
        Assert.Equal("Sayın Ahmet Bey,\nYarın izinliyim.", call.Arguments["metin"]);
    }

    [Theory]
    [InlineData("e-posta_taslagi(alici='Ahmet Bey', metin='Sayın Ahmet Bey')")]
    [InlineData("e_posta_taslagi(alici='Ahmet Bey', metin='Sayın Ahmet Bey')")]
    public void AMisspelledToolNameStillBecomesACall(string text)
    {
        Assert.True(TextToolCall.MaybeStarts(text[..11]));
        Assert.True(TextToolCall.Starts(text, Names));
        Assert.Equal("eposta_taslagi", TextToolCall.Parse(text, Names)?.Name);
    }

    [Theory]
    [InlineData("sunum işini önemli olarak işaretle", "sunum")]
    [InlineData("kirayı önemli yap", "kirayı")]
    public void MarksATaskImportant(string sentence, string task)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("onemli", command?.Arguments["islem"]);
        Assert.Equal(task, command!.Arguments["metin"]);
    }

    [Fact]
    public void GemmasToolCodeFormIsUnderstood()
    {
        var text = "```tool_code\nprint(default_api.not(metin='Mert toplantısı perşembe'))\n```";
        Assert.True(TextToolCall.Starts(text, Names));
        Assert.Equal("Mert toplantısı perşembe", TextToolCall.Parse(text, Names)!.Arguments["metin"]);
    }

    [Theory]
    [InlineData("Not aldım, perşembe.")]
    [InlineData("unknown_tool(x=1)")]
    [InlineData("Saat (yaklaşık) üç.")]
    public void OrdinaryTextIsNotACall(string text) => Assert.Null(TextToolCall.Parse(text, Names));

    [Theory]
    [InlineData("eposta_tas", true)]
    [InlineData("```", true)]
    [InlineData("tamam", true)]
    [InlineData("Kaydettim", false)]
    [InlineData("evet şimdi", false)]
    public void APossibleCallIsHeldBackOnlyWhileUndecided(string text, bool undecided) => Assert.Equal(undecided, TextToolCall.MaybeStarts(text));

    [Theory]
    [InlineData("Bir zamanlar bir ejderha vardı. Her gece yıldızı korurdu. Bir gün yıldız kay", "Bir zamanlar bir ejderha vardı. Her gece yıldızı korurdu.")]
    [InlineData("Tek cümle bitmeden", "Tek cümle bitmeden")]
    [InlineData("Saat 3.5'te geldi! Sonra git", "Saat 3.5'te geldi!")]
    public void ACutAnswerEndsAtItsLastFullSentence(string text, string expected) => Assert.Equal(expected, text[..FridayAssistant.SentenceEnd(text)]);

    [Fact]
    public void AnAnswerCutByTheLimitIsMarked()
    {
        Assert.True(OllamaChatClient.ParseLine("""{"message":{"content":""},"done":true,"done_reason":"length","eval_count":512}""")!.Truncated);
        Assert.False(OllamaChatClient.ParseLine("""{"message":{"content":""},"done":true,"done_reason":"stop"}""")!.Truncated);
    }
}

/// <summary>Sorunun hangi günün programını sorduğu.</summary>
public class AgendaRangeTests
{
    private static readonly DateOnly Tuesday = new(2026, 12, 1);

    [Theory]
    [InlineData("yarın dişçim var mı", 1, 1)]
    [InlineData("bugün ne var", 0, 1)]
    [InlineData("bu akşam bir planım var mı", 0, 1)]
    [InlineData("öbür gün işim var mı", 2, 1)]
    [InlineData("bu hafta neler var", 0, 7)]
    [InlineData("cuma boş muyum", 3, 1)]
    [InlineData("salı günü toplantım var mı", 0, 1)]
    [InlineData("perşembeye randevum var mı", 2, 1)]
    public void TheAskedDayIsFound(string question, int offset, int days)
    {
        var range = DayAgenda.Range(question, Tuesday);
        Assert.NotNull(range);
        Assert.Equal(Tuesday.AddDays(offset), range!.Value.From);
        Assert.Equal(days, range.Value.To.DayNumber - range.Value.From.DayNumber);
    }

    [Theory]
    [InlineData("bugün hava nasıl")]
    [InlineData("dün ne yaptım")]
    [InlineData("geçen cuma toplantı var mıydı")]
    [InlineData("Ankara'nın nüfusu ne kadar")]
    public void OtherQuestionsHaveNoAgenda(string question) => Assert.Null(DayAgenda.Range(question, Tuesday));

    [Theory]
    [InlineData("bu ay markete ne kadar verdim", "ay", "market")]
    [InlineData("bu hafta benzine ne kadar ödedim", "hafta", "benzin")]
    public void ASpendingQuestionWithAnyVerbIsASummary(string said, string period, string category)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("harcama", command?.Tool);
        Assert.Equal(period, command!.Arguments["donem"]);
        // "Benzine" → "benzi" de olabilir; harcama notunda "içerir" diye arandığı için ikisi de bulur.
        Assert.StartsWith(command.Arguments["kategori"], category, StringComparison.Ordinal);
    }
}
