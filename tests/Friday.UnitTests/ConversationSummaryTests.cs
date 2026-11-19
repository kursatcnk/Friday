using Friday.Core;

namespace Friday.UnitTests;

public class ConversationSummaryTests
{
    [Fact]
    public void KeepsAShortNoteOfDroppedTurns()
    {
        var memory = new ConversationMemory(TimeSpan.FromMinutes(30));
        Assert.Null(memory.Summary);
        memory.Add(new ChatMessage(ChatRole.User, "Kızımın adı Elif, 8 yaşında; doğum günü için hediye önerir misin?\n[Bağlam: kullanıcı \"Chrome\" penceresindeydi]"));
        memory.Add(new ChatMessage(ChatRole.Assistant, "Elif için bir bilim seti güzel olur. Mikroskop da düşünebilirsin."));
        for (var index = 0; index < 8; index++)
        {
            memory.Add(new ChatMessage(ChatRole.User, $"Soru {index}"));
            memory.Add(new ChatMessage(ChatRole.Assistant, $"Cevap {index}."));
        }

        Assert.True(memory.Messages.Count <= 16);
        Assert.Equal(ChatRole.User, memory.Messages[0].Role);
        var summary = memory.Summary;
        Assert.NotNull(summary);
        Assert.StartsWith("[Bu sohbette daha önce: S: Kızımın adı Elif, 8 yaşında;", summary, StringComparison.Ordinal);
        Assert.Contains("C: Elif için bir bilim seti güzel olur.", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Bağlam", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Mikroskop", summary, StringComparison.Ordinal);

        memory.Clear();
        Assert.Null(memory.Summary);
    }
}
