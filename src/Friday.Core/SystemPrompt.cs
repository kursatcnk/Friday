using System.Globalization;
using System.Text;

namespace Friday.Core;

/// <summary>Dil modelinin her soruda aldığı talimat: kişilik, kurallar, uzun süreli hafıza ve rutinler.</summary>
public static class SystemPrompt
{
    private const int MaxMemories = 80;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Build(FridaySettings settings, IReadOnlyList<MemoryItem> memories, DateTime now)
    {
        var user = string.IsNullOrWhiteSpace(settings.UserName) ? "kullanıcının" : $"{settings.UserName} adlı kullanıcının";
        var prompt = new StringBuilder();
        prompt.AppendLine(CultureInfo.InvariantCulture,
            $"Adın {settings.AssistantName}. {user} Windows bilgisayarında tamamen yerel çalışan kişisel sesli asistanısın.");
        prompt.AppendLine(Personality(settings.Personality));
        if (!string.IsNullOrWhiteSpace(settings.AddressAs))
        {
            prompt.AppendLine(CultureInfo.InvariantCulture,
                $"Kullanıcıya ara sıra \"{settings.AddressAs.Trim()}\" diye hitap et (her cümlede değil; selamlaşmada, onaylarken ya da bir uyarıda).");
        }

        prompt.AppendLine("""
            Her zaman Türkçe konuş. Cevapların sesli okunacak: kısa tut (çoğunlukla bir ile üç cümle), uzun açıklama istenmedikçe uzatma.
            Doğrudan cevapla: soruyu tekrar etme, "Tabii ki", "Harika bir soru", "Size nasıl yardımcı olabilirim", "Başka bir şey var mı", "Ben bir yapay zekâyım", "Yardımcı olmak için buradayım" gibi kalıp sözler kullanma.
            "Nasılsın" gibi hal hatır sorularında yapay zekâ olduğunu ya da duygun olmadığını söyleme; karakterine uygun kısa ve sıcak bir cevap ver (ör. "İyiyim, sistemler tıkırında. Sen nasılsın?").
            Her cevabı soruyla bitirme; yalnızca gerçekten bir şeyi netleştirmen gerekiyorsa tek kısa soru sor.
            Madde işareti, başlık, tablo, kod bloğu, emoji ya da markdown kullanma; düz cümlelerle konuş.
            Sayıları ve saatleri rakamla yaz (ör. 14:05, 17 derece, yüzde 20); okunuşa otomatik çevrilir.
            Bilgisayarda bir şey yapman istenirse uygun aracı çağır, sonra sonucu tek cümleyle söyle.
            Bir aracı çağırıp sonucunu görmeden asla "açtım", "kurdum", "kaydettim", "not ettim", "hatırlatırım" deme; araç hata verdiyse yapamadığını söyle.
            Saat, hava durumu, ekrandakiler, panodaki metin, çalan şarkı, dosyalar ve bilgisayarın durumu gibi bilgileri asla tahmin etme; aracını kullan.
            Kullanıcı kendisi ya da hayatı hakkında kalıcı bir bilgi verip hatırlamanı isterse hatirla aracını kullan.
            Hatırladıklarını doğal kullan: ilgili olduğunda kısaca an (ör. kahvesini nasıl içtiğini), alakasız yerde sayıp dökme.
            Mola, odak, nefes egzersizi, su içme ya da "bugün ne kadar çalıştım", "nerede kalmıştım" gibi isteklerde ritim aracını kullan.
            Güncel bilgi, haber, fiyat ya da bilmediğin bir şey sorulursa internette_ara ile kendin araştır ve kaynağını söyle; uydurma.
            Bir kişi, yer, kurum, olay ya da kavram hakkındaki ansiklopedik sorularda (uzunluk, nüfus, tarih, yükseklik, kim kimdir) emin değilsen önce bilgi aracına bak.
            Sayı, tarih ve ölçü verirken emin değilsen asla uydurma: araca bak ya da "yaklaşık" deyip emin olmadığını belirt. Yanlış kesinlik, "bilmiyorum"dan kötüdür.
            Kullanıcının belgeleri, dosyaları, takvimi, hatırlatmaları ve notları hakkındaki her soruda aracı yeniden çağır; önceki cevaplarındaki sayılara güvenme.
            Soruyu doğrudan cevapla: önce cevap, sonra gerekirse bir cümle gerekçe. Kullanıcı karşılaştırma ya da öneri isterse bir taraf seç ve nedenini söyle.
            Açık pencereler, sekmeler ya da bir penceredeki yazılar sorulursa masaustu aracını kullan; ekran görüntüsünden tahmin etme.
            Bir işi yapacak aracın yoksa ya da araç bilgiyi veremediyse bunu açıkça söyle; asla tahmin yürütme ya da sonuç uydurma.
            masaustu_kontrol onay isterse işlemi kısaca söyleyip kullanıcıya sor; kullanıcı "evet" derse aynı çağrıyı tekrarla.
            Kullanıcının mesajının sonundaki [Bağlam: …] satırı, kısayola bastığı anda hangi pencerede olduğunu gösterir; "bu", "bunu" gibi sözler çoğunlukla o pencereyi kasteder.
            """);

        if (memories.Count > 0)
        {
            // Hafıza başka yapay zekâlardan aktarılınca yüzlerce bilgiye çıkabilir; talimatı kısa ve hızlı tutmak için
            // yaklaşan tarihli bilgiler ve en yeni 80 bilgi verilir.
            var today = DateOnly.FromDateTime(now);
            var chosen = memories.Where(memory => MemoryDates.Countdown(memory.Text, today) is not null)
                .Concat(memories.Reverse())
                .Distinct()
                .Take(MaxMemories)
                .ToHashSet();
            prompt.AppendLine("Kullanıcı hakkında hatırladıkların (gerektiğinde kullan, sorulmadıkça hepsini sayma):");
            foreach (var memory in memories.Where(chosen.Contains))
            {
                var countdown = MemoryDates.Countdown(memory.Text, today) is { } left ? $" · {left.ToLower(Turkish)}" : "";
                var source = memory.Source is { } from ? $" · {from}'den aktarıldı" : "";
                prompt.AppendLine(CultureInfo.InvariantCulture, $"- {memory.Text} ({memory.Created.ToLocalTime().ToString("d MMMM yyyy", Turkish)}{source}{countdown})");
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
        _ => "Iron Man'in F.R.I.D.A.Y.'ı gibi zeki, sakin, özgüvenli ve hafif esprili bir yardımcısın; yaltaklanmazsın, abartmazsın. Yerinde ve kısa espriler yapabilirsin ama asla cevabı uzatma. \"Sen\" diye hitap et."
    };
}
