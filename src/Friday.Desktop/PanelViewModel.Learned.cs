using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Friday.Desktop;

/// <summary>Friday'in kendi kendine öğrendiği bir şey; <see cref="Group"/> ve <see cref="Key"/> silmek için.</summary>
public sealed record LearnedItemView(string Group, string Key, string Text, string Detail)
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>Öğrendiklerinden bir grup: kısayollar, düzeltmeler, okunuşlar, adlar, üslup, susturulanlar, okunan belgeler.</summary>
public sealed record LearnedGroupView(string Key, string Title, string Glyph, string Hint, IReadOnlyList<LearnedItemView> Items)
{
    public bool IsEmpty => Items.Count == 0;

    public string Count => Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// "Öğrendikleri" sayfası: Friday konuştukça kendi kendine öğrenir (iki kez aynı istek → kısayol, "yanlış anladın" →
/// düzeltme, "X adını öğren", okunuşlar, cevap ve yazı üslubu, "bunu bir daha sorma", okuduğu belgeler). Bunlar eskiden
/// görünmüyordu; yanlış öğrenilen bir şey ancak dosya elle düzenlenerek silinebiliyordu.
/// </summary>
public sealed partial class PanelViewModel
{
    public ObservableCollection<LearnedGroupView> LearnedGroups { get; } = [];

    [ObservableProperty]
    private string _learnedMessage = "";

    [ObservableProperty]
    private string _learnedSummary = "";

    public void RefreshLearned()
    {
        LearnedGroups.Clear();
        foreach (var group in _actions.Learned())
        {
            LearnedGroups.Add(group);
        }

        var total = LearnedGroups.Sum(group => group.Items.Count);
        LearnedSummary = total == 0 ? "Henüz kendi kendine bir şey öğrenmedi." : $"{total} öğrenilmiş şey, {LearnedGroups.Count(group => !group.IsEmpty)} grupta.";
    }

    [RelayCommand]
    private void ForgetLearned(LearnedItemView? item)
    {
        if (item is null)
        {
            return;
        }

        LearnedMessage = _actions.ForgetLearned(item.Group, item.Key);
        RefreshLearned();
    }

    partial void OnIsLearnedChanged(bool value)
    {
        if (value)
        {
            LearnedMessage = "";
            RefreshLearned();
        }
    }
}
