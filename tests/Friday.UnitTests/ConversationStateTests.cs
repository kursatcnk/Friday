using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

/// <summary>
/// Yarım kalan konuşmalar: kelime çalışması sohbet kapanınca biter, araya giren bir istek cevap sanılmaz.
/// Hata: "kelime çalış"tan sonra sohbet kapatılıp "100 dolar ne kadar" denince kelime çalışması "Değil." diye sürüyordu.
/// </summary>
public class ConversationAsideTests
{
    [Theory]
    [InlineData("100 dolar ne kadar")]
    [InlineData("100 dolar kaç lira?")]
    [InlineData("yarın hava nasıl olacak")]
    [InlineData("bugün toplantım var mı")]
    [InlineData("ekranımda ne var?")]
    [InlineData("bana bugün neler konuştuğumuzu kısaca anlatır mısın")]
    public void ARequestInTheMiddleIsNotAnAnswer(string said) => Assert.True(QuickIntents.IsAside(said, shortAnswer: true));

    [Theory]
    [InlineData("isteksiz")]
    [InlineData("başarmak mı")]
    [InlineData("istekli, hevesli")]
    [InlineData("pas")]
    [InlineData("yok")]
    [InlineData("tamam aldım")]
    [InlineData("bilmiyorum")]
    public void AShortAnswerStaysAnAnswer(string said) => Assert.False(QuickIntents.IsAside(said, shortAnswer: true));

    [Theory]
    [InlineData("çok yorucuydu, üç toplantı yaptım ama sonunda proje bitti", false)]
    [InlineData("ne kadar yorucu bir gündü anlatamam", false)]
    [InlineData("yarın hava nasıl olacak?", true)]
    public void AFreeAnswerIsOnlySetAsideForAClearQuestion(string said, bool aside) => Assert.Equal(aside, QuickIntents.IsAside(said, shortAnswer: false));

    [Theory]
    [InlineData("100 dolar ne kadar", "100", "USD", "TRY")]
    [InlineData("50 euro kaç", "50", "EUR", "TRY")]
    [InlineData("100 dolar kaç lira", "100", "USD", "TRY")]
    public void AnAmountWithoutTargetIsConvertedToLira(string said, string amount, string from, string to)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("doviz", command?.Tool);
        Assert.Equal(amount, command!.Arguments["miktar"]);
        Assert.Equal(from, command.Arguments["kaynak"]);
        Assert.Equal(to, command.Arguments["hedef"]);
    }

    [Fact]
    public void TheRateQuestionWithoutAmountStaysTheSame() => Assert.Equal("doviz", QuickIntents.Match("dolar ne kadar")?.Tool);
}

public sealed class ConversationStateTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;

    public ConversationStateTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")))
        {
            Vocabulary = new VocabularyDeck(Path.Combine(_temp.Path, "vocabulary.json"))
        };
    }

    [Fact]
    public async Task ClosingTheChatEndsTheVocabularyPractice()
    {
        var start = await _tools.RunQuickAsync(QuickIntents.Match("kelime çalış")!, CancellationToken.None);
        Assert.Contains("İlki:", start!.Text, StringComparison.Ordinal);
        Assert.True(_tools.AwaitsAnswer);
        Assert.True(_tools.AnswerTakesPriority);

        _tools.EndConversation();

        Assert.False(_tools.AwaitsAnswer);
        Assert.False(_tools.AnswerTakesPriority);
        Assert.False(_tools.InChecklist);
        Assert.False(_tools.HasPendingConfirmation);
    }

    [Fact]
    public void AQuestionAboutAReadDocumentRemindsTheModelOfIt()
    {
        var log = new ReadingLog(Path.Combine(_temp.Path, "reading.json"));
        var (offer, _) = log.Touch(@"C:\a\teklif_v2.pdf", DateTime.Now, 300);
        log.SetDigest(offer, "Mavi Yazılım'ın kurumsal web sitesi teklifi; toplam 92.000 TL artı KDV, teslim 30 gün.", "teklif, web sitesi, Mavi Yazılım, proposal");
        _tools.Reading = log;

        Assert.Contains("teklif_v2.pdf", _tools.ReadingContext("Mavi Yazılım'ın teklifi ne kadardı"), StringComparison.Ordinal);
        Assert.Equal("", _tools.ReadingContext("yarın hava nasıl olacak"));
        Assert.Equal("", _tools.ReadingContext("web tasarımı nedir"));
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
