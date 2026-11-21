using System.Net;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Seçili metin işlemleri, yazdırma, pencere düzeni ve haber başlıkları.</summary>
public sealed partial class AssistantTools
{
    private const byte WinKey = 0x5B;
    private const byte Left = 0x25;
    private const byte Right = 0x27;
    private static readonly TimeSpan NewsCacheLife = TimeSpan.FromMinutes(10);

    private static readonly Dictionary<string, (string Label, string[] Feeds)> NewsFeeds = new()
    {
        ["gundem"] = ("Gündem", ["https://www.trthaber.com/manset_articles.rss", "https://www.hurriyet.com.tr/rss/gundem", "https://feeds.bbci.co.uk/turkce/rss.xml"]),
        ["spor"] = ("Spor", ["https://www.trthaber.com/spor_articles.rss", "https://www.hurriyet.com.tr/rss/spor"]),
        ["ekonomi"] = ("Ekonomi", ["https://www.trthaber.com/ekonomi_articles.rss", "https://www.hurriyet.com.tr/rss/ekonomi"]),
        ["teknoloji"] = ("Teknoloji", ["https://www.trthaber.com/bilim_teknoloji_articles.rss", "https://www.hurriyet.com.tr/rss/teknoloji"]),
        ["dunya"] = ("Dünya", ["https://www.trthaber.com/dunya_articles.rss", "https://www.hurriyet.com.tr/rss/dunya", "https://feeds.bbci.co.uk/turkce/rss.xml"]),
    };

    private readonly Dictionary<string, (DateTime At, List<string> Titles)> _news = [];

    /// <summary>Kısayola basıldığı ya da "Hey Friday" denildiği andaki pencere; seçili metin ve pencere komutları buna uygulanır.</summary>
    public OpenWindow? ActiveWindow { get; set; }

    /// <summary>
    /// "Seçili metni düzelt / İngilizceye çevir / resmileştir / özetle": seçimi Ctrl+C ile alır, yerel modelde işler.
    /// Düzeltme, çeviri ve yeniden yazmada sonuç seçimin yerine yapıştırılır; özet ve açıklama okunur. Pano eski haline döner.
    /// </summary>
    private async Task<QuickReply> SelectionAsync(string operation, string language, CancellationToken cancellationToken)
    {
        var previous = ClipboardText.Get();
        var text = await CopySelectionAsync(cancellationToken).ConfigureAwait(false);
        if (text.Length == 0)
        {
            Restore(previous);
            return new QuickReply("Seçili bir metin bulamadım. Önce metni seç, sonra söyle.");
        }

        if (text.Length > MaxClipboardCharacters)
        {
            text = text[..MaxClipboardCharacters];
        }

        var key = Simplify(operation);
        if (ClipboardInstruction(key, language) is not { } instruction)
        {
            Restore(previous);
            return new QuickReply($"Bilinmeyen işlem: {operation}");
        }

        ledger.Record(DataFlow.Local, "Seçili metin", $"{text.Length} karakter alındı, yerel modelde işlendi");
        var result = (await chat.CompleteAsync(Settings.OllamaModel, instruction, text, null, null, cancellationToken).ConfigureAwait(false)).Trim();
        if (key is "ozetle" or "acikla")
        {
            Restore(previous);
            return new QuickReply(result);
        }

        // Sonuç seçimin yerine yapıştırılır; uygulama yapıştırmayı okuduktan sonra eski pano geri konur.
        ClipboardText.Set(result);
        if (ActiveWindow is { } window)
        {
            Desktop.BringToFront(window.Handle);
        }

        Keys.Chord(Keys.Control, 0x56);
        await Task.Delay(700, CancellationToken.None).ConfigureAwait(false);
        Restore(previous);
        return new QuickReply(key switch
        {
            "duzelt" => "Düzelttim.",
            "cevir" => "Çevirdim.",
            "resmilestir" => "Daha resmi hale getirdim.",
            "maddele" => "Maddeledim.",
            _ => "Kısalttım."
        });
    }

