using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Kelime pratiği (aralıklı tekrar) ve uygulama bazında ses.</summary>
public sealed partial class AssistantTools
{
    private static readonly string[] Praise = ["Doğru!", "Harika!", "Evet, doğru!", "Aynen öyle!", "Süper!"];

    private readonly List<WordCard> _quiz = [];
    private readonly HashSet<string> _gradedIds = [];
    private readonly List<string> _missed = [];
    private int _quizIndex;
    private int _quizRight;
    private int _quizGraded;

    /// <summary>Kelime destesi (App verir).</summary>
    public VocabularyDeck? Vocabulary { get; set; }

    /// <summary>Kelime pratiği sürüyor; cevap ("mola" bile olsa) komut sayılmaz, karta gider.</summary>
    public bool AnswerTakesPriority => _quiz.Count > 0 && AwaitsAnswer;

    // ---------- Kelime pratiği ----------

    private QuickReply VocabularyCommand(string action, string front, string back)
    {
        if (Vocabulary is not { } deck)
        {
            return new QuickReply("Kelime pratiği hazır değil.");
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        switch (Simplify(action))
        {
            case "ekle":
                if (front.Trim().Length == 0 || back.Trim().Length == 0)
                {
                    return new QuickReply("Kelimeyi ve anlamını anlayamadım; ör. \"kelime ekle: serendipity = mutlu tesadüf\".");
                }

                var card = deck.Add(front, back, today);
                ledger.Record(DataFlow.Local, "Kelime", $"\"{card.Front}\" desteye eklendi");
                return new QuickReply($"Ekledim: {card.Front}, {card.Back}. Sıradaki çalışmada sorarım.");
            case "sil":
                return new QuickReply(deck.Remove(front) ? $"{front} kelimesini desteden çıkardım." : $"Destede \"{front}\" diye bir kelime yok.");
            case "durum":
                var stats = deck.Stats(today);
                if (stats.Total == 0)
                {
                    return new QuickReply("Henüz kelime desten yok. \"Kelime çalışalım\" dersen 110 sık kullanılan İngilizce kelimeyle başlarız.");
                }

                ShowCard(new AnswerCard("Kelime pratiği", "")
                {
                    Big = stats.Learned.ToString(CultureInfo.InvariantCulture),
                    BigCaption = "öğrenilen kelime",
                    Rows = [new CardRow("Destede", $"{stats.Total}"), new CardRow("Bugün tekrar", $"{stats.DueToday}"), new CardRow("Henüz sorulmadı", $"{stats.Unseen}")]
                });
                return new QuickReply($"Destende {stats.Total} kelime var; {stats.Learned} tanesi iyice oturdu. Bugün {stats.DueToday} tekrar bekliyor"
                    + (stats.Waiting > stats.DueToday ? $", ayrıca {stats.Waiting - stats.DueToday} yeni kelime gelebilir." : "."));
            case "bitir":
                return _quiz.Count == 0 ? new QuickReply("Şu an süren bir kelime çalışması yok.") : FinishQuiz("");
            default:
                return StartQuiz(deck, today);
        }
    }

    private QuickReply StartQuiz(VocabularyDeck deck, DateOnly today)
    {
        var seeded = deck.SeedStarter(today);
        var session = deck.Session(today);
        _quiz.Clear();
        _gradedIds.Clear();
        _missed.Clear();
        _quizIndex = _quizRight = _quizGraded = 0;
        if (session.Count == 0)
        {
            var next = deck.Cards.Where(card => card.FirstSeen is not null).Select(card => card.Due).DefaultIfEmpty(today.AddDays(1)).Min();
            return new QuickReply($"Bugünlük kelimeler bitti, harika! Sıradaki tekrar {SpecialDays.When(next, today)}. Yeni kelime eklemek istersen \"kelime ekle: kelime = anlamı\" de.");
        }

        _quiz.AddRange(session);
        ledger.Record(DataFlow.Local, "Kelime", $"Pratik başladı: {session.Count} kart");
        var reviews = session.Count(card => card.FirstSeen is not null);
        var intro = seeded > 0
            ? $"110 sık kullanılan İngilizce kelimeyle bir deste hazırladım. Bugün {session.Count} kelime soracağım."
            : reviews == session.Count ? $"Bugün {session.Count} kelime tekrar bekliyor."
            : $"Bugün {reviews} tekrar ve {session.Count - reviews} yeni kelime var.";
        intro += " Türkçe anlamını söyle; bilmiyorsan \"pas\", bitirmek için \"yeter\" de.";
        return AskCard(intro + " İlki:");
    }

    private QuickReply AskCard(string lead)
    {
        var card = _quiz[_quizIndex];
        _awaitingAnswer = QuizAnswer;
        _awaitingAt = DateTime.Now;
        _awaitingOwnsDecline = true;
        ShowCard(new AnswerCard("Kelime pratiği", "")
        {
            Big = card.Front,
            BigCaption = $"{Math.Min(_quizIndex + 1, _quiz.Count)}/{_quiz.Count}"
        });
        return new QuickReply($"{lead} {card.Front}.".TrimStart());
    }

    private QuickReply QuizAnswer(string text)
    {
        if (_quiz.Count == 0 || _quizIndex >= _quiz.Count || Vocabulary is not { } deck)
        {
            return new QuickReply("Şu an süren bir kelime çalışması yok.");
        }

        var simple = Simplify(text);
        if (simple is "yeter" or "bitir" or "bitti" or "testibitir" or "dur" or "durdur" or "tamamyeter" || QuickIntents.IsNo(text) && simple is not "yok")
        {
            return FinishQuiz("Tamam, burada bırakalım.");
        }

        var card = _quiz[_quizIndex];
        var pass = simple is "pas" or "gec" or "bilmiyorum" or "hatirlamiyorum" or "yok" or "bilemedim" or "atla" or "pasgec";
        var correct = !pass && VocabularyDeck.Check(text, card.Back);
        // Bir kart oturumda yalnızca ilk sorulduğunda puanlanır; bilinmeyen kart sonda bir kez daha sorulur.
        var firstTime = _gradedIds.Add(card.Id);
        if (firstTime)
        {
            deck.Grade(card.Id, correct, DateOnly.FromDateTime(DateTime.Now));
            _quizGraded++;
            _quizRight += correct ? 1 : 0;
        }

        var feedback = correct
            ? Praise[Random.Shared.Next(Praise.Length)] + (card.Back.Contains(',', StringComparison.Ordinal) ? $" {TurkishGrammar.Capitalize(card.Back)}." : "")
            : $"{(pass ? "Pas." : "Değil.")} {TurkishGrammar.Capitalize(card.Front)}: {card.Back}.";
        if (!correct && firstTime)
        {
            _quiz.Add(card);
            _missed.Add(card.Front);
        }

        _quizIndex++;
        return _quizIndex < _quiz.Count ? AskCard($"{feedback} Sıradaki:") : FinishQuiz(feedback);
    }

    private QuickReply FinishQuiz(string lead)
    {
        _awaitingAnswer = null;
        _awaitingOwnsDecline = false;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var summary = _quizGraded == 0
            ? ""
            : _quizRight == _quizGraded ? $" {_quizGraded} kelimenin hepsini bildin, tebrikler!" : $" {_quizGraded} kelimeden {_quizRight} tanesini bildin.";
        if (_missed.Count > 0)
        {
            summary += $" Bunları yine soracağım: {TurkishGrammar.Join([.. _missed.Distinct().Take(4)])}.";
        }

        if (Vocabulary is { } deck)
        {
            var stats = deck.Stats(today);
            ShowCard(new AnswerCard("Kelime pratiği", "")
            {
                Big = $"{_quizRight}/{_quizGraded}",
                BigCaption = "bu çalışmada",
                Rows = [new CardRow("Öğrenilen", $"{stats.Learned}"), new CardRow("Destede", $"{stats.Total}")]
            });
        }

        ledger.Record(DataFlow.Local, "Kelime", $"Pratik bitti: {_quizRight}/{_quizGraded}");
        _quiz.Clear();
        return new QuickReply((lead + summary).Trim());
    }

    // ---------- Uygulama sesi ----------

    private QuickReply AppVolumeCommand(string action, string name, string levelText)
    {
        var simple = Simplify(action);
        if (simple == "listele")
        {
            var apps = AppAudio.Apps();
            if (apps.Count == 0)
            {
                return new QuickReply("Şu an ses çıkaran bir uygulama yok.");
            }

            ShowListCard("Uygulama sesleri", "", apps.Take(MaxCardRows).Select(app => new CardRow(app.Name, AudioLabel(app))));
            return new QuickReply("Ses karıştırıcıdaki uygulamalar: " + string.Join(", ", apps.Take(5).Select(app => $"{app.Name} {AudioLabel(app).ToLower(Turkish)}")) + ".");
        }

        var level = int.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent) ? Math.Clamp(percent, 0, 100) / 100f : -1f;
        var result = AppAudio.Apply(name, (volume, muted) => simple switch
        {
            "ayarla" when level >= 0 => (level, false),
            "kis" => (Math.Max(0.05f, volume - 0.2f), muted),
            "sessiz" => (volume, true),
            "sesli" => (volume, false),
            _ => muted ? (volume, false) : (Math.Min(1f, volume + 0.2f), false)
        });
        if (result is null)
        {
            return new QuickReply($"{TurkishGrammar.Capitalize(name)} şu an ses karıştırıcıda yok; ses çıkarmıyor olabilir.");
        }

        ledger.Record(DataFlow.Local, "Uygulama sesi", $"{result.Name}: {AudioLabel(result)}");
        return new QuickReply(result.Muted ? $"{result.Name} sessize alındı." : $"{result.Name} sesi yüzde {Math.Round(result.Volume * 100)}.", Silent: simple is "kis" or "ac");
    }

    private static string AudioLabel(AudioApp app) => app.Muted ? "Sessiz" : $"%{Math.Round(app.Volume * 100)}";
}
