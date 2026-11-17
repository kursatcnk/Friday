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
    private DateTimeOffset _lastActivity;

    public ConversationMemory(TimeSpan expiry, TimeProvider? clock = null)
    {
        Expiry = expiry;
        _clock = clock ?? TimeProvider.System;
        _lastActivity = _clock.GetUtcNow();
    }

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>Bu kadar konuşulmazsa sohbet biter (ayarlardan değişebilir).</summary>
    public TimeSpan Expiry { get; set; }

    /// <summary>Yeni bir tur başlarken çağrılır; hafıza süresi dolduysa eski mesajları siler.</summary>
    /// <returns>Süre dolmuşsa true: önceki konuşma bitti, yeni bir sohbet başlıyor.</returns>
    public bool BeginTurn()
    {
        var expired = _clock.GetUtcNow() - _lastActivity > Expiry;
        if (expired)
        {
            _messages.Clear();
        }

        _lastActivity = _clock.GetUtcNow();
        return expired;
    }

    /// <summary>
    /// Listeden açılan eski bir sohbetin son turlarını yükler; devam sorusu ("peki yarın?") o sohbete göre anlaşılır.
    /// Süre bu andan itibaren yeniden sayılır.
    /// </summary>
    public void Load(IEnumerable<ConversationTurn> turns, int maxTurns = 6)
    {
        _messages.Clear();
        foreach (var turn in turns.TakeLast(maxTurns))
        {
            _messages.Add(new ChatMessage(ChatRole.User, turn.User));
            _messages.Add(new ChatMessage(ChatRole.Assistant, turn.Assistant));
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

    /// <summary>
    /// Araç çıktıları (arama sonuçları, sekme listeleri…) yalnızca o turda gerekir. Tur bitince kısaltılır; yoksa
    /// birkaç soru sonra modelin bağlamı dolar ve model boş cevap vermeye başlar.
    /// </summary>
    public void CompactToolResults(int maxCharacters = 400)
    {
        for (var index = 0; index < _messages.Count; index++)
        {
            var message = _messages[index];
            if (message.Role == ChatRole.Tool && message.Content.Length > maxCharacters)
            {
                _messages[index] = message with { Content = message.Content[..maxCharacters] + " …(kısaltıldı)" };
            }
        }
    }

    public void Clear() => _messages.Clear();
}