    /// <summary>Etkin penceredeki seçimi panoya kopyalar ve okur; seçim yoksa boş döner.</summary>
    private async Task<string> CopySelectionAsync(CancellationToken cancellationToken)
    {
        ClipboardText.Set("");
        if (ActiveWindow is { } window)
        {
            Desktop.BringToFront(window.Handle);
        }

        Keys.Chord(Keys.Control, 0x43);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(60, cancellationToken).ConfigureAwait(false);
            var text = ClipboardText.Get();
            if (text.Trim().Length > 0)
            {
                return text;
            }
        }

        return "";
    }

    private static void Restore(string previous)
    {
        if (previous.Length > 0)
        {
            ClipboardText.Set(previous);
        }
    }

    /// <summary>"Şunu yaz: …": metni etkin pencereye harf harf yazar.</summary>
    private QuickReply TypeText(string text)
    {
        if (ActiveWindow is { } window)
        {
            Desktop.BringToFront(window.Handle);
        }

        Keys.Text(text);
        ledger.Record(DataFlow.Local, "Yazdırma", $"{text.Length} karakter {ActiveWindow?.Process ?? "etkin pencere"} penceresine yazıldı");
        return new QuickReply("Yazdım.", Silent: true);
    }

    /// <summary>Dikte: söylenen bir parçayı ya da "yeni satır" gibi bir tuşu etkin pencereye gönderir.</summary>
    public static void Dictate(string text, OpenWindow? target)
    {
        if (target is not null)
        {
            Desktop.BringToFront(target.Handle);
        }

        switch (QuickIntents.DictationKey(text))
        {
            case "enter":
                Keys.Tap(0x0D);
                break;
            case "geri_al":
                Keys.Chord(Keys.Control, 0x5A);
                break;
            case "kelime_sil":
                Keys.Chord(Keys.Control, 0x08);
                break;
            default:
                // Dolgu sesleri atılır, söylenen noktalama yazıya çevrilir (bkz. DictationText).
                Keys.Text(DictationText.Clean(text) + " ");
                break;
        }
    }

    /// <summary>"Pencereyi sola yasla", "tam ekran yap", "ekranı ikiye böl": Windows'un kendi yerleştirmesiyle.</summary>
    private QuickReply WindowCommand(string action)
    {
        var target = ActiveWindow ?? Desktop.Foreground();
        if (target is null)
        {
            return new QuickReply("Hangi pencere olduğunu bulamadım.");
        }

        switch (Simplify(action))
        {
            case "sol":
            case "sag":
                Desktop.BringToFront(target.Handle);
                Keys.Chord(WinKey, Simplify(action) == "sol" ? Left : Right);
                return new QuickReply($"{target.Process} {(Simplify(action) == "sol" ? "sola" : "sağa")} yaslandı.", Silent: true);
            case "buyut":
                NativeMethods.ShowWindow(target.Handle, 3);
                return new QuickReply($"{target.Process} tam ekran.", Silent: true);
            case "kucult":
                NativeMethods.ShowWindow(target.Handle, 6);
                return new QuickReply($"{target.Process} küçültüldü.", Silent: true);
            default:
                var other = Desktop.Windows().FirstOrDefault(window => window.Handle != target.Handle);
                Desktop.BringToFront(target.Handle);
                Keys.Chord(WinKey, Left);
                if (other is null)
                {
                    return new QuickReply("Yanına koyacak ikinci bir pencere yok; bunu sola yasladım.");
                }

                Thread.Sleep(350);
                Desktop.BringToFront(other.Handle);
                Keys.Chord(WinKey, Right);
                return new QuickReply($"{target.Process} solda, {other.Process} sağda.");
        }
    }

    /// <summary>"Gündem ne", "spor haberleri": haber kaynağının RSS akışından son başlıklar (10 dk saklanır).</summary>
    private async Task<QuickReply> NewsAsync(string category, CancellationToken cancellationToken)
    {
        var key = NewsFeeds.ContainsKey(Simplify(category)) ? Simplify(category) : "gundem";
        var (label, feeds) = NewsFeeds[key];
        if (!_news.TryGetValue(key, out var cached) || DateTime.Now - cached.At > NewsCacheLife)
        {
            List<string> titles = [];
            foreach (var feed in feeds)
            {
                titles = await FetchHeadlinesAsync(feed, cancellationToken).ConfigureAwait(false);
                if (titles.Count > 0)
                {
                    ledger.Record(DataFlow.Outbound, "Haberler", $"{new Uri(feed).Host} · {label.ToLower(Turkish)} başlıkları alındı");
                    break;
                }
            }

            cached = (DateTime.Now, titles);
            _news[key] = cached;
        }

        if (cached.Titles.Count == 0)
        {
            return new QuickReply("Haber kaynağına şu an ulaşamadım.");
        }

        var top = cached.Titles.Take(5).ToList();
        ShowListCard($"{label} başlıkları", "", top.Select(title => new CardRow(title, "")));
        return new QuickReply($"{label} başlıkları: " + string.Join(" ", top.Select(EndSentence)));
    }

    private async Task<List<string>> FetchHeadlinesAsync(string feed, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var request = new HttpRequestMessage(HttpMethod.Get, feed);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Friday)");
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            return ParseHeadlines(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return [];
        }
    }

    /// <summary>Başlığı okunacak bir cümleye çevirir: soru ya da ünlemle bitiyorsa nokta eklenmez.</summary>
    public static string EndSentence(string title)
    {
        var clean = title.Trim().TrimEnd('.', ' ');
        return clean.EndsWith('?') || clean.EndsWith('!') ? clean : clean + ".";
    }

    /// <summary>RSS (item) ya da Atom (entry) akışındaki başlıklar, sırasıyla.</summary>
    public static List<string> ParseHeadlines(string xml)
    {
        var document = XDocument.Parse(xml);
        return [.. document.Descendants()
            .Where(element => element.Name.LocalName is "item" or "entry")
            .Select(element => element.Elements().FirstOrDefault(child => child.Name.LocalName == "title")?.Value)
            .OfType<string>()
            .Select(title => WebUtility.HtmlDecode(title).Trim())
            .Where(title => title.Length > 0)
            .Distinct()];
    }

    /// <summary>Pano ve seçili metin işlemlerinin dil modeli talimatı; bilinmeyen işlemde null.</summary>
    private static string? ClipboardInstruction(string operation, string language)
    {
        var target = string.IsNullOrWhiteSpace(language) ? "İngilizce" : language.Trim();
        return operation switch
        {
            "ozetle" => "Aşağıdaki metni metnin kendi dilinde kısa ve öz özetle. Yalnızca özeti yaz.",
            "cevir" => $"Aşağıdaki metni {target} diline çevir. Biçimi koru, yalnızca çeviriyi yaz.",
            "duzelt" => "Aşağıdaki metindeki yazım, noktalama ve dil bilgisi hatalarını düzelt. Anlamı, dili ve biçimi koru. Yalnızca düzeltilmiş metni yaz.",
            "resmilestir" => "Aşağıdaki metni aynı dilde, kibar ve profesyonel bir üslupla yeniden yaz (ör. iş e-postası). Yalnızca yeni metni yaz.",
            "maddele" => "Aşağıdaki metni aynı dilde, kısa madde işaretli bir listeye dönüştür. Yalnızca listeyi yaz.",
            "acikla" => "Aşağıdaki metni (kod, hata mesajı ya da yazı olabilir) Türkçe, sade ve kısa bir şekilde açıkla.",
            "kisalt" => "Aşağıdaki metni aynı dilde, anlamını koruyarak yarı uzunluğa kısalt. Yalnızca yeni metni yaz.",
            _ => null
        };
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ShowWindow(IntPtr window, int command);
    }
}
