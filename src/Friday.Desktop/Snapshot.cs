using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Friday.Core;
using Friday.Infrastructure;

namespace Friday.Desktop;

/// <summary>
/// "Friday.exe --snapshot klasör": arayüzü örnek verilerle PNG olarak çizer. Tasarımı uygulamayı çalıştırmadan
/// gözden geçirmek ve değişiklikleri karşılaştırmak için.
/// </summary>
internal static class Snapshot
{
    public static void RenderAll(string folder)
    {
        Directory.CreateDirectory(folder);
        var hud = new HudViewModel();
        foreach (var (state, level, user, caption, tool) in new (AssistantState, double, string, string, string?)[]
                 {
                     (AssistantState.Listening, 0.55, "", "", null),
                     (AssistantState.Thinking, 0, "Yarın Ankara'da hava nasıl olacak?", "", "hava_durumu"),
                     (AssistantState.Speaking, 0.42, "Yarın Ankara'da hava nasıl olacak?",
                         "Yarın Ankara'da parçalı bulutlu, en düşük 3, en yüksek 16 derece. Yağmur beklenmiyor, ama sabah serin olacak.", null),
                 })
        {
            hud.BeginTurn();
            hud.State = state;
            hud.Level = level;
            hud.UserText = user;
            hud.SetReply(caption);
            if (tool is not null)
            {
                hud.SetTool(tool);
            }

            var view = new HudView { DataContext = hud };
            view.FreezeAnimation(state == AssistantState.Thinking ? 0.3 : 1.1);
            var desktop = new Grid
            {
                Width = 900,
                Height = 260,
                Background = new LinearGradientBrush(Color.FromRgb(0x2B, 0x3A, 0x55), Color.FromRgb(0x7A, 0x5C, 0x86), 20)
            };
            desktop.Children.Add(new Border { Child = view, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom });
            Save(desktop, Path.Combine(folder, $"hud-{state.ToString().ToLowerInvariant()}.png"));
        }

        var settings = new FridaySettings { UserName = "Kürşat" };
        var empty = new PanelViewModel(settings, new PanelActions())
        {
            State = AssistantState.Idle,
            StatusText = "Hazır · Whisper GPU (Vulkan) · Azure nöral ses (Emel)"
        };
        RenderPanel(empty, folder, "panel-chat-empty");

        var panel = new PanelViewModel(settings, new PanelActions())
        {
            State = AssistantState.Speaking,
            StatusText = "Konuşuyor…",
            Level = 0.4,
            EngineLabel = "Azure nöral ses (Emel)"
        };
        panel.OnUserSaid("Panodaki metnin yazım hatalarını düzeltir misin?");
        panel.OnAssistantSaid("Düzelttim ve panoya koydum, Ctrl+V ile yapıştırabilirsin. \"Kalcakmış\" yerine \"kalacakmış\", \"cunku\" yerine \"çünkü\" yazdım.");
        panel.OnUserSaid("Unutma, arabamın muayenesi 20 Kasım'da.");
        panel.OnAssistantSaid("Not ettim, 20 Kasım yaklaşınca sabah brifinginde hatırlatırım.");
        panel.OnUserSaid("Bilgisayar neden yavaş?");
        panel.OnAssistantSaid("İşlemci yüzde 18'de, asıl yükü Chrome çekiyor: 4,2 GB bellek. Kullanmadığın sekmeleri kapatırsan rahatlar.");
        RenderPanel(panel, folder, "panel-chat");

        panel.IsChat = false;
        panel.IsMemory = true;
        var soon = DateTime.Now.AddDays(3).ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        panel.ShowMemories(
        [
            new MemoryItem("a1", $"Kullanıcının arabasının muayenesi {soon}'da.", DateTimeOffset.Now.AddDays(-2)),
            new MemoryItem("a2", "Kullanıcı kahveyi şekersiz içer.", DateTimeOffset.Now.AddDays(-9)),
            new MemoryItem("a3", "Kullanıcının kızının doğum günü 3 Mart.", DateTimeOffset.Now.AddDays(-30)),
            new MemoryItem("a4", "Is building a local voice assistant in C# and WPF.", DateTimeOffset.Now.AddDays(-1), "ChatGPT"),
            new MemoryItem("a5", "Prefers short, direct answers without filler.", DateTimeOffset.Now.AddDays(-1), "ChatGPT"),
        ]);
        RenderPanel(panel, folder, "panel-memory");

        panel.PreviewImport("""
            Here is what I remember about you:
            - Is a software developer living in Istanbul.
            - Prefers concise answers.
            - Has a daughter whose birthday is on March 3.
            - Rides a bike on weekends.
            """, "chatgpt-memories.txt");
        RenderPanel(panel, folder, "panel-memory-import");
        panel.CancelImportCommand.Execute(null);

        panel.IsMemory = false;
        panel.IsRhythm = true;
        var day = DateTime.Today;
        panel.ShowRhythm(new RhythmDay
        {
            Date = DateOnly.FromDateTime(day),
            ActiveMinutes = 251,
            FocusMinutes = 75,
            Breaks = 3,
            LongestStretchMinutes = 82,
            Water = 4,
            Segments =
            [
                new RhythmSegment(day.AddHours(8.6), day.AddHours(9.9), RhythmSegmentKind.Work),
                new RhythmSegment(day.AddHours(10.05), day.AddHours(10.9), RhythmSegmentKind.Focus),
                new RhythmSegment(day.AddHours(10.95), day.AddHours(12.3), RhythmSegmentKind.Work),
                new RhythmSegment(day.AddHours(13.2), day.AddHours(13.6), RhythmSegmentKind.Focus),
                new RhythmSegment(day.AddHours(13.6), day.AddHours(14.4), RhythmSegmentKind.Work),
            ]
        }, TimeSpan.FromMinutes(34), DateTime.Now.AddMinutes(12), 50);
        panel.RhythmNow = day.AddHours(14.4);
        RenderPanel(panel, folder, "panel-rhythm");
        panel.IsRhythm = false;
        panel.IsMemory = true;

        panel.IsMemory = false;
        panel.IsPrivacy = true;
        var today = DateTimeOffset.Now.Date;
        panel.ShowLedger(
        [
            new LedgerEntry(today.AddHours(9).AddMinutes(12), DataFlow.Local, "Mikrofon", "3,4 sn açık kaldı · ses bellekte işlendi, kaydedilmedi"),
            new LedgerEntry(today.AddHours(9).AddMinutes(12), DataFlow.Outbound, "Hava durumu", "open-meteo.com · şehir: İstanbul"),
            new LedgerEntry(today.AddHours(9).AddMinutes(13), DataFlow.Outbound, "Ses (Azure)", "Cevabın 182 karakterlik metni nöral sese çevrilmek üzere Microsoft'a gönderildi · ses kaydı gitmedi"),
            new LedgerEntry(today.AddHours(11).AddMinutes(2), DataFlow.Local, "Ekran görüntüsü", "Visual Studio · bellekte işlendi, kaydedilmedi, yerel modele verildi"),
            new LedgerEntry(today.AddHours(11).AddMinutes(30), DataFlow.Local, "Pano", "110 karakter okundu, yerel modelde işlendi"),
        ]);
        RenderPanel(panel, folder, "panel-privacy");

        panel.IsPrivacy = false;
        panel.IsRoutines = true;
        RenderPanel(panel, folder, "panel-routines");

        panel.IsRoutines = false;
        panel.IsSettings = true;
        RenderPanel(panel, folder, "panel-settings");
    }

    private static void RenderPanel(PanelViewModel viewModel, string folder, string name)
    {
        var view = new PanelView { DataContext = viewModel, Width = 1020, Height = 660 };
        view.FreezeAnimation(1.1);
        Save(view, Path.Combine(folder, name + ".png"));
    }

    private static void Save(FrameworkElement element, string file)
    {
        var size = new Size(double.IsNaN(element.Width) ? 900 : element.Width, double.IsNaN(element.Height) ? 260 : element.Height);
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        // Bağlamaların ve şablonların uygulanması için bir düzen turu daha.
        element.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
