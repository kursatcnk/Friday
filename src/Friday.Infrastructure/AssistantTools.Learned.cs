using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Öğrenilmiş bir şey: silmek için anahtarı, gösterilecek metni ve kısa açıklaması.</summary>
public sealed record LearnedEntry(string Key, string Text, string Detail);

/// <summary>Öğrenilenlerden bir grup (kısayollar, düzeltmeler, adlar…).</summary>
public sealed record LearnedGroup(string Key, string Title, string Hint, IReadOnlyList<LearnedEntry> Items);

/// <summary>
/// Friday'in kendi kendine öğrendikleri, tek yerde: listelenir ve tek tek unutulabilir. Panelin "Öğrendikleri" sayfası
/// bunları gösterir. Eskiden kısayollar, düzeltmeler, söz kalıpları ve üslup dosyalarda görünmeden duruyordu.
/// </summary>
public sealed partial class AssistantTools
{
    public IReadOnlyList<LearnedGroup> Learned(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var style = new List<LearnedEntry>();
        if (Style?.Current is { } verbosity && verbosity != Verbosity.Normal)
        {
            style.Add(new LearnedEntry("cevap", verbosity == Verbosity.Brief ? "Kısa cevap" : "Ayrıntılı cevap", "Sesli cevaplar bu uzunlukta"));
        }

        style.AddRange((WritingStyle?.Learned ?? []).Select(kind => new LearnedEntry("yazi:" + kind, $"Yazılarda {kind} üslup", "Yazma köprüsünün taslakları")));

        return
        [
            new LearnedGroup("kisayol", "Kısayollar", "Aynı isteği iki kez söyleyince dil modelini beklemeden yapılır.",
                [.. (Shortcuts?.Learned ?? []).Select(item => new LearnedEntry(item.Phrase, $"\"{item.Phrase}\"", ActivityName(item.Tool)))]),
            new LearnedGroup("kalip", "Söz kalıpları", "Modelin kendi araç çağrılarından çıkarılan kalıplar; boşluklara her şey gelebilir.",
                [.. (Shortcuts?.Templates.Learned ?? []).Select(item => new LearnedEntry(item.Pattern, Pattern(item.Pattern), ActivityName(item.Tool)))]),
            new LearnedGroup("duzeltme", "Düzeltmeler", "\"Yanlış anladın\" dediğin sözler; artık hızlı komut sanılmaz, dil modeline gider.",
                [.. (Corrections?.Phrases ?? []).Select(phrase => new LearnedEntry(phrase, $"\"{phrase}\"", ""))]),
            new LearnedGroup("ad", "Duyduğu adlar", "Konuşma tanımaya ipucu olarak verilen adlar; doğru duyulmalarını sağlar.",
                [.. Settings.HearingWords.Select(name => new LearnedEntry(name, name, ""))]),
            new LearnedGroup("okunus", "Okunuşlar", "Friday bu sözleri böyle okur.",
                [.. Settings.Pronunciations.Select(pair => new LearnedEntry(pair.Key, $"{pair.Key} → {pair.Value}", ""))]),
            new LearnedGroup("uslup", "Üslup", "Söz kesmelerinden ve düzeltmelerinden öğrendiği tercihler.", style),
            new LearnedGroup("susturulan", "Susturulan hatırlatmalar", "Art arda reddettiğin ya da \"bir daha sorma\" dediğin öneriler.",
                [.. (Nudges?.Silenced(now) ?? []).Select(item => new LearnedEntry(item.Kind, NudgePolicy.Label(item.Kind),
                    item.Until is { } until ? $"{until.ToString("d MMMM", Turkish)} tarihine kadar" : "Sen açana kadar"))]),
            new LearnedGroup("okunan", "Okuduğu belgeler", "Yarım dakikadan uzun baktığın belgeler; \"geçen hafta okuduğum…\" diye sorabilirsin.",
                [.. (Reading?.Entries ?? []).OrderByDescending(entry => entry.LastSeen).Take(40)
                    .Select(entry => new LearnedEntry(entry.Id, entry.Name, WhenRead(entry.LastSeen, today) + (entry.Gist.Length > 0 ? " · " + FirstSentence(entry.Gist) : "")))]),
        ];
    }

    /// <summary>Öğrenilmiş bir şeyi unutur; ne yapıldığını anlatan cümle.</summary>
    public string ForgetLearned(string group, string key)
    {
        switch (group)
        {
            case "kisayol" when Shortcuts?.Forget(key) == true:
                return $"\"{key}\" kısayolu unutuldu; bundan sonra dil modeli karar verir.";
            case "kalip" when Shortcuts?.Templates.Remove(key) == true:
                return "Söz kalıbı unutuldu.";
            case "duzeltme" when Corrections?.Remove(key) == true:
                return $"\"{key}\" yeniden hızlı komut olabilir.";
            case "ad":
                return HearingCommand("unut", key).Text;
            case "okunus" when Settings.Pronunciations.Remove(key):
                SaveSettings?.Invoke(Settings);
                return $"\"{key}\" yeniden olağan biçimde okunacak.";
            case "uslup" when key == "cevap" && Style is { } style:
                style.Prefer(Verbosity.Normal);
                return "Cevap uzunluğu tercihi sıfırlandı.";
            case "uslup" when key.StartsWith("yazi:", StringComparison.Ordinal) && WritingStyle?.Unlearn(key[5..]) == true:
                return $"Yazılardaki {key[5..]} üslup unutuldu.";
            case "susturulan" when Nudges?.Unsilence(key) == true:
                return $"{NudgePolicy.Label(key)} yeniden açıldı.";
            case "okunan" when Reading?.Entries.FirstOrDefault(entry => entry.Id == key) is { } entry && Reading.Forget(entry):
                return $"\"{entry.Name}\" okuduklarından silindi.";
            default:
                return "Bu zaten unutulmuş.";
        }
    }

    private static string ActivityName(string tool) => DescribeActivity(tool).TrimEnd('…', '.');

    /// <summary>"{metin} {zaman} bana bildir" → "… … bana bildir".</summary>
    private static string Pattern(string pattern) =>
        System.Text.RegularExpressions.Regex.Replace(pattern, @"\{[^}]+\}", "…");
}
