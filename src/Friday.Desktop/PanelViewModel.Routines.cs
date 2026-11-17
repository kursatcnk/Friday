using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;

namespace Friday.Desktop;

public sealed record RoutineStepView(string Glyph, string Text, bool IsValid = true);

/// <summary>Rutinler sayfasındaki bir kart.</summary>
public sealed record RoutineView(string Key, string Name, string Phrase, IReadOnlyList<RoutineStepView> Steps);

/// <summary>Düzenleyicide bir adım: türü sabit, değeri yazılabilir.</summary>
public sealed partial class RoutineStepEditor(RoutineStepKind kind, string value) : ObservableObject
{
    public RoutineStepKind Kind { get; } = kind;

    public string Label => Kind.Label;

    public string Glyph => Kind.Glyph;

    public string Hint => Kind.Hint;

    public bool NeedsValue => Kind.NeedsValue;

    [ObservableProperty]
    private string _value = value;

    /// <summary>settings.json yazımı; değer eksik ya da hatalıysa FormatException.</summary>
    public RoutineStep ToStep() => RoutineStep.Parse(NeedsValue ? $"{Kind.Key}:{Value.Trim()}" : Kind.Key);
}

public sealed partial class PanelViewModel
{
    private string? _editingRoutine;
    private string? _pendingRoutineDelete;

    public ObservableCollection<RoutineView> Routines { get; } = [];

    public ObservableCollection<RoutineStepEditor> EditorSteps { get; } = [];

    public IReadOnlyList<RoutineStepKind> StepKinds { get; } = RoutineStep.Kinds;

    public IReadOnlyList<RoutineTemplate> RoutineTemplates { get; } = RoutineStep.Templates;

    [ObservableProperty]
    private bool _isEditingRoutine;

    [ObservableProperty]
    private string _editorTitle = "Yeni rutin";

    [ObservableProperty]
    private string _routineName = "";

    [ObservableProperty]
    private string _routineError = "";

    [ObservableProperty]
    private string _routineMessage = "";

    [ObservableProperty]
    private bool _isConfirmingRoutineDelete;

    [ObservableProperty]
    private string _routineDeleteQuestion = "";

    [ObservableProperty]
    private bool _canDeleteEditedRoutine;

    /// <summary>Düzenleyicide adın nasıl söyleneceği: "Sabah" ya da "sabah rutinini başlat".</summary>
    public string RoutinePhrase => RoutineName.Trim().Length == 0
        ? "Rutine bir ad ver; o adı söylemen yeter."
        : $"Başlatmak için \"{TurkishGrammar.Capitalize(RoutineName.Trim())}\" ya da \"{RoutineName.Trim().ToLower(Turkish)} rutinini başlat\" de.";

    partial void OnRoutineNameChanged(string value) => OnPropertyChanged(nameof(RoutinePhrase));

    [RelayCommand]
    private void NewRoutine()
    {
        _editingRoutine = null;
        EditorTitle = "Yeni rutin";
        RoutineName = "";
        EditorSteps.Clear();
        RoutineError = "";
        CanDeleteEditedRoutine = false;
        IsEditingRoutine = true;
    }

    [RelayCommand]
    private void EditRoutine(string key)
    {
        if (!_settings.Routines.TryGetValue(key, out var steps))
        {
            return;
        }

        _editingRoutine = key;
        EditorTitle = "Rutini düzenle";
        RoutineName = key;
        EditorSteps.Clear();
        foreach (var text in steps)
        {
            try
            {
                var step = RoutineStep.Parse(text);
                EditorSteps.Add(new RoutineStepEditor(step.Kind, step.Value));
            }
            catch (FormatException)
            {
                // Bozuk adım düzenleyiciye alınmaz; kaydedilince dosyadan da düşer.
            }
        }

        RoutineError = "";
        CanDeleteEditedRoutine = true;
        IsEditingRoutine = true;
    }

    [RelayCommand]
    private void UseTemplate(RoutineTemplate template)
    {
        _editingRoutine = null;
        EditorTitle = "Yeni rutin";
        RoutineName = _settings.Routines.ContainsKey(template.Name) ? $"{template.Name} 2" : template.Name;
        EditorSteps.Clear();
        foreach (var step in template.Steps.Select(RoutineStep.Parse))
        {
            EditorSteps.Add(new RoutineStepEditor(step.Kind, step.Value));
        }

        RoutineError = "";
        CanDeleteEditedRoutine = false;
        IsEditingRoutine = true;
    }

