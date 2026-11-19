using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Friday.Desktop;

/// <summary>Komut paletinde bir sonuç: başlık, açıklama, simge ve seçilince yapılacak iş.</summary>
public sealed record PaletteItem(string Title, string Detail, string Glyph, Action Run);

/// <summary>
/// Ctrl+K komut paleti: sayfalar, rutinler, sohbetler, notlar, yapılacaklar, hafıza ve sık komutlar tek kutuda aranır;
/// Enter ile çalışır. Eşleşme yoksa yazılan şey doğrudan Friday'e sorulur.
/// </summary>
public sealed partial class PanelViewModel
{
    private static readonly (string Title, int Page, string Glyph)[] PalettePages =
    [
        ("Bugün", 1, "\uE80F"), ("Sohbet", 2, "\uE8BD"), ("Ritim", 3, "\uE9D9"), ("Hafıza", 4, "\uE8F1"),
        ("Rutinler", 5, "\uE945"), ("Gizlilik", 6, "\uEA18"), ("Ayarlar", 7, "\uE713")
    ];

    private static readonly string[] PaletteCommands =
    [
        "Mola ver", "25 dakika odaklanacağım", "4 pomodoro yap", "Odağı bitir", "Nefes egzersizi yapalım", "Durum raporu",
        "Bugün neler var?", "Yarın hava nasıl?", "Ekran sürem", "Bu hafta neler yaptım", "1 saat rahatsız etme",
        "Rahatsız etmeyi kapat", "Geçici dosyaları temizle", "Diskimde ne yer kaplıyor", "Okuma listem", "Masalarım",
        "Listelerim", "Günlüğümü oku", "Hedefime ne kadar kaldı", "Gündem ne?", "Kopyaladıklarım", "Yeni sohbet",
        "Günümü planla", "İnternetim nasıl", "Bu ay ne kadar harcadım", "Harcamaları Excel'e aktar", "Sabit giderlerim",
        "Alışkanlıklarım nasıl gidiyor", "Kelime çalışalım", "Kaç kelime biliyorum", "Yaklaşan özel günler", "Kısaltmalarım",
        "Panodaki resmi oku", "Hangi uygulamalar ses çıkarıyor", "Süre sınırlarım", "Döviz alarmlarım", "Günümü yazalım",
        "Haftalık raporu kaydet", "Ben yokken ne dedin", "Şimdi ne yapayım", "Panodaki fişi harcamaya ekle", "Ekrandaki yazıyı kopyala", "Panodakini yapılacaklara ekle", "Kopyaladığımı not al"
    ];

    public ObservableCollection<PaletteItem> PaletteResults { get; } = [];

    [ObservableProperty]
    private bool _isPaletteOpen;

    [ObservableProperty]
    private string _paletteQuery = "";

    [ObservableProperty]
    private PaletteItem? _selectedPaletteItem;

    public void OpenPalette()
    {
        PaletteQuery = "";
        RefreshPalette();
        IsPaletteOpen = true;
    }

    [RelayCommand]
    private void ClosePalette() => IsPaletteOpen = false;

    /// <summary>Seçili sonucu çalıştırır (seçim yoksa ilkini).</summary>
    public void RunPalette()
    {
        var item = SelectedPaletteItem ?? (PaletteResults.Count > 0 ? PaletteResults[0] : null);
        IsPaletteOpen = false;
        item?.Run();
    }

    /// <summary>Ok tuşlarıyla seçim.</summary>
    public void MovePalette(int step)
    {
        if (PaletteResults.Count == 0)
        {
            return;
        }

        var index = SelectedPaletteItem is null ? -1 : PaletteResults.IndexOf(SelectedPaletteItem);
        SelectedPaletteItem = PaletteResults[Math.Clamp(index + step, 0, PaletteResults.Count - 1)];
    }

    partial void OnPaletteQueryChanged(string value) => RefreshPalette();

    private void RefreshPalette()
    {
        var query = Fold(PaletteQuery);
        var items = new List<(int Score, PaletteItem Item)>();

        void Add(string title, string detail, string glyph, Action run)
        {
            var score = Score(query, title, detail);
            if (score > 0)
            {
                items.Add((score, new PaletteItem(title, detail, glyph, run)));
            }
        }

        foreach (var (title, page, glyph) in PalettePages)
        {
            Add(title, "Sayfa · Ctrl+" + page.ToString(CultureInfo.InvariantCulture), glyph, () => ShowPage(page));
        }

        foreach (var routine in HomeRoutines)
        {
            var name = routine.Key;
            Add(routine.Name, "Rutini çalıştır" + (routine.When.Length > 0 ? " · " + routine.When : ""), "\uE768", () => _actions.Send($"{name} rutinini çalıştır"));
        }

        foreach (var command in PaletteCommands)
        {
            var text = command;
            Add(command, "Komut", "\uE756", () =>
            {
                GoToChat();
                _actions.Send(text);
            });
        }

        if (query.Length > 0)
        {
            foreach (var chat in ChatList.OfType<ChatItemView>())
            {
                var id = chat.Id;
                Add(chat.Title, "Sohbet · " + chat.Meta, "\uE8BD", () =>
                {
                    GoToChat();
                    OpenChatCommand.Execute(id);
                });
            }

            foreach (var note in RecentNotes)
            {
                Add(note.Text, "Not · " + note.Time, "\uE70B", () => ShowPage(1));
            }

            foreach (var todo in Todos)
            {
                Add(todo.Text, todo.IsDone ? "Yapılacak · tamamlandı" : "Yapılacak", "\uE73E", () => ShowPage(1));
            }

            foreach (var memory in Memories)
            {
                Add(memory.Text, "Hafıza", "\uE8F1", () => ShowPage(4));
            }

            var ask = PaletteQuery.Trim();
            items.Add((1, new PaletteItem($"Friday'e sor: {ask}", "Enter ile gönder", "\uE8F2", () =>
            {
                GoToChat();
                _actions.Send(ask);
            })));
        }

        PaletteResults.Clear();
        foreach (var (_, item) in items.OrderByDescending(entry => entry.Score).Take(query.Length == 0 ? 12 : 9))
        {
            PaletteResults.Add(item);
        }

        SelectedPaletteItem = PaletteResults.Count > 0 ? PaletteResults[0] : null;
    }

    /// <summary>Boş sorguda sayfalar ve komutlar; başlık başta eşleşirse en üstte, içerirse ortada, açıklamada geçerse sonda.</summary>
    private static int Score(string query, string title, string detail)
    {
        if (query.Length == 0)
        {
            return detail.StartsWith("Sayfa", StringComparison.Ordinal) ? 5 : detail.StartsWith("Rutini", StringComparison.Ordinal) ? 4 : 3;
        }

        var folded = Fold(title);
        return folded.StartsWith(query, StringComparison.Ordinal) ? 100
            : folded.Contains(query, StringComparison.Ordinal) ? 60
            : query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => folded.Contains(word, StringComparison.Ordinal)) ? 40
            : Fold(detail).Contains(query, StringComparison.Ordinal) ? 10
            : 0;
    }

    private static string Fold(string text) => text.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"))
        .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
}
