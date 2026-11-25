using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;

namespace Friday.Desktop;

/// <summary>Ajandada bir satır: saat, ne olduğu ve (hatırlatma ya da işse) silme/tamamlama.</summary>
public sealed record AgendaEntryView(string? Id, AgendaKind Kind, string Time, string Text)
{
    public string Glyph => Kind switch
    {
        AgendaKind.Calendar => "",
        AgendaKind.Reminder => "",
        AgendaKind.Overdue => "",
        AgendaKind.Deadline => "",
        AgendaKind.SpecialDay => "",
        AgendaKind.Bill => "",
        _ => ""
    };

    public string KindLabel => Kind switch
    {
        AgendaKind.Calendar => "Takvim",
        AgendaKind.Reminder => "Hatırlatma",
        AgendaKind.Overdue => "Gecikmiş iş",
        AgendaKind.Deadline => "Son günü",
        AgendaKind.SpecialDay => "Özel gün",
        AgendaKind.Bill => "Ödeme",
        _ => "Her hafta"
    };

    public bool HasTime => Time.Length > 0;

    public bool CanDelete => Kind == AgendaKind.Reminder && Id is not null;

    public bool CanComplete => Kind is AgendaKind.Deadline or AgendaKind.Overdue && Id is not null;

    public bool IsOverdue => Kind == AgendaKind.Overdue;

    public bool IsRoutine => Kind == AgendaKind.Weekly;
}

/// <summary>Ajandada bir gün kartı.</summary>
public sealed record AgendaDayView(string Title, string Date, bool IsToday, IReadOnlyList<AgendaEntryView> Items)
{
    public bool IsEmpty => Items.Count == 0;

    public string Count => Items.Count == 0 ? "" : Items.Count.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Ajanda sayfası: önümüzdeki 7 gün, gün gün. Sesle sorulan "bu hafta neler var" cevabıyla aynı kaynaktan gelir
/// (<see cref="DayAgenda"/>): takvim, hatırlatmalar, son günü gelen ve gecikmiş işler, özel günler, ödemeler ve
/// hafızadaki haftalık düzen. Üstteki kutuya "yarın 9'da dişçi" yazmak hatırlatma, "cuma sunumu bitir" iş ekler.
/// </summary>
public sealed partial class PanelViewModel
{
    public ObservableCollection<AgendaDayView> AgendaDays { get; } = [];

    [ObservableProperty]
    private string _newAgendaItem = "";

    [ObservableProperty]
    private string _agendaMessage = "";

    [ObservableProperty]
    private string _agendaSummary = "";

    /// <summary>Ajandayı yeniden çizer (kayıtlar değişince App de çağırır).</summary>
    public void RefreshAgenda()
    {
        if (IsAgenda)
        {
            ShowAgenda(_actions.Agenda(DateTime.Now), DateTime.Now);
        }
    }

    public void ShowAgenda(IReadOnlyList<AgendaItem> items, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        AgendaDays.Clear();
        for (var offset = 0; offset < 7; offset++)
        {
            var day = today.AddDays(offset);
            var title = offset switch { 0 => "Bugün", 1 => "Yarın", _ => TurkishGrammar.Capitalize(WeeklyFacts.DayName(day)) };
            var entries = items.Where(item => item.Day == day)
                .Select(item => new AgendaEntryView(item.Id, item.Kind, item.Time is { } time ? time.ToString("HH:mm", CultureInfo.InvariantCulture) : "", item.Text))
                .ToList();
            AgendaDays.Add(new AgendaDayView(title, day.ToString("d MMMM", Turkish), offset == 0, entries));
        }

        var timed = items.Count(item => item.Kind is AgendaKind.Reminder or AgendaKind.Calendar);
        var due = items.Count(item => item.Kind is AgendaKind.Deadline or AgendaKind.Overdue);
        var late = items.Count(item => item.Kind == AgendaKind.Overdue);
        AgendaSummary = items.Count == 0
            ? "Önümüzdeki 7 gün boş."
            : $"7 günde {timed} hatırlatma ya da etkinlik, {due} son günlü iş" + (late > 0 ? $", {late} gecikmiş" : "") + ".";
    }

    [RelayCommand]
    private async Task AddAgendaItem()
    {
        var text = NewAgendaItem.Trim();
        if (text.Length == 0)
        {
            return;
        }

        AgendaMessage = await _actions.AddToAgenda(text);
        NewAgendaItem = "";
        RefreshAgenda();
    }

    [RelayCommand]
    private void DeleteAgendaItem(AgendaEntryView? entry)
    {
        if (entry is { CanDelete: true, Id: { } id })
        {
            _actions.DeleteReminder(id);
            AgendaMessage = $"\"{entry.Text}\" hatırlatması silindi. Yanlışlıkla olduysa Ctrl+Z.";
            RefreshAgenda();
        }
    }

    [RelayCommand]
    private void CompleteAgendaItem(AgendaEntryView? entry)
    {
        if (entry is { CanComplete: true, Id: { } id })
        {
            _actions.ToggleTodo(id, true);
            AgendaMessage = $"\"{entry.Text}\" tamamlandı.";
            RefreshAgenda();
        }
    }

    partial void OnIsAgendaChanged(bool value)
    {
        if (value)
        {
            AgendaMessage = "";
            RefreshAgenda();
        }
    }
}
