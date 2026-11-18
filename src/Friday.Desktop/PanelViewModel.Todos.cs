using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;

namespace Friday.Desktop;

/// <summary>"Bugün" ekranındaki yapılacaklar listesinde bir iş.</summary>
public sealed record TodoView(string Id, string Text, bool IsDone);

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
            Todos.Add(new TodoView(item.Id, item.Text, item.IsDone));
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
}
