using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

public sealed record ConversationTurn(DateTimeOffset Time, string User, string Assistant);

/// <summary>Günlük sohbet geçmişi: "Bugün ne konuştuk?" ve paneldeki geçmiş için. Yalnızca bilgisayarda tutulur.</summary>
public sealed class ConversationLog(string directory, TimeProvider? clock = null)
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();

    public void Append(string user, string assistant)
    {
        var turn = new ConversationTurn(_clock.GetLocalNow(), user, assistant);
        lock (_gate)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(FileFor(DateOnly.FromDateTime(turn.Time.DateTime)),
                JsonSerializer.Serialize(turn, Options) + Environment.NewLine);
        }
    }

    public IReadOnlyList<ConversationTurn> Read(DateOnly day)
    {
        var file = FileFor(day);
        lock (_gate)
        {
            if (!File.Exists(file))
            {
                return [];
            }

            return File.ReadLines(file)
                .Where(line => line.Length > 0)
                .Select(line => JsonSerializer.Deserialize<ConversationTurn>(line, Options))
                .OfType<ConversationTurn>()
                .ToList();
        }
    }

    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    private string FileFor(DateOnly day) => Path.Combine(directory, $"{day:yyyy-MM-dd}.jsonl");
}
