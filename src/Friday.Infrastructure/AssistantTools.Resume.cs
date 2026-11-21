using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>"Nerede kalmıştık?" (bkz. <see cref="ResumeStory"/>).</summary>
public sealed partial class AssistantTools
{
    private DateTime _focusAt;

    private QuickReply Resume()
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var open = Todos?.Open ?? [];
        var current = history.CurrentId;
        var facts = new ResumeFacts(now)
        {
            Focus = _focusTask is { Length: > 0 } task ? (task, _focusAt) : null,
            BeforeBreak = Rhythm is { Enabled: true } coach ? coach.LastPlace : null,
            RecentApps = Tracker?.Since(now.AddHours(-1)) ?? [],
            TodayApps = Tracker?.Day(now) ?? [],
            YesterdayApps = Tracker?.Day(now.AddDays(-1)) ?? [],
            LastChat = history.List()
                .Where(chat => chat.Id != current && chat.TurnCount > 0)
                .OrderByDescending(chat => chat.Updated)
                .Select(chat => ((string Title, DateTime At)?)(chat.Title, chat.Updated.LocalDateTime))
                .FirstOrDefault(),
            Due = [.. open.Where(item => item.Due is { } due && due <= today).Select(item => item.Text)],
            NewestTodo = open.OrderByDescending(item => item.Created).Select(item => ((string Text, DateTime At)?)(item.Text, item.Created)).FirstOrDefault(),
            LastNote = notes.All.Where(note => !note.Text.StartsWith("Notlarım burada", StringComparison.Ordinal))
                .OrderByDescending(note => note.Time).Select(note => ((string Text, DateTime At)?)(note.Text, note.Time)).FirstOrDefault()
        };
        return new QuickReply(ResumeStory.Tell(facts));
    }
}
