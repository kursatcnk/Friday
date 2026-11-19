using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;

namespace Friday.Desktop;

/// <summary>Sohbet listesinde bir grup başlığı ("Bugün", "Dün", "Bu hafta").</summary>
public sealed record ChatGroupView(string Title);

/// <summary>Sohbet listesinde bir sohbet.</summary>
public sealed partial class ChatItemView(string id, string title, string meta, string snippet, bool pinned) : ObservableObject
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    public string Meta { get; } = meta;

    public string Snippet { get; } = snippet;

    public bool HasSnippet => Snippet.Length > 0;

    public bool Pinned { get; } = pinned;

    [ObservableProperty]
    private bool _isCurrent;
}

public sealed partial class PanelViewModel
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(180);
    private string? _pendingChatDelete;
    private DispatcherTimer? _searchTimer;

    /// <summary>Sol sütundaki sohbetler: grup başlıkları ve sohbetler karışık.</summary>
    public ObservableCollection<object> ChatList { get; } = [];

    [ObservableProperty]
    private string _chatSearch = "";

    [ObservableProperty]
    private string _currentChatTitle = "Yeni sohbet";

    [ObservableProperty]
    private string _currentChatMeta = "";

    [ObservableProperty]
    private bool _hasCurrentChat;

    [ObservableProperty]
    private bool _isCurrentPinned;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = "";

    [ObservableProperty]
    private bool _isConfirmingChatDelete;

    [ObservableProperty]
    private string _chatDeleteQuestion = "";

    [ObservableProperty]
    private bool _hasChats;

    [ObservableProperty]
    private string _chatListEmpty = "";

    [ObservableProperty]
    private string _chatMessage = "";

    private ChatStore? Chats => _actions.Chats;

    /// <summary>Sohbet listesini yeniler (sohbet eklendi, silindi, başlığı değişti ya da arama yazıldı).</summary>
    public void RefreshChats()
    {
        if (Chats is not { } store)
        {
            return;
        }

        var current = store.CurrentId;
        var now = DateTime.Now;
        ChatList.Clear();
        var query = ChatSearch.Trim();
        if (query.Length > 0)
        {
            var hits = store.Search(query);
            if (hits.Count > 0)
            {
                ChatList.Add(new ChatGroupView($"{hits.Count} sonuç"));
            }

            foreach (var hit in hits)
            {
                ChatList.Add(new ChatItemView(hit.Chat.Id, hit.Chat.Title, When(hit.Chat.Updated.LocalDateTime, now), hit.Snippet, hit.Chat.Pinned)
                {
                    IsCurrent = hit.Chat.Id == current
                });
            }

            ChatListEmpty = hits.Count == 0 ? $"\"{query}\" hiçbir sohbette geçmiyor." : "";
        }
        else
        {
            string? group = null;
            foreach (var chat in store.List())
            {
                var title = chat.Pinned ? "Sabitlenenler" : Group(chat.Updated.LocalDateTime, now);
                if (title != group)
                {
                    ChatList.Add(new ChatGroupView(title));
                    group = title;
                }

                ChatList.Add(new ChatItemView(chat.Id, chat.Title, When(chat.Updated.LocalDateTime, now), "", chat.Pinned) { IsCurrent = chat.Id == current });
            }

            ChatListEmpty = store.Count == 0 ? "Henüz sohbet yok. Konuştukların burada ayrı sohbetler olarak durur." : "";
        }

        HasChats = store.Count > 0;
        UpdateCurrentChat();
    }

    /// <summary>Açık sohbet değişti: konuşma alanı o sohbetin turlarıyla dolar.</summary>
    public void ShowChat(IReadOnlyList<ConversationTurn> turns)
    {
        _currentReply = null;
        Lines.Clear();
        foreach (var turn in turns.TakeLast(MaxLines / 2))
        {
            Lines.Add(new ChatLine(true, turn.User, turn.Time.LocalDateTime));
            Lines.Add(new ChatLine(false, turn.Assistant, turn.Time.LocalDateTime));
        }

        HasLines = Lines.Count > 0;
        IsRenaming = false;
        IsConfirmingChatDelete = false;
        ChatMessage = "";
        RefreshChats();
    }

    partial void OnChatSearchChanged(string value)
    {
        // Yazarken her harfte değil, kısa bir duraksamada aranır.
        if (_searchTimer is null)
        {
            _searchTimer = new DispatcherTimer { Interval = SearchDelay };
            _searchTimer.Tick += (_, _) =>
            {
                _searchTimer.Stop();
                RefreshChats();
            };
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    [RelayCommand]
    private void NewChat()
    {
        GoToChat();
        _actions.OpenChat(null);
        ChatMessage = "";
    }

    [RelayCommand]
    private void OpenChat(string id)
    {
        GoToChat();
        _actions.OpenChat(id);
    }

    [RelayCommand]
    private void StartRename()
    {
        if (Chats?.CurrentId is null)
        {
            return;
        }

        RenameText = CurrentChatTitle;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (Chats?.CurrentId is { } id && RenameText.Trim().Length > 0)
        {
            Chats.Rename(id, RenameText);
        }

        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    [RelayCommand]
    private void TogglePin()
    {
        if (Chats?.CurrentId is { } id)
        {
            Chats.SetPinned(id, !IsCurrentPinned);
        }
    }

    /// <summary>Silme önce sorulur; id verilmezse açık sohbet silinir.</summary>
    [RelayCommand]
    private void AskDeleteChat(string? id)
    {
        var target = id ?? Chats?.CurrentId;
        if (target is null || Chats?.Find(target) is not { } chat)
        {
            return;
        }

        _pendingChatDelete = target;
        ChatDeleteQuestion = $"\"{chat.Title}\" silinsin mi? {chat.TurnCount} mesaj kalıcı olarak silinir.";
        IsConfirmingChatDelete = true;
    }

    [RelayCommand]
    private void ConfirmDeleteChat()
    {
        if (_pendingChatDelete is { } id)
        {
            _actions.DeleteChat(id);
        }

        _pendingChatDelete = null;
        IsConfirmingChatDelete = false;
        ChatMessage = "Sohbet silindi.";
    }

    [RelayCommand]
    private void CancelDeleteChat()
    {
        _pendingChatDelete = null;
        IsConfirmingChatDelete = false;
    }

    [RelayCommand]
    private void ExportChat()
    {
        if (Chats?.CurrentId is { } id)
        {
            ChatMessage = _actions.ExportChat(id) is { } file ? $"Kaydedildi: {file}" : "Dışa aktarılamadı.";
        }
    }

    [RelayCommand]
    private void ClearChatSearch() => ChatSearch = "";

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "RelayCommand üreteci örnek yöntem ister.")]
    private async Task CopyLine(ChatLine line)
    {
        try
        {
            System.Windows.Clipboard.SetText(line.Text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Pano başka bir uygulamada kilitliyse kopyalama sessizce atlanır.
            return;
        }

        line.Copied = true;
        await Task.Delay(1500).ConfigureAwait(true);
        line.Copied = false;
    }

    /// <summary>Bu cevaptan önceki soruyu yeniden sorar (ör. internet yokken gelen cevabı yenilemek için).</summary>
    [RelayCommand]
    private void RetryLine(ChatLine line)
    {
        var index = Lines.IndexOf(line);
        if (index > 0 && Lines.Take(index).LastOrDefault(item => item.IsUser) is { } question && question.Text.Trim().Length > 0)
        {
            _actions.Send(question.Text);
        }
    }

    private void GoToChat()
    {
        IsToday = IsMemory = IsRhythm = IsPrivacy = IsRoutines = IsSettings = false;
        IsChat = true;
    }

    private void UpdateCurrentChat()
    {
        var chat = Chats?.CurrentId is { } id ? Chats.Find(id) : null;
        HasCurrentChat = chat is not null;
        IsCurrentPinned = chat?.Pinned ?? false;
        CurrentChatTitle = chat?.Title ?? "Yeni sohbet";
        CurrentChatMeta = chat is null
            ? "Sorduğun ilk şey bu sohbetin başlığı olur."
            : $"{chat.Created.LocalDateTime.ToString("d MMMM HH:mm", Turkish)} · {chat.TurnCount} soru";
        foreach (var item in ChatList.OfType<ChatItemView>())
        {
            item.IsCurrent = item.Id == chat?.Id;
        }
    }

    private static string Group(DateTime time, DateTime now) => (now.Date - time.Date).Days switch
    {
        0 => "Bugün",
        1 => "Dün",
        < 7 => "Bu hafta",
        < 30 => "Son 30 gün",
        _ => "Daha eski"
    };

    private static string When(DateTime time, DateTime now) => (now.Date - time.Date).Days switch
    {
        0 => time.ToString("HH:mm", CultureInfo.InvariantCulture),
        1 => "Dün",
        < 7 => time.ToString("dddd", Turkish),
        _ => time.ToString("d MMM", Turkish)
    };
}
