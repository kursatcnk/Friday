using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Friday.Core;

namespace Friday.Desktop;

public sealed record RoutineStepView(string Glyph, string Text, bool IsValid = true);

/// <summary>Rutinler sayfasındaki bir kart: rutin ya da özel komut (cümlenin içinde geçince çalışır).</summary>
public sealed record RoutineView(string Key, string Name, string Phrase, IReadOnlyList<RoutineStepView> Steps, bool IsTrigger = false, string When = "");

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
    private RoutineView? _editingRoutine;
    private RoutineView? _pendingRoutineDelete;

    public ObservableCollection<RoutineView> Routines { get; } = [];

    public ObservableCollection<RoutineStepEditor> EditorSteps { get; } = [];

    public IReadOnlyList<RoutineStepKind> StepKinds { get; } = RoutineStep.Kinds;

    public IReadOnlyList<RoutineTemplate> RoutineTemplates { get; } = RoutineStep.Templates;

    public IReadOnlyList<string> ScheduleOptions { get; } = ["Kapalı", "Her gün", "Hafta içi", "Hafta sonu", "Friday açılınca", "Bilgisayara dönünce", "Uygulama açılınca"];

    /// <summary>"Uygulama açılınca" seçiliyken hangi uygulama (ör. Spotify).</summary>
    [ObservableProperty]
    private string _routineTriggerApp = "";

    public bool ScheduleNeedsApp => SelectedSchedule == "Uygulama açılınca";

    public IReadOnlyList<string> RoutineHotkeyOptions { get; } =
        ["Yok", "Ctrl+Alt+1", "Ctrl+Alt+2", "Ctrl+Alt+3", "Ctrl+Alt+4", "Ctrl+Alt+5", "Ctrl+Alt+6", "Ctrl+Alt+7", "Ctrl+Alt+8", "Ctrl+Alt+9"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleNeedsTime))]
    [NotifyPropertyChangedFor(nameof(ScheduleNeedsApp))]
    private string _selectedSchedule = "Kapalı";

    [ObservableProperty]
    private string _routineTime = "09:00";

    [ObservableProperty]
    private string _selectedRoutineHotkey = "Yok";

    /// <summary>Saat kutusu yalnızca gün seçilince görünür.</summary>
    public bool ScheduleNeedsTime => SelectedSchedule is "Her gün" or "Hafta içi" or "Hafta sonu";

    [ObservableProperty]
    private bool _isEditingRoutine;

    [ObservableProperty]
    private string _editorTitle = "Yeni rutin";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoutinePhrase))]
    private string _routineName = "";

    /// <summary>Özel komut: ad cümlenin herhangi bir yerinde geçince çalışır ("baha" → "Baha geldi").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoutinePhrase))]
    private bool _isTriggerRoutine;

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

    /// <summary>Düzenleyicide adın nasıl söyleneceği.</summary>
    public string RoutinePhrase
    {
        get
        {
            var name = RoutineName.Trim();
            if (name.Length == 0)
            {
                return "Rutine bir ad ver; o adı söylemen yeter.";
            }

            return IsTriggerRoutine
                ? $"Söylediğin cümlede \"{name.ToLower(Turkish)}\" geçince çalışır (ör. \"{TurkishGrammar.Capitalize(name)} geldi\")."
                : $"Başlatmak için \"{TurkishGrammar.Capitalize(name)}\" ya da \"{name.ToLower(Turkish)} rutinini başlat\" de.";
        }
    }

    /// <summary>Özel komut sesle eklenince ya da ayarlar dışarıdan değişince listeyi yeniler.</summary>
    public void ReloadRoutines() => LoadRoutines();

    [RelayCommand]
    private void NewRoutine() => OpenEditor(null, "Yeni rutin", "", [], isTrigger: false);

    [RelayCommand]
    private void EditRoutine(RoutineView view)
    {
        var source = view.IsTrigger ? _settings.Triggers : _settings.Routines;
        if (source.TryGetValue(view.Key, out var steps))
        {
            OpenEditor(view, view.IsTrigger ? "Özel komutu düzenle" : "Rutini düzenle", view.Key, steps, view.IsTrigger);
        }
    }

    [RelayCommand]
    private void UseTemplate(RoutineTemplate template) =>
        OpenEditor(null, "Yeni rutin", _settings.Routines.ContainsKey(template.Name) ? $"{template.Name} 2" : template.Name, template.Steps, isTrigger: false);

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
            RoutineError = "En az iki harflik bir ad ver.";
            return;
        }

        if (name.Length > 40)
        {
            RoutineError = "Ad çok uzun; söylemesi kolay, kısa bir ad seç.";
            return;
        }

        var target = IsTriggerRoutine ? _settings.Triggers : _settings.Routines;
        var renamedOrMoved = _editingRoutine is null || _editingRoutine.IsTrigger != IsTriggerRoutine
                             || !string.Equals(_editingRoutine.Key, name, StringComparison.OrdinalIgnoreCase);
        if (renamedOrMoved && target.ContainsKey(name))
        {
            RoutineError = IsTriggerRoutine ? $"\"{name}\" için bir özel komut zaten var." : $"\"{name}\" adında bir rutin zaten var.";
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

        string? schedule = null;
        if (!IsTriggerRoutine && SelectedSchedule != "Kapalı")
        {
            var days = SelectedSchedule switch { "Hafta içi" => "haftaici", "Hafta sonu" => "haftasonu", _ => "hergun" };
            schedule = SelectedSchedule switch
            {
                "Friday açılınca" => RoutineSchedule.Startup,
                "Bilgisayara dönünce" => "donus",
                "Uygulama açılınca" => RoutineTriggerApp.Trim().Length >= 2 ? "uygulama:" + RoutineTriggerApp.Trim() : null,
                _ => RoutineSchedule.Parse($"{RoutineTime.Trim()} {days}")?.Format()
            };
            if (schedule is null)
            {
                RoutineError = SelectedSchedule == "Uygulama açılınca" ? "Hangi uygulama açılınca çalışsın? Adını yaz (ör. Spotify)." : "Saati 09:00 ya da 18:30 gibi yaz.";
                return;
            }
        }

        var hotkey = !IsTriggerRoutine && SelectedRoutineHotkey != "Yok" ? SelectedRoutineHotkey : null;
        if (hotkey is not null && _settings.RoutineHotkeys.FirstOrDefault(pair => pair.Value == hotkey
                && !string.Equals(pair.Key, _editingRoutine?.Key, StringComparison.OrdinalIgnoreCase)) is { Key: { } owner })
        {
            RoutineError = $"{hotkey} zaten \"{owner}\" rutininde.";
            return;
        }

        if (_editingRoutine is { } original)
        {
            (original.IsTrigger ? _settings.Triggers : _settings.Routines).Remove(original.Key);
            _settings.RoutineSchedules.Remove(original.Key);
            _settings.RoutineHotkeys.Remove(original.Key);
        }

        if (schedule is not null)
        {
            _settings.RoutineSchedules[name] = schedule;
        }

        if (hotkey is not null)
        {
            _settings.RoutineHotkeys[name] = hotkey;
        }

        target[name] = steps;
        _actions.Save(_settings);
        IsEditingRoutine = false;
        RoutineError = "";
        RoutineMessage = $"\"{TurkishGrammar.Capitalize(name)}\" kaydedildi.";
        LoadRoutines();
    }

    [RelayCommand]
    private void AskDeleteRoutine(RoutineView view)
    {
        _pendingRoutineDelete = view;
        RoutineDeleteQuestion = view.IsTrigger
            ? $"\"{view.Key}\" özel komutu silinsin mi?"
            : $"\"{TurkishGrammar.Capitalize(view.Key)}\" rutini silinsin mi?";
        IsConfirmingRoutineDelete = true;
    }

    [RelayCommand]
    private void ConfirmDeleteRoutine()
    {
        if (_pendingRoutineDelete is { } view && (view.IsTrigger ? _settings.Triggers : _settings.Routines).Remove(view.Key))
        {
            if (!view.IsTrigger)
            {
                _settings.RoutineSchedules.Remove(view.Key);
                _settings.RoutineHotkeys.Remove(view.Key);
            }

            _actions.Save(_settings);
            RoutineMessage = $"\"{TurkishGrammar.Capitalize(view.Key)}\" silindi.";
            if (_editingRoutine == view)
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
        if (_editingRoutine is { } view)
        {
            AskDeleteRoutine(view);
        }
    }

    [RelayCommand]
    private void RunRoutine(RoutineView view)
    {
        GoToChat();
        // Özel komut adı söylenmiş gibi, rutin "… rutinini çalıştır" diye gider; ikisi de hızlı yoldan çalışır.
        _actions.Send(view.IsTrigger ? TurkishGrammar.Capitalize(view.Key) : $"{view.Key} rutinini çalıştır");
    }

    private void OpenEditor(RoutineView? view, string title, string name, IEnumerable<string> steps, bool isTrigger)
    {
        _editingRoutine = view;
        EditorTitle = title;
        RoutineName = name;
        IsTriggerRoutine = isTrigger;
        var schedule = view is { IsTrigger: false } && _settings.RoutineSchedules.TryGetValue(view.Key, out var saved) ? RoutineSchedule.Parse(saved) : null;
        SelectedSchedule = schedule switch
        {
            null => "Kapalı",
            { Trigger: "donus" } => "Bilgisayara dönünce",
            { Trigger: not null } => "Uygulama açılınca",
            { OnStart: true } => "Friday açılınca",
            { Days: "haftaici" } => "Hafta içi",
            { Days: "haftasonu" } => "Hafta sonu",
            _ => "Her gün"
        };
        RoutineTime = schedule?.At?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "09:00";
        RoutineTriggerApp = schedule?.Trigger is { } trigger && trigger.StartsWith("uygulama:", StringComparison.Ordinal) ? trigger["uygulama:".Length..] : "";
        SelectedRoutineHotkey = view is { IsTrigger: false } && _settings.RoutineHotkeys.TryGetValue(view.Key, out var key) ? key : "Yok";
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
        CanDeleteEditedRoutine = view is not null;
        IsEditingRoutine = true;
    }

    private void LoadRoutines()
    {
        Routines.Clear();
        var comparer = StringComparer.Create(Turkish, true);
        foreach (var (name, steps) in _settings.Routines.OrderBy(pair => pair.Key, comparer))
        {
            var when = new List<string>();
            if (_settings.RoutineSchedules.TryGetValue(name, out var scheduleText) && RoutineSchedule.Parse(scheduleText) is { } schedule)
            {
                when.Add(schedule.Describe());
            }

            if (_settings.RoutineHotkeys.TryGetValue(name, out var hotkey))
            {
                when.Add(hotkey);
            }

            Routines.Add(new RoutineView(name, TurkishGrammar.Capitalize(name), $"\"{TurkishGrammar.Capitalize(name)}\" de", Describe(steps),
                When: string.Join(" · ", when)));
        }

        HomeRoutines.Clear();
        foreach (var routine in Routines)
        {
            HomeRoutines.Add(routine);
        }

        foreach (var (phrase, steps) in _settings.Triggers.OrderBy(pair => pair.Key, comparer))
        {
            Routines.Add(new RoutineView(phrase, TurkishGrammar.Capitalize(phrase), $"Cümlede \"{phrase}\" geçince", Describe(steps), IsTrigger: true));
        }
    }

    private static List<RoutineStepView> Describe(IEnumerable<string> steps)
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

        return views;
    }
}
