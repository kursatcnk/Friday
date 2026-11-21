using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Gizlilik kalkanı (bkz. <see cref="PrivacyShield"/>): internete giden aramalardan kişisel kimlik bilgileri çıkarılır.</summary>
public sealed partial class AssistantTools
{
    /// <summary>
    /// Sorgusu internete giden araçlar. Açıkça verilen adresler (sayfayı oku, siteyi aç) değiştirilmez; bunlar
    /// kullanıcının ya da modelin seçtiği bir yerdir, sorgu değil.
    /// </summary>
    private static readonly HashSet<string> OnlineQueries = new(StringComparer.Ordinal)
    {
        "internette_ara", "web_ara", "bilgi", "video_ac", "arastirma_raporu", "rapor"
    };

    private IReadOnlyDictionary<string, string> Shielded(string tool, IReadOnlyDictionary<string, string> arguments)
    {
        if (!OnlineQueries.Contains(tool))
        {
            return arguments;
        }

        var removed = new List<string>();
        var clean = arguments.ToDictionary(pair => pair.Key, pair =>
        {
            var (text, kinds) = PrivacyShield.Redact(pair.Value);
            removed.AddRange(kinds);
            return text;
        });
        if (removed.Count == 0)
        {
            return arguments;
        }

        var kindsText = TurkishGrammar.Join([.. removed.Distinct(StringComparer.Ordinal)]);
        ledger.Record(DataFlow.Local, "Gizlilik kalkanı", $"İnternete gitmeden önce sorgudan çıkarıldı: {kindsText}");
        FridayLog.Info($"Gizlilik kalkanı: {tool} sorgusundan {kindsText} çıkarıldı");
        return clean;
    }
}
