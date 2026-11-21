using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Friday kendini açıklar: "bunu neden söyledin?" kendiliğinden söylenen son şeyin gerekçesini, "neler öğrendin?" bütün
/// öğrenen parçaların (kulak, kısayollar, düzeltmeler, üslup, günlük ritim, hafıza) özetini verir.
/// </summary>
public sealed partial class AssistantTools
{
    private QuickReply WhyCommand()
    {
        var now = DateTime.Now;
        if (LastNudge is not { } kind || now - _lastNudgeAt > TimeSpan.FromHours(2))
        {
            return new QuickReply("Son iki saatte kendiliğimden bir şey söylemedim; söylediklerim senin isteğine cevaptı.");
        }

        var end = Clock(DayEnd(now));
        var reason = kind switch
        {
            "seri" => $"Alışkanlık serin bozulmasın diye. Akşamları, günün genelde bittiği saatten ({end}) önce, o gün işaretlenmemiş ve en az iki günlük serisi olan alışkanlıkları bir kez hatırlatırım.",
            "gun_sonu" => $"Gün sonu sorusu: günün genelde bittiği {end} saatinden önceki bir buçuk saatte, o gün günlüğe bir şey yazılmadıysa bir kez sorarım.",
            "son_tarih" => "Yarın teslim olan işlerin var; akşamları, günün bitmesinden önce bir kez hatırlatırım ki sabaha sürpriz olmasın.",
            "arife" => "Yarın hafızandaki birinin özel günü; hazırlanabilmen için akşamdan söyledim.",
            "uyku" => "Uyku saatini sen ayarladın; yarım saat önce ekranları bırakmayı hatırlatırım.",
            "sinir" => "Bu uygulamaya günlük süre sınırı koymuştun; sınır aşılınca söylerim.",
            "goz" => "20-20-20 kuralı: 20 dakika aralıksız ekrana bakınca 20 saniye uzağa bakmayı hatırlatırım; odaktayken susarım.",
            "hava" => "Yarın için yağış ya da sert hava görünüyor; hazırlıklı ol diye akşamdan haber verdim.",
            "rutin_onerisi" => "Bunu sık sık aynı saatlerde yaptığını fark ettim; tek cümleyle başlatabilmen için rutine çevirmeyi önerdim.",
            "gorusme_notu" => "Uzun bir görüşmeden çıktın; konuşulanları unutmadan not almak isteyebilirsin diye sordum.",
            "kayip" => "Sen bilgisayarın başında değilken bir şey söylemiştim; kaçırmayasın diye döndüğünde tekrarladım.",
            _ => "Kendiliğimden söylediğim bir hatırlatmaydı."
        };
        var label = NudgePolicy.Labels.ContainsKey(kind) ? " İstemezsen \"bunu bir daha sorma\" de, o türü kapatırım." : "";
        return new QuickReply(reason + label);
    }

    private QuickReply LearnedReport()
    {
        var now = DateTime.Now;
        var parts = new List<string>();
        var taught = Settings.HearingWords.Count;
        var heard = HearingTerms().Count;
        if (heard > 0)
        {
            parts.Add($"kulağım {heard} özel ada alıştı{(taught > 0 ? $", {taught} tanesini sen öğrettin" : "")}");
        }

        if (Shortcuts?.Learned is { Count: > 0 } shortcuts)
        {
            parts.Add($"{shortcuts.Count} sözü düşünmeden yapmayı öğrendim");
        }

        if (Corrections?.Phrases is { Count: > 0 } corrections)
        {
            parts.Add($"{corrections.Count} sözün komut olmadığını öğrendim");
        }

        if (Style is { } style && style.Current != Verbosity.Normal)
        {
            parts.Add(style.Current == Verbosity.Brief ? "kısa cevapları sevdiğini öğrendim" : "ayrıntılı cevapları sevdiğini öğrendim");
        }

        if (Tracker?.TypicalDay(now) is { } day)
        {
            parts.Add($"günün genelde {Clock(day.Start)} civarı başlayıp {Clock(day.End)} civarı bittiğini öğrendim");
        }

        var newFacts = memory.Items.Count(item => now - item.Created.LocalDateTime < TimeSpan.FromDays(7));
        if (newFacts > 0)
        {
            parts.Add($"bu hafta hafızama {newFacts} yeni bilgi yazdım");
        }

        if (Nudges?.Silenced(now) is { Count: > 0 } silenced)
        {
            parts.Add($"{silenced.Count} tür hatırlatmayı istemediğini öğrendim");
        }

        return new QuickReply(parts.Count == 0
            ? "Henüz kayda değer bir şey öğrenmedim. Benimle konuştukça adlarını, alışkanlıklarını, nasıl konuşmamı istediğini öğrenirim."
            : TurkishGrammar.Capitalize(TurkishGrammar.Join(parts)) + ".");
    }
}
