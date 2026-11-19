using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class SituationTests
{
    [Fact]
    public void DescribesTheMomentForTheModel()
    {
        using var temp = new TempDirectory();
        using var http = new HttpClient();
        var paths = new FridayPaths(temp.Path);
        var reminders = new ReminderStore(Path.Combine(temp.Path, "reminders.json"));
        using var tools = new AssistantTools(http, new OllamaChatClient(http, new OllamaServer(paths, http)),
            new MemoryStore(Path.Combine(temp.Path, "memory.json")), new PrivacyLedger(temp.Path), new ChatStore(Path.Combine(temp.Path, "chats")),
            reminders, new NoteStore(Path.Combine(temp.Path, "notes.md")))
        {
            Todos = new TodoStore(Path.Combine(temp.Path, "todos.json")),
            MeetingApp = () => "Teams"
        };

        var now = DateTime.Now;
        Assert.StartsWith($"\n[Durum: saat {now:HH:mm}", tools.Situation(now), StringComparison.Ordinal);

        tools.Todos.Add("Raporu bitir", now, DateOnly.FromDateTime(now));
        tools.Todos.Add("Süt al", now);
        reminders.Add(now.AddHours(1), "doktoru ara", now);
        var situation = tools.Situation(now);
        Assert.Contains("2 açık iş (bugün son gün: Raporu bitir)", situation, StringComparison.Ordinal);
        Assert.Contains("sıradaki hatırlatma", situation, StringComparison.Ordinal);
        Assert.Contains("doktoru ara", situation, StringComparison.Ordinal);
        Assert.Contains("Teams görüşmesinde", situation, StringComparison.Ordinal);
        Assert.EndsWith("]", situation, StringComparison.Ordinal);
    }
}
