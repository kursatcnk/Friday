using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class VocabularyCheckTests
{
    [Theory]
    [InlineData("isteksiz", "isteksiz, gönülsüz", true)]
    [InlineData("gönülsüz demek", "isteksiz, gönülsüz", true)]
    [InlineData("İSTEKSİZ.", "isteksiz, gönülsüz", true)]
    [InlineData("istekli", "isteksiz, gönülsüz", false)]
    [InlineData("vazgeçtim", "vazgeçmek, bırakmak", true)]
    [InlineData("israr etmek", "ısrar etmek", true)]
    [InlineData("kaçınılmas", "kaçınılmaz", true)]
    [InlineData("mutsuz", "mutlu tesadüf, şans eseri bulma", false)]
    [InlineData("göz ardı etmek galiba", "görmezden gelmek, göz ardı etmek", true)]
    [InlineData("", "fırsat", false)]
    public void ChecksSpokenAnswers(string answer, string back, bool correct) => Assert.Equal(correct, VocabularyDeck.Check(answer, back));
}

public class VocabularyDeckTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    [Fact]
    public void SeedsOnceAndLimitsNewWords()
    {
        using var temp = new TempDirectory();
        var deck = new VocabularyDeck(Path.Combine(temp.Path, "vocabulary.json"));
        Assert.Equal(StarterWords.All.Count, deck.SeedStarter(Today));
        Assert.Equal(0, deck.SeedStarter(Today));
        Assert.True(StarterWords.All.Count >= 100);
        Assert.Equal(StarterWords.All.Count, StarterWords.All.Select(word => word.Front).Distinct().Count());

        // Kendi kelimen hazır desteden önce sorulur.
        deck.Add("serendipity", "mutlu tesadüf", Today);
        deck.Add("Petrichor", "yağmur sonrası toprak kokusu", Today);
        var session = deck.Session(Today);
        Assert.Equal(VocabularyDeck.NewPerDay, session.Count);
        Assert.Equal("Petrichor", session[0].Front);
        Assert.Equal(StarterWords.All.Count + 1, deck.Cards.Count);

        foreach (var card in session)
        {
            deck.Grade(card.Id, correct: true, Today);
        }

        // Günün yeni kelime hakkı doldu; yarın tekrarlar ve yeni kelimeler gelir.
        Assert.Empty(deck.Session(Today));
        var tomorrow = deck.Session(Today.AddDays(1));
        Assert.Equal(VocabularyDeck.SessionSize, tomorrow.Count);
        Assert.Equal(session.Count, tomorrow.Count(card => card.FirstSeen is not null));
    }

    [Fact]
    public void MovesCardsThroughLeitnerBoxes()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "vocabulary.json");
        var deck = new VocabularyDeck(file);
        var card = deck.Add("thrive", "gelişmek", Today);
        deck.Grade(card.Id, true, Today);
        deck.Grade(card.Id, true, Today.AddDays(1));
        var saved = new VocabularyDeck(file).Cards.Single();
        Assert.Equal(2, saved.Box);
        Assert.Equal(Today.AddDays(4), saved.Due);

        deck.Grade(card.Id, false, Today.AddDays(4));
        saved = new VocabularyDeck(file).Cards.Single();
        Assert.Equal(0, saved.Box);
        Assert.Equal(Today.AddDays(4), saved.Due);
        Assert.Equal((2, 1), (saved.Right, saved.Wrong));
        Assert.Equal(new DeckStats(1, 0, 1, 0, 1), deck.Stats(Today.AddDays(4)));
        Assert.True(deck.Remove("THRIVE"));
    }
}

