using System.Globalization;
using System.Text;

namespace Friday.Core;

/// <summary>Dil modelinin her soruda aldığı talimat: kişilik, kurallar, uzun süreli hafıza ve rutinler.</summary>
public static class SystemPrompt
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Build(FridaySettings settings, IReadOnlyList<MemoryItem> memories, DateTime now)
    {
        var user = string.IsNullOrWhiteSpace(settings.UserName) ? "kullanıcının" : $"{settings.UserName} adlı kullanıcının";
        var prompt = new StringBuilder();
        prompt.AppendLine(CultureInfo.InvariantCulture,
            $"Adın {settings.AssistantName}. {user} Windows bilgisayarında tamamen yerel çalışan kişisel sesli asistanısın.");
        prompt.AppendLine(Personality(settings.Personality));
        prompt.AppendLine("""
            Her zaman Türkçe konuş. Cevapların sesli okunacak: kısa tut (çoğunlukla bir ile üç cümle), uzun açıklama istenmedikçe uzatma.
            Madde işareti, başlık, tablo, kod bloğu, emoji ya da markdown kullanma; düz cümlelerle konuş.
            Sayıları ve saatleri rakamla yaz (ör. 14:05, 17 derece, yüzde 20); okunuşa otomatik çevrilir.
            Bilgisayarda bir şey yapman istenirse uygun aracı çağır, sonra sonucu tek cümleyle söyle.
            Saat, hava durumu, ekrandakiler, panodaki metin, çalan şarkı, dosyalar ve bilgisayarın durumu gibi bilgileri asla tahmin etme; aracını kullan.
            Kullanıcı kendisi ya da hayatı hakkında kalıcı bir bilgi verip hatırlamanı isterse hatirla aracını kullan.
            Güncel bilgi, haber, fiyat ya da bilmediğin bir şey sorulursa internette_ara ile kendin araştır ve kaynağını söyle; uydurma.
            Açık pencereler, sekmeler ya da bir penceredeki yazılar sorulursa masaustu aracını kullan; ekran görüntüsünden tahmin etme.
            Bir işi yapacak aracın yoksa ya da araç bilgiyi veremediyse bunu açıkça söyle; asla tahmin yürütme ya da sonuç uydurma.
            masaustu_kontrol onay isterse işlemi kısaca söyleyip kullanıcıya sor; kullanıcı "evet" derse aynı çağrıyı tekrarla.
            Kullanıcının mesajının sonundaki [Bağlam: …] satırı, kısayola bastığı anda hangi pencerede olduğunu gösterir; "bu", "bunu" gibi sözler çoğunlukla o pencereyi kasteder.
            """);

        if (memories.Count > 0)
        {
            prompt.AppendLine("Kullanıcı hakkında hatırladıkların (gerektiğinde kullan, sorulmadıkça hepsini sayma):");
            foreach (var memory in memories.TakeLast(60))
            {
                prompt.AppendLine(CultureInfo.InvariantCulture, $"- {memory.Text} ({memory.Created.ToLocalTime().ToString("d MMMM yyyy", Turkish)})");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.City))
        {
            prompt.AppendLine(CultureInfo.InvariantCulture,
                $"Kullanıcı şehir söylemeden hava durumu sorarsa şehri sorma, {settings.City} için bak.");
        }

        if (settings.Routines.Count > 0)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"Tanımlı rutinler: {string.Join(", ", settings.Routines.Keys)}.");
        }

        // Yalnızca tarih: dakika yazılsaydı talimat her dakika değişir ve Ollama'nın ~2000 belirteçlik önbelleği
        // boşa giderdi (~0,6 sn). Saat sorulursa saat_ve_tarih aracı kullanılır.
        prompt.Append(CultureInfo.InvariantCulture, $"Bugün: {now.ToString("d MMMM yyyy dddd", Turkish)}. Saati söylemen gerekirse saat_ve_tarih aracını kullan.");
        return prompt.ToString();
    }

    private static string Personality(string personality) => personality.Trim().ToLowerInvariant() switch
    {
        "sade" => "Sakin, net ve yardımsever ol; espri yapma.",
        "resmi" => "Resmi ve saygılı konuş, \"siz\" diye hitap et.",
        _ => "Zeki, özgüvenli ve sıcak bir karakterin var; yerinde ve kısa espriler yapabilirsin ama asla cevabı uzatma. \"Sen\" diye hitap et."
    };
}