    [RelayCommand]
    private void AddStep(RoutineStepKind kind) => EditorSteps.Add(new RoutineStepEditor(kind, ""));

    [RelayCommand]
    private void RemoveStep(RoutineStepEditor step) => EditorSteps.Remove(step);

    [RelayCommand]
    private void MoveStepUp(RoutineStepEditor step)
    {
        var index = EditorSteps.IndexOf(step);
        if (index > 0)
        {
            EditorSteps.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveStepDown(RoutineStepEditor step)
    {
        var index = EditorSteps.IndexOf(step);
        if (index >= 0 && index < EditorSteps.Count - 1)
        {
            EditorSteps.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private void CancelRoutine()
    {
        IsEditingRoutine = false;
        RoutineError = "";
    }

    [RelayCommand]
    private void SaveRoutine()
    {
        var name = string.Join(' ', RoutineName.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLower(Turkish);
        if (name.Length < 2)
        {
            RoutineError = "Rutine en az iki harflik bir ad ver.";
            return;
        }

        if (name.Length > 40)
        {
            RoutineError = "Ad çok uzun; söylemesi kolay, kısa bir ad seç.";
            return;
        }

        if (_settings.Routines.ContainsKey(name) && !string.Equals(name, _editingRoutine, StringComparison.OrdinalIgnoreCase))
        {
            RoutineError = $"\"{name}\" adında bir rutin zaten var.";
            return;
        }

        if (EditorSteps.Count == 0)
        {
            RoutineError = "En az bir adım ekle.";
            return;
        }

        var steps = new List<string>();
        for (var index = 0; index < EditorSteps.Count; index++)
        {
            try
            {
                steps.Add(EditorSteps[index].ToStep().Format());
            }
            catch (FormatException)
            {
                RoutineError = $"{index + 1}. adım ({EditorSteps[index].Label}) eksik ya da hatalı: {EditorSteps[index].Hint}";
                return;
            }
        }

        if (_editingRoutine is not null)
        {
            _settings.Routines.Remove(_editingRoutine);
        }

        _settings.Routines[name] = steps;
        _actions.Save(_settings);
        IsEditingRoutine = false;
        RoutineError = "";
        RoutineMessage = $"\"{TurkishGrammar.Capitalize(name)}\" kaydedildi.";
        LoadRoutines();
    }

    [RelayCommand]
    private void AskDeleteRoutine(string key)
    {
        _pendingRoutineDelete = key;
        RoutineDeleteQuestion = $"\"{TurkishGrammar.Capitalize(key)}\" rutini silinsin mi?";
        IsConfirmingRoutineDelete = true;
    }

    [RelayCommand]
    private void ConfirmDeleteRoutine()
    {
        if (_pendingRoutineDelete is { } key && _settings.Routines.Remove(key))
        {
            _actions.Save(_settings);
            RoutineMessage = $"\"{TurkishGrammar.Capitalize(key)}\" silindi.";
            if (string.Equals(key, _editingRoutine, StringComparison.OrdinalIgnoreCase))
            {
                IsEditingRoutine = false;
            }

            LoadRoutines();
        }

        _pendingRoutineDelete = null;
        IsConfirmingRoutineDelete = false;
    }

    [RelayCommand]
    private void CancelDeleteRoutine()
    {
        _pendingRoutineDelete = null;
        IsConfirmingRoutineDelete = false;
    }

    [RelayCommand]
    private void DeleteEditedRoutine()
    {
        if (_editingRoutine is { } key)
        {
            AskDeleteRoutine(key);
        }
    }

    [RelayCommand]
    private void RunRoutine(string key)
    {
        GoToChat();
        _actions.Send($"{key} rutinini çalıştır");
    }

    private void LoadRoutines()
    {
        Routines.Clear();
        foreach (var (name, steps) in _settings.Routines.OrderBy(pair => pair.Key, StringComparer.Create(Turkish, true)))
        {
            var views = new List<RoutineStepView>();
            foreach (var text in steps)
            {
                try
                {
                    var step = RoutineStep.Parse(text);
                    views.Add(new RoutineStepView(step.Kind.Glyph, step.Describe()));
                }
                catch (FormatException)
                {
                    views.Add(new RoutineStepView("", $"Geçersiz adım: {text}", IsValid: false));
                }
            }

            Routines.Add(new RoutineView(name, TurkishGrammar.Capitalize(name), $"\"{TurkishGrammar.Capitalize(name)}\" de", views));
        }
    }
}
