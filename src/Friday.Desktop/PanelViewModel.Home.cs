using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Friday.Desktop;

/// <summary>Ana ekran düzenleyicisindeki bir kart: anahtarı, adı ve görünür mü.</summary>
public sealed partial class HomeCardOption(string key, string label, bool visible, Action changed) : ObservableObject
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    [ObservableProperty]
    private bool _visible = visible;

    partial void OnVisibleChanged(bool value) => changed();
}

/// <summary>Sistem kartındaki bir satır: ad, değer ve çubuğun doluluğu (0–1).</summary>
public sealed record SystemRow(string Label, string Value, double Fraction);

public sealed partial class PanelViewModel
{
    /// <summary>Bugün ekranının bütün kartları, varsayılan sırasıyla.</summary>
    public static IReadOnlyList<(string Key, string Label)> AllHomeCards { get; } =
    [
        ("greeting", "Selam ve hava"),
        ("upcoming", "Yaklaşanlar"),
        ("plan", "Günün planı"),
        ("rhythm", "Ritim"),
        ("stopwatch", "Kronometre"),
        ("todos", "Yapılacaklar"),
        ("habits", "Alışkanlıklar"),
        ("spending", "Harcamalar"),
        ("notes", "Notlar"),
        ("routines", "Rutinler"),
        ("system", "Sistem"),
        ("screentime", "Ekran süresi"),
        ("ideas", "Dene")
    ];

    public ObservableCollection<HomeCardOption> HomeCards { get; } = [];

    public ObservableCollection<RoutineView> HomeRoutines { get; } = [];

    public ObservableCollection<SystemRow> SystemRows { get; } = [];

    [ObservableProperty]
    private string _systemNote = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HomeEditorLabel))]
    private bool _isCustomizingHome;

    public string HomeEditorLabel => IsCustomizingHome ? "Düzenlemeyi bitir" : "Ana ekranı düzenle";

    /// <summary>Kart sırası ya da görünürlüğü değişti; sayfa kartları yeniden dizer.</summary>
    public event EventHandler? HomeLayoutChanged;

    /// <summary>Görünecek kartlar sırasıyla.</summary>
    public IReadOnlyList<string> HomeOrder => [.. HomeCards.Where(card => card.Visible).Select(card => card.Key)];

    /// <summary>Rahatsız etme açıkken başlıkta görünen rozet ("Rahatsız etme · 15:30"); kapalıyken boş.</summary>
    [ObservableProperty]
    private string _quietBadge = "";

    [RelayCommand]
    private void EndQuietNow() => _actions.EndQuiet();

    public static IReadOnlyList<(string Name, string Color)> Accents { get; } =
    [
        ("Windows", "sistem"), ("Turuncu", "#FF8A5B"), ("Altın", "#F2CC8F"), ("Yeşil", "#7FD4B0"), ("Mavi", "#6FB7FF"), ("Mor", "#B9A8FF"), ("Pembe", "#FF7EB6")
    ];

    public IReadOnlyList<string> AccentOptions { get; } = [.. Accents.Select(accent => accent.Name)];

    public IReadOnlyList<string> ScaleOptions { get; } = ["Küçük", "Normal", "Büyük", "Çok büyük"];

    [ObservableProperty]
    private string _selectedAccent = "Windows";

    public IReadOnlyList<string> ThemeOptions { get; } = ["Windows'a uy", "Açık", "Koyu"];

    [ObservableProperty]
    private string _selectedTheme = "Windows'a uy";

    [ObservableProperty]
    private string _selectedScale = "Normal";

    private static double ScaleValue(string name) => name switch { "Küçük" => 0.9, "Büyük" => 1.1, "Çok büyük" => 1.25, _ => 1.0 };

    private static string ScaleName(double value) => value switch { < 0.95 => "Küçük", > 1.18 => "Çok büyük", > 1.05 => "Büyük", _ => "Normal" };

    /// <summary>Ctrl+1…7: Bugün, Sohbet, Ritim, Hafıza, Rutinler, Gizlilik, Ayarlar.</summary>
    public void ShowPage(int number)
    {
        var page = number switch { 2 => "chat", 3 => "rhythm", 4 => "memory", 5 => "routines", 7 => "settings", _ => "" };
        GoTo(page);
        if (number == 1)
        {
            IsToday = true;
        }
        else if (number == 6)
        {
            IsPrivacy = true;
        }
    }

    public bool ShowsHomeCard(string key) => HomeCards.Any(card => card.Key == key && card.Visible);

    private void LoadHome()
    {
        HomeCards.Clear();
        var order = _settings.HomeOrder.Where(key => AllHomeCards.Any(card => card.Key == key)).Distinct().ToList();
        // Kayıtlı düzende olmayan (yeni sürümle gelen) kart, varsayılan sırada kendinden önce gelen kartın arkasına girer.
        var defaults = AllHomeCards.Select(card => card.Key).ToList();
        foreach (var key in defaults.Where(key => !order.Contains(key)).ToList())
        {
            var before = defaults.Take(defaults.IndexOf(key)).LastOrDefault(order.Contains);
            order.Insert(before is null ? 0 : order.IndexOf(before) + 1, key);
        }
        foreach (var key in order)
        {
            HomeCards.Add(new HomeCardOption(key, AllHomeCards.First(card => card.Key == key).Label, !_settings.HomeHidden.Contains(key), HomeChanged));
        }

        HomeLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Düzen anında uygulanır ve yalnızca ayar dosyasına yazılır (asistan yeniden yapılandırılmaz).</summary>
    private void HomeChanged()
    {
        _settings.HomeOrder = [.. HomeCards.Select(card => card.Key)];
        _settings.HomeHidden = [.. HomeCards.Where(card => !card.Visible).Select(card => card.Key)];
        _actions.Persist(_settings);
        HomeLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleHomeEditor() => IsCustomizingHome = !IsCustomizingHome;

    [RelayCommand]
    private void MoveHomeCardUp(HomeCardOption card)
    {
        var index = HomeCards.IndexOf(card);
        if (index > 0)
        {
            HomeCards.Move(index, index - 1);
            HomeChanged();
        }
    }

    [RelayCommand]
    private void MoveHomeCardDown(HomeCardOption card)
    {
        var index = HomeCards.IndexOf(card);
        if (index >= 0 && index < HomeCards.Count - 1)
        {
            HomeCards.Move(index, index + 1);
            HomeChanged();
        }
    }

    [RelayCommand]
    private void ResetHome()
    {
        _settings.HomeOrder = [];
        _settings.HomeHidden = [];
        _actions.Persist(_settings);
        LoadHome();
    }

    /// <summary>Bugün ekranındaki rutin düğmesi: sayfa değişmeden çalışır, sonucu Friday söyler.</summary>
    [RelayCommand]
    private void RunRoutineHere(RoutineView view) => _actions.Send($"{view.Key} rutinini çalıştır");

    public void ShowSystem(IReadOnlyList<SystemRow> rows, string note)
    {
        if (SystemRows.Count == rows.Count)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (SystemRows[index] != rows[index])
                {
                    SystemRows[index] = rows[index];
                }
            }
        }
        else
        {
            SystemRows.Clear();
            foreach (var row in rows)
            {
                SystemRows.Add(row);
            }
        }

        SystemNote = note;
    }
}