public class PracticeIntentTests
{
    [Theory]
    [InlineData("kelime çalışalım")]
    [InlineData("İngilizce pratik yapalım")]
    [InlineData("kelime testi")]
    [InlineData("kartları tekrar edelim")]
    [InlineData("Friday, kelime pratiği başlat")]
    public void StartsPractice(string sentence)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("kelime", command?.Tool);
        Assert.Equal("calis", command!.Arguments["islem"]);
    }

    [Theory]
    [InlineData("kelime ekle: serendipity = mutlu tesadüf", "serendipity", "mutlu tesadüf")]
    [InlineData("kelime ekle Petrichor anlamı yağmur sonrası toprak kokusu", "Petrichor", "yağmur sonrası toprak kokusu")]
    [InlineData("yeni kelime ekle: to give up: vazgeçmek", "to give up", "vazgeçmek")]
    public void AddsWords(string sentence, string front, string back)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("kelime", command?.Tool);
        Assert.Equal("ekle", command!.Arguments["islem"]);
        Assert.Equal(front, command.Arguments["kelime"]);
        Assert.Equal(back, command.Arguments["anlam"]);
    }

    [Theory]
    [InlineData("kaç kelime biliyorum", "durum")]
    [InlineData("serendipity kelimesini sil", "sil")]
    public void ShowsStatsAndRemoves(string sentence, string action) => Assert.Equal(action, QuickIntents.Match(sentence)?.Arguments["islem"]);

    [Theory]
    [InlineData("spotify'ın sesini kıs", "kis", "spotify", "")]
    [InlineData("discord'un sesini biraz aç", "ac", "discord", "")]
    [InlineData("chrome'un sesini yüzde 30 yap", "ayarla", "chrome", "30")]
    [InlineData("spotify sesini 20'ye indir", "ayarla", "spotify", "20")]
    [InlineData("discord'u sessize al", "sessiz", "discord", "")]
    [InlineData("spotify'ın sesini geri aç", "sesli", "spotify", "")]
    [InlineData("tarayıcının sesini kapat", "sessiz", "tarayıcı", "")]
    public void ControlsAppVolume(string sentence, string action, string app, string level)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("uygulama_sesi", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(app, command.Arguments["ad"]);
        Assert.Equal(level, command.Arguments.GetValueOrDefault("deger", ""));
    }

    [Theory]
    [InlineData("hangi uygulamalar ses çıkarıyor", "uygulama_sesi")]
    [InlineData("sesi kıs", "medya")]
    [InlineData("sessize al", "medya")]
    [InlineData("müziğin sesini aç", null)]
    public void KeepsGeneralVolumeCommands(string sentence, string? tool) => Assert.Equal(tool, QuickIntents.Match(sentence)?.Tool is "uygulama_ac" ? null : QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("spotify", "Spotify", true)]
    [InlineData("edge", "msedge", true)]
    [InlineData("tarayıcı", "chrome", true)]
    [InlineData("tarayıcı", "Spotify", false)]
    [InlineData("discord", "Spotify", false)]
    public void MatchesAudioApps(string spoken, string process, bool match) => Assert.Equal(match, AppAudio.Match(spoken)(process));
}

public sealed class PracticeToolTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;

    public PracticeToolTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")))
        {
            Vocabulary = new VocabularyDeck(Path.Combine(_temp.Path, "vocabulary.json")),
            HabitBook = new HabitBook(Path.Combine(_temp.Path, "habit-book.json"))
        };
    }

    private Task<QuickReply?> Answer(string text) =>
        _tools.RunQuickAsync(new QuickCommand("serbest_cevap", new Dictionary<string, string> { ["metin"] = text }), CancellationToken.None);

    [Fact]
    public async Task RunsAQuizSession()
    {
        var deck = _tools.Vocabulary!;
        deck.Add("serendipity", "mutlu tesadüf", DateOnly.FromDateTime(DateTime.Now));
        var start = await _tools.RunQuickAsync(QuickIntents.Match("kelime çalışalım")!, CancellationToken.None);
        Assert.Contains("110 sık kullanılan", start!.Text, StringComparison.Ordinal);
        Assert.EndsWith("İlki: serendipity.", start.Text, StringComparison.Ordinal);
        Assert.Equal("serendipity", start.Card?.Big);
        Assert.True(_tools.AnswerTakesPriority);

        var right = await Answer("mutlu tesadüf");
        Assert.Contains("Sıradaki: reluctant.", right!.Text, StringComparison.Ordinal);

        // Bilinmeyen kelime ("pas") sonda bir kez daha sorulur; "mola" gibi komut sözleri de cevap sayılır.
        var pass = await Answer("pas");
        Assert.StartsWith("Pas. Reluctant: isteksiz, gönülsüz.", pass!.Text, StringComparison.Ordinal);
        Assert.True(_tools.AnswerTakesPriority);

        var stop = await Answer("yeter");
        Assert.Contains("2 kelimeden 1 tanesini bildin", stop!.Text, StringComparison.Ordinal);
        Assert.Contains("reluctant", stop.Text, StringComparison.Ordinal);
        Assert.False(_tools.AwaitsAnswer);
        Assert.Equal(2, deck.Cards.Count(card => card.FirstSeen is not null));

        var stats = await _tools.RunQuickAsync(QuickIntents.Match("kaç kelime biliyorum")!, CancellationToken.None);
        Assert.Contains($"Destende {StarterWords.All.Count} kelime var", stats!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NudgesToKeepAStreak()
    {
        var book = _tools.HabitBook!;
        var today = DateOnly.FromDateTime(DateTime.Now);
        book.Add("Kitap okumak", today.AddDays(-5));
        book.Add("Spor", today.AddDays(-5));
        for (var day = 1; day <= 3; day++)
        {
            book.Mark("kitap", today.AddDays(-day));
        }

        Assert.Equal("Bugün henüz kitap okumak işaretlenmedi; 3 günlük serin var, bozma!", _tools.StreakNudge(DateTime.Now));
        Assert.Null(_tools.StreakNudge(DateTime.Now));
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
