using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;

namespace Friday.Desktop;

/// <summary>"Bugün" ekranındaki yapılacaklar listesinde bir iş.</summary>
public sealed record TodoView(string Id, string Text, bool IsDone, string DueLabel = "", bool IsUrgent = false, bool Important = false)
{
    public bool HasDue => DueLabel.Length > 0;

    /// <summary>Önemli işler yıldızla başlar.</summary>
    public string Display => (Important ? "★ " : "") + Text;
}

public sealed partial class PanelViewModel
{
    public ObservableCollection<TodoView> Todos { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTodoCommand))]
    private string _newTodo = "";

    [ObservableProperty]
    private string _todoSummary = "";

    public void ShowTodos(IReadOnlyList<TodoItem> items)
    {
        Todos.Clear();
        foreach (var item in items.Take(8))
        {
            var label = item.IsDone ? "" : TodoDue.Label(item.Due, DateOnly.FromDateTime(DateTime.Now));
            Todos.Add(new TodoView(item.Id, item.Text, item.IsDone, label, label is "Gecikti" or "Bugün", item.Important));
        }

        var open = items.Count(item => !item.IsDone);
        TodoSummary = items.Count == 0 ? "\"Yapılacaklara ekle: …\" de" : open == 0 ? "Hepsi bitti" : $"{open} iş";
    }

    private bool CanAddTodo() => NewTodo.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanAddTodo))]
    private void AddTodo()
    {
        _actions.AddTodo(NewTodo.Trim());
        NewTodo = "";
    }

    [RelayCommand]
    private void ToggleTodo(TodoView item) => _actions.ToggleTodo(item.Id, !item.IsDone);

    [RelayCommand]
    private void RemoveTodo(TodoView item) => _actions.RemoveTodo(item.Id);

    /// <summary>İşe 25 dakikalık odak: oturum o işe sayılır, bitince "bitirdin mi?" sorulur; halka Ritim sayfasında görünür.</summary>
    [RelayCommand]
    private void FocusTodo(TodoView item)
    {
        RhythmNote = _actions.FocusOnTask(item.Text, 25);
        GoTo("rhythm");
    }
}
