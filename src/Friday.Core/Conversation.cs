namespace Friday.Core;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record ToolCall(string Name, IReadOnlyDictionary<string, string> Arguments);

public sealed record ChatMessage(ChatRole Role, string Content)
{
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];

    /// <summary>Role Tool ise sonucun hangi araçtan geldiği.</summary>
    public string? ToolName { get; init; }
}

/// <summary>
/// Kısa süreli sohbet hafızası: son mesajları tutar, belirli bir süre konuşulmazsa unutur. Böylece
/// "peki yarın?" gibi devam soruları çalışır ama dünkü konuşma modele taşınmaz.
/// </summary>
public sealed class ConversationMemory
{
    private const int MaxMessages = 16;

    private readonly List<ChatMessage> _messages = [];
    private readonly TimeProvider _clock;
    private readonly TimeSpan _expiry;
    private DateTimeOffset _lastActivity;

    public ConversationMemory(TimeSpan expiry, TimeProvider? clock = null)
    {
        _expiry = expiry;
        _clock = clock ?? TimeProvider.System;
        _lastActivity = _clock.GetUtcNow();
    }

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>Yeni bir tur başlarken çağrılır; hafıza süresi dolduysa eski mesajları siler.</summary>
    public void BeginTurn()
    {
        if (_clock.GetUtcNow() - _lastActivity > _expiry)
        {
            _messages.Clear();
        }

        _lastActivity = _clock.GetUtcNow();
    }

    public void Add(ChatMessage message)
    {
        _messages.Add(message);
        _lastActivity = _clock.GetUtcNow();

        if (_messages.Count <= MaxMessages)
        {
            return;
        }

        // Kırpılan baş, araç sonucu ya da araç çağrısının ortasında kalmamalı: ilk kullanıcı mesajından başlar.
        var cut = _messages.Count - MaxMessages;
        while (cut < _messages.Count && _messages[cut].Role != ChatRole.User)
        {
            cut++;
        }

        _messages.RemoveRange(0, cut);
    }

    public void Clear() => _messages.Clear();
}
