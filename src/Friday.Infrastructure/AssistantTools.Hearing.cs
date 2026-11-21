using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Friday'in kulağı: Whisper ipucu için kullanıcının dünyasındaki adlar (bkz. <see cref="HearingVocabulary"/>).</summary>
public sealed partial class AssistantTools
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Kullanıcı bir ad öğrettiğinde ya da unutturduğunda; ipucu hemen yenilenir.</summary>
    public event EventHandler? HearingChanged;

    /// <summary>
    /// Whisper ipucu: öğretilen adlar, kullanıcının adı, şehir ve sık kullanılan uygulamalar önce; sonra hafızada,
    /// işlerde, hatırlatıcılarda, notlarda ve kısaltmalarda en çok geçen özel adlar.
    /// </summary>
    public string HearingPrompt() => HearingVocabulary.Prompt(Settings.AssistantName, HearingTerms());

    private List<string> HearingTerms()
    {
        var now = DateTime.Now;
        var fixedTerms = new List<string>(Settings.HearingWords) { Settings.UserName, Settings.City };
        if (Tracker is { } tracker)
        {
            fixedTerms.AddRange(Enumerable.Range(0, 7)
                .SelectMany(back => tracker.Day(now.AddDays(-back)))
                .GroupBy(app => app.App, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Sum(app => app.Seconds))
                .Take(6)
                .Select(group => group.Key));
        }

        var texts = memory.Items.TakeLast(600).Select(item => item.Text)
            .Concat(Todos?.Open.Select(item => item.Text) ?? [])
            .Concat(reminders.Upcoming.Select(item => item.Text))
            .Concat(notes.All.TakeLast(150).Select(note => note.Text))
            .Concat(Settings.Snippets.Values);
        return [.. HearingVocabulary.Collect(fixedTerms, texts)];
    }

    private QuickReply HearingCommand(string action, string word)
    {
        var name = string.Join(' ', word.Trim().Trim('"', '\'', '.', ',').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length > 0 ? char.ToUpper(part[0], TurkishCulture) + part[1..] : part));
        switch (action)
        {
            case "ogren" when name.Length > 1:
                if (!Settings.HearingWords.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    Settings.HearingWords.Add(name);
                    SaveSettings?.Invoke(Settings);
                    HearingChanged?.Invoke(this, EventArgs.Empty);
                }

                return new QuickReply($"Tamam, \"{name}\" adına kulak verdim; bundan sonra doğru duymaya çalışacağım.");
            case "unut" when name.Length > 1:
                var removed = Settings.HearingWords.RemoveAll(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    SaveSettings?.Invoke(Settings);
                    HearingChanged?.Invoke(this, EventArgs.Empty);
                    return new QuickReply($"\"{name}\" adını öğrettiklerinden çıkardım.");
                }

                return new QuickReply($"\"{name}\" adını bana sen öğretmemiştin; hafızanda ya da işlerinde geçtiği için tanıyorum. Oralardan silinirse unuturum.");
            default:
                var terms = HearingTerms();
                return new QuickReply(terms.Count == 0
                    ? "Henüz özel bir ad öğrenmedim. Bana kişilerden ve yerlerden bahsettikçe ya da \"X adını öğren\" dedikçe kulağım alışır."
                    : $"Kulağım şu adlara alışık: {string.Join(", ", terms.Take(12))}{(terms.Count > 12 ? $" ve {terms.Count - 12} ad daha" : "")}. Yanlış duyduğum bir ad olursa \"X adını öğren\" de.");
        }
    }
}
