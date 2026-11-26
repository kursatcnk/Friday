using System.Globalization;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Uygulama bazında ses.</summary>
public sealed partial class AssistantTools
{
    // ---------- Uygulama sesi ----------

    private QuickReply AppVolumeCommand(string action, string name, string levelText)
    {
        var simple = Simplify(action);
        if (simple == "listele")
        {
            var apps = AppAudio.Apps();
            if (apps.Count == 0)
            {
                return new QuickReply("Şu an ses çıkaran bir uygulama yok.");
            }

            ShowListCard("Uygulama sesleri", "", apps.Take(MaxCardRows).Select(app => new CardRow(app.Name, AudioLabel(app))));
            return new QuickReply("Ses karıştırıcıdaki uygulamalar: " + string.Join(", ", apps.Take(5).Select(app => $"{app.Name} {AudioLabel(app).ToLower(Turkish)}")) + ".");
        }

        var level = int.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent) ? Math.Clamp(percent, 0, 100) / 100f : -1f;
        var result = AppAudio.Apply(name, (volume, muted) => simple switch
        {
            "ayarla" when level >= 0 => (level, false),
            "kis" => (Math.Max(0.05f, volume - 0.2f), muted),
            "sessiz" => (volume, true),
            "sesli" => (volume, false),
            _ => muted ? (volume, false) : (Math.Min(1f, volume + 0.2f), false)
        });
        if (result is null)
        {
            return new QuickReply($"{TurkishGrammar.Capitalize(name)} şu an ses karıştırıcıda yok; ses çıkarmıyor olabilir.");
        }

        ledger.Record(DataFlow.Local, "Uygulama sesi", $"{result.Name}: {AudioLabel(result)}");
        return new QuickReply(result.Muted ? $"{result.Name} sessize alındı." : $"{result.Name} sesi yüzde {Math.Round(result.Volume * 100)}.", Silent: simple is "kis" or "ac");
    }

    private static string AudioLabel(AudioApp app) => app.Muted ? "Sessiz" : $"%{Math.Round(app.Volume * 100)}";
}
