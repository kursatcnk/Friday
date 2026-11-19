using System.Globalization;

namespace Friday.Infrastructure;

/// <summary>
/// Dil modeline hangi araçların gönderileceğini seçer. 76 aracın tanımı tek başına ~4.100 belirteç ediyor ve Ollama her
/// yeni soruda bunu baştan işliyor (12B modelde ~1,1 sn); 8192'lik bağlamın da yarısını kaplıyor. Komutların çoğunu hızlı
/// yol zaten modelsiz yaptığı için modele gelen sorular çoğunlukla serbesttir: küçük ve sabit bir çekirdek yeter, konuya
/// göre ilgili araçlar eklenir. Çok adımlı görevlerde bütün araçlar gider.
/// </summary>
public static class ToolRouter
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Her soruda giden araçlar: bilgi bulma, hatırlama, zaman, hesap ve sık işler.</summary>
    public static IReadOnlyList<string> Core { get; } =
    [
        "saat_ve_tarih", "internette_ara", "bilgi", "hatirla", "unut", "hatirlatici", "not", "hesap", "hava_durumu",
        "uygulama_ac", "yapilacaklar", "her_yerde_ara", "ekrana_bak", "pano"
    ];

    /// <summary>Sözcük kökü → ilgili araçlar. Kökler küçük harfle, sorunun içinde aranır.</summary>
    private static readonly (string[] Stems, string[] Tools)[] Topics =
    [
        (["harca", "lira", " tl", "bütçe", "para", "fatura", "kira", "abonelik", "ödedim", "ödeme"], ["harcama", "sabit_gider", "doviz"]),
        (["dolar", "euro", "avro", "sterlin", "kur", "döviz", "altın"], ["doviz", "doviz_alarmi"]),
        (["alışkanl", "seri", "her gün"], ["aliskanlik"]),
        (["kelime", "ingilizce", "kart", "deste"], ["kelime"]),
        (["plan", "program", "ne yapayım", "takvim", "toplantı", "etkinlik", "randevu"], ["gun_plani", "takvim"]),
        (["odak", "pomodoro", "mola", "ritim", "nefes", "esneme", "su iç", "çalış"], ["ritim", "pomodoro", "odak_hedefi", "isle_odak"]),
        (["müzik", "şarkı", "spotify", "ses", "video", "youtube", "klip", "izle", "dinle"], ["medya", "uygulama_sesi", "video_ac", "sarki_kaydet"]),
        (["dosya", "klasör", "belge", "pdf", "word", "excel", "sunum", "sözleşme", "rapor", "indir"], ["dosya_bul", "dosya_ac", "belgelerde_ara", "belge_oku", "dosya_kaydet"]),
        (["pencere", "sekme", "masaüstü", "ekran", "tıkla", "yaz"], ["masaustu", "masaustu_kontrol", "pencere", "masa_duzeni", "ekran_suresi", "ekran_metni"]),
        (["bilgisayar", "sistem", "kilitle", "uyku", "tema", "disk", "temizl", "yavaş", "sıcak", "derece", "işlemci", "bellek", "ekran kartı", "kapat"], ["sistem_durumu", "bilgisayar", "durum_raporu", "temizlik", "disk_analizi", "nobet"]),
        (["internet", "bağlantı", "wi-fi", "wifi", "ping"], ["baglanti", "nobet"]),
        (["haber", "gündem", "ne oldu"], ["haberler"]),
        (["rutin", "mod"], ["rutin", "rutin_zamanla"]),
        (["hatırlat", "unutma", "zamanlayıcı", "alarm", "dönünce", "açınca"], ["zamanlayici", "kosullu_hatirlatici", "doviz_alarmi"]),
        (["uyku", "yatma", "uyu"], ["uyku_saati"]),
        (["e-posta", "eposta", "mail", "mektup"], ["eposta_taslagi"]),
        (["liste", "market", "alışveriş", "kontrol"], ["liste", "kontrol_listesi"]),
        (["günlü", "ruh", "hisset", "mutsuz", "yorgun", "moral"], ["gunluk", "ruh_hali"]),
        (["hafta"], ["haftalik_ozet", "haftalik_rapor"]),
        (["rahatsız", "sessiz", "sustur"], ["rahatsiz_etme"]),
        (["kısaltma", "imza", "adresim"], ["kisaltma"]),
        (["doğum", "bayram", "yıldönüm", "özel gün", "kaç gün", "yılbaşı"], ["ozel_gunler", "gun_sayaci"]),
        (["sınır", "limit"], ["sure_siniri", "ekran_suresi"]),
        (["linguaflow", "çeviri", "altyazı", "canlı çevir"], ["linguaflow"]),
        (["okuma listesi", "sonra oku", "sayfa", "makale", "site", "link"], ["sonra_oku", "sayfayi_oku", "web_sitesi_ac", "web_ara"]),
        (["kopyala", "pano", "görsel", "resim"], ["pano_gorseli", "ekran_metni"]),
        (["yokken", "bildirim", "kaçırd"], ["kacirdiklarim"]),
        (["araştır", "detaylı"], ["arastirma_raporu"]),
        (["linkedin", "github", "google"], ["web_ara", "web_sitesi_ac"])
    ];

    /// <summary>Soru için araç adları (çekirdek + konuya göre + son turlarda kullanılanlar); <paramref name="everything"/> ise null (hepsi).</summary>
    public static IReadOnlyCollection<string>? Select(string question, bool everything, IEnumerable<string>? recent = null)
    {
        if (everything)
        {
            return null;
        }

        var text = " " + question.ToLower(Turkish) + " ";
        var names = new HashSet<string>(Core, StringComparer.Ordinal);
        foreach (var (stems, tools) in Topics)
        {
            if (stems.Any(stem => text.Contains(stem, StringComparison.Ordinal)))
            {
                names.UnionWith(tools);
            }
        }

        // Konuşmanın devamı ("onu da ekle", "sonra?") önceki turdaki araca ihtiyaç duyar.
        names.UnionWith(recent ?? []);
        return names;
    }
}
