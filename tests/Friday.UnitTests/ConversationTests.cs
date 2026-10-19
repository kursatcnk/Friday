using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class ConversationMemoryTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void ForgetsAfterInactivity()
    {
        var clock = new ManualClock();
        var memory = new ConversationMemory(TimeSpan.FromMinutes(10), clock);
        memory.BeginTurn();
        memory.Add(new ChatMessage(ChatRole.User, "Merhaba"));

        clock.Now += TimeSpan.FromMinutes(5);
        memory.BeginTurn();
        Assert.Single(memory.Messages);

        clock.Now += TimeSpan.FromMinutes(11);
        memory.BeginTurn();
        Assert.Empty(memory.Messages);
    }

    [Fact]
    public void TrimmedHistoryStartsWithAUserMessage()
    {
        var memory = new ConversationMemory(TimeSpan.FromMinutes(10));
        for (var index = 0; index < 10; index++)
        {
            memory.Add(new ChatMessage(ChatRole.User, $"soru {index}"));
            memory.Add(new ChatMessage(ChatRole.Assistant, "") { ToolCalls = [new ToolCall("saat_ve_tarih", new Dictionary<string, string>())] });
            memory.Add(new ChatMessage(ChatRole.Tool, "10:00") { ToolName = "saat_ve_tarih" });
        }

        Assert.True(memory.Messages.Count <= 16);
        Assert.Equal(ChatRole.User, memory.Messages[0].Role);
    }
}

public class OllamaProtocolTests
{
    [Fact]
    public void ParsesStreamedContent()
    {
        var delta = OllamaChatClient.ParseLine("""{"model":"m","message":{"role":"assistant","content":"Merhaba"},"done":false}""");
        Assert.NotNull(delta);
        Assert.Equal("Merhaba", delta.Content);
        Assert.False(delta.Done);
    }

    [Fact]
    public void ParsesToolCallsWithNonStringArguments()
    {
        var delta = OllamaChatClient.ParseLine(
            """{"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"zamanlayici","arguments":{"dakika":5,"not":"çay"}}}]},"done":true}""");
        var call = Assert.Single(delta!.ToolCalls);
        Assert.Equal("zamanlayici", call.Name);
        Assert.Equal("5", call.Arguments["dakika"]);
        Assert.Equal("çay", call.Arguments["not"]);
        Assert.True(delta.Done);
    }

    [Fact]
    public void ErrorLineThrows() =>
        Assert.Throws<HttpRequestException>(() => OllamaChatClient.ParseLine("""{"error":"model not found"}"""));

    [Fact]
    public void RequestCarriesToolResultsAndEnablesThinking()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Saat kaç?"),
            new(ChatRole.Assistant, "") { ToolCalls = [new ToolCall("saat_ve_tarih", new Dictionary<string, string>())] },
            new(ChatRole.Tool, "10:00") { ToolName = "saat_ve_tarih" },
        };

        var request = OllamaChatClient.BuildRequest("gemma4:e4b", messages, []);
        Assert.True(request["think"]!.GetValue<bool>());
        Assert.Equal("tool", request["messages"]![2]!["role"]!.GetValue<string>());
        Assert.Equal("saat_ve_tarih", request["messages"]![2]!["tool_name"]!.GetValue<string>());
        Assert.Equal("saat_ve_tarih", request["messages"]![1]!["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
    }
}

public class FridayPathsTests
{
    [Fact]
    public void RefusesCDrive() =>
        Assert.Throws<InvalidOperationException>(() => new FridayPaths(@"C:\Friday"));

    [Fact]
    public void AcceptsDDrive() =>
        Assert.Equal(@"D:\Friday\data\settings.json", new FridayPaths(@"D:\Friday").SettingsFile);
}
