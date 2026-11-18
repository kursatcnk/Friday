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
    private static readonly int[] SampleWeek = [310, 265, 0, 340, 288, 120, 251];

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

        // Görsel kartlı cevaplar.
        foreach (var (name, question, reply, card) in new (string, string, string, AnswerCard)[]
                 {
                     ("hud-card-weather", "Hava nasıl?", "İstanbul'da şu an 15 derece, hava parçalı bulutlu. Gün içinde en yüksek 17 derece; yağmur beklenmiyor.",
                         new AnswerCard("İstanbul", "")
                         {
                             Big = "15°",
                             BigCaption = "parçalı bulutlu",
                             Rows = [new CardRow("En yüksek / en düşük", "17° / 9°"), new CardRow("Yağış ihtimali", "%20"), new CardRow("Hissedilen", "13°")]
                         }),
                     ("hud-card-status", "Durum raporu", "Durum raporu, patron. İşlemci yüzde 13, bellek yüzde 49, ekran kartı 54 derece. Her şey yolunda.",
                         new AnswerCard("Durum raporu", "")
                         {
                             Gauges =
                             [
                                 new CardGauge("İşlemci", 0.13, "%13"), new CardGauge("Bellek", 0.49, "%49"), new CardGauge("Ekran kartı", 0.54, "54°"),
                                 new CardGauge("C: diski", 0.26, "693 GB boş"), new CardGauge("D: diski", 0.54, "853 GB boş")
                             ]
                         }),
                 })
        {
            hud.BeginTurn();
            hud.State = AssistantState.Speaking;
            hud.Level = 0.4;
            hud.UserText = question;
            hud.SetReply(reply);
            hud.Card = card;
            var view = new HudView { DataContext = hud };
            view.FreezeAnimation(1.1);
            var desktop = new Grid
            {
                Width = 900,
                Height = 560,
                Background = new LinearGradientBrush(Color.FromRgb(0x2B, 0x3A, 0x55), Color.FromRgb(0x7A, 0x5C, 0x86), 20)
            };
            desktop.Children.Add(new Border { Child = view, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom });
            Save(desktop, Path.Combine(folder, $"{name}.png"));
        }

        var settings = new FridaySettings { UserName = "Kürşat", AddressAs = "patron" };
        settings.Triggers["baha"] = ["soyle:Baha abi geldiyse çayı tazeleyelim."];
        var now = DateTime.Now;
        var chatFolder = Path.Combine(folder, ".snapshot-chats");
        if (Directory.Exists(chatFolder))
        {
            Directory.Delete(chatFolder, recursive: true);
        }

        var chats = SampleChats(chatFolder, now);
        var actions = new PanelActions
        {
            Chats = chats,
            PiperVoices = [PanelViewModel.PiperVoiceOption(@"D:\Friday\models\piper\tr_TR-dfki-medium.onnx"),
                PanelViewModel.PiperVoiceOption(@"D:\Friday\models\piper\tr_TR-99eren99.onnx")]
        };
        var working = new RhythmSnapshot(true, now.AddMinutes(-34), null, null, null, TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5), false, null);
        var home = new PanelViewModel(settings, actions)
        {
            State = AssistantState.Idle,
            StatusText = "Hazır · \"Hey Friday\" de ya da Ctrl+Alt+F"
        };
        home.ShowWeather(new WeatherCard("İstanbul", 15, "parçalı bulutlu", 17, 9, 20));
        home.ShowToday(
            [
                new Reminder("r1", now.Date.AddHours(20), "ilacımı", now),
                new Reminder("r2", now.Date.AddDays(1).AddHours(9), "doktoru aramayı", now),
            ],
            [new MemoryItem("m1", $"Kullanıcının arabasının muayenesi {now.AddDays(3).ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))}'da.", DateTimeOffset.Now)]);
        home.ShowNotes([new Note(now.AddHours(-3), "Süt ve ekmek al"), new Note(now.AddHours(-1), "Sunumun ilk slaytını sadeleştir")]);
        home.ShowRhythm(new RhythmDay { Date = DateOnly.FromDateTime(now), ActiveMinutes = 186, FocusMinutes = 50, Breaks = 2 }, working);
        RenderPanel(home, folder, "panel-today");

        var intro = new PanelViewModel(new FridaySettings(), new PanelActions()) { State = AssistantState.Idle, StatusText = "Hazır" };
        RenderPanel(intro, folder, "panel-today-intro");

        var empty = new PanelViewModel(settings, actions)
        {
            State = AssistantState.Idle,
            StatusText = "Hazır · Whisper GPU (Vulkan) · Azure nöral ses (Emel)",
            IsToday = false,
            IsChat = true
        };
        chats.NewChat();
        empty.ShowChat([]);
        RenderPanel(empty, folder, "panel-chat-empty");

        var panel = new PanelViewModel(settings, actions)
        {
            State = AssistantState.Speaking,
            StatusText = "Konuşuyor…",
            Level = 0.4,
            EngineLabel = "Azure nöral ses (Emel)"
        };
        var current = chats.List().First(chat => chat.Title.StartsWith("Panodaki", StringComparison.Ordinal));
        chats.Open(current.Id);
        panel.ShowChat(chats.CurrentTurns);
        panel.IsToday = false;
        panel.IsChat = true;
        RenderPanel(panel, folder, "panel-chat");

        panel.ChatSearch = "muayene";
        panel.RefreshChats();
        RenderPanel(panel, folder, "panel-chat-search");
        panel.ChatSearch = "";
        panel.RefreshChats();

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
        var focus = new RhythmSnapshot(true, now.AddMinutes(-41), now.AddMinutes(-13), now.AddMinutes(12), null,
            TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5), false, null);
        var rhythmDay = new RhythmDay
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
        };
        panel.ShowRhythm(rhythmDay, focus);
        panel.RhythmNow = day.AddHours(14.4);
        panel.ShowWeek([.. Enumerable.Range(0, 7).Select(offset => new RhythmDay
        {
            Date = DateOnly.FromDateTime(day.AddDays(offset - 6)),
            ActiveMinutes = SampleWeek[offset]
        })]);
        RenderPanel(panel, folder, "panel-rhythm");
        panel.TickRhythm(working);
        RenderPanel(panel, folder, "panel-rhythm-working");
        panel.TickRhythm(new RhythmSnapshot(true, null, null, null, now.AddMinutes(-2).AddSeconds(-14), TimeSpan.FromMinutes(50),
            TimeSpan.FromMinutes(5), false, null));
        RenderPanel(panel, folder, "panel-rhythm-break");
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
        panel.UseTemplateCommand.Execute(RoutineStep.Templates[1]);
        RenderPanel(panel, folder, "panel-routines-edit");
        panel.CancelRoutineCommand.Execute(null);

        panel.IsRoutines = false;
        panel.IsSettings = true;
        RenderPanel(panel, folder, "panel-settings");
        Directory.Delete(chatFolder, recursive: true);
    }

    /// <summary>Sohbet listesi için örnek sohbetler (bugün, dün, bu hafta ve sabitlenmiş bir tane).</summary>
    private static ChatStore SampleChats(string folder, DateTime now)
    {
        var clock = new FixedClock(now);
        var chats = new ChatStore(folder, clock);
        void Chat(double hoursAgo, params (string User, string Assistant)[] turns)
        {
            clock.Now = now.AddHours(-hoursAgo);
            chats.NewChat();
            foreach (var (user, assistant) in turns)
            {
                chats.Append(user, assistant);
            }
        }

        Chat(26 * 4, ("Arabamın muayenesi ne zaman?", "Arabanın muayenesi 20 Kasım'da; üç gün kala sabah brifinginde hatırlatırım."));
        Chat(50, ("Bu hafta sonu İzmir'de hava nasıl?", "Cumartesi güneşli ve 22 derece, pazar akşamüstü hafif yağmur bekleniyor."));
        Chat(27, ("Merhaba", "Merhaba Kürşat, nasıl gidiyor?"), ("Sunum için üç başlık önerir misin?", "Sorun, çözüm ve yol haritası. İlk slaytta tek bir cümleyle sorunu söyle."));
        Chat(5, ("Unutma, arabamın muayenesi 20 Kasım'da.", "Not ettim, 20 Kasım yaklaşınca sabah brifinginde hatırlatırım."));
        Chat(2.2, ("Dolar kaç lira?", "Dolar 41 lira 82 kuruş, euro 48 lira 15 kuruş. Merkez Bankası'nın bugünkü kurları."));
        Chat(0.4,
            ("Panodaki metnin yazım hatalarını düzeltir misin?", "Düzelttim ve panoya koydum, Ctrl+V ile yapıştırabilirsin. \"Kalcakmış\" yerine \"kalacakmış\", \"cunku\" yerine \"çünkü\" yazdım."),
            ("Bilgisayar neden yavaş?", "İşlemci yüzde 18'de, asıl yükü Chrome çekiyor: 4,2 GB bellek. Kullanmadığın sekmeleri kapatırsan rahatlar."),
            ("Peki hangi sekmeler?", "En çok YouTube (1,1 GB) ve iki Figma sekmesi (1,6 GB) yiyor. YouTube'u kapatman yeter."));
        if (chats.List().FirstOrDefault(chat => chat.Title.StartsWith("Bu hafta sonu", StringComparison.Ordinal)) is { } pinned)
        {
            chats.SetPinned(pinned.Id, true);
        }

        clock.Now = now;
        return chats;
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now).ToUniversalTime();
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
