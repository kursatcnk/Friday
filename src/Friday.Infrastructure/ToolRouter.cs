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
        (["harca", "masraf", "lira", " tl", "bütçe", "para", "fatura", "kira", "abonelik", "ödedim", "ödeme", "ekstre", "kredi kartı", "taksit", "fiş", "ne kadar verdim", "kaç verdim"], ["harcama", "sabit_gider", "doviz", "fis"]),
        (["dolar", "euro", "avro", "sterlin", "kur", "döviz", "altın"], ["doviz", "doviz_alarmi"]),
        (["alışkanl", "seri", "her gün"], ["aliskanlik"]),
        (["kelime", "ingilizce", "kart", "deste", "anlamı", "ne demek"], ["kelime", "kelime_cevir"]),
        (["plan", "program", "ne yapayım", "takvim", "ajanda", "toplantı", "etkinlik", "randevu"], ["gun_plani", "takvim"]),
        (["odak", "pomodoro", "mola", "ritim", "nefes", "esneme", "su iç", "çalış"], ["ritim", "pomodoro", "odak_hedefi", "isle_odak"]),
        (["müzik", "şarkı", "parça", "çalan", "spotify", "ses", "video", "youtube", "klip", "izle", "dinle"], ["medya", "uygulama_sesi", "video_ac", "sarki_kaydet"]),
        (["dosya", "klasör", "belge", "pdf", "word", "excel", "sunum", "sözleşme", "kontrat", "poliçe", "dekont", "makbuz", "rapor", "indir"], ["dosya_bul", "dosya_ac", "belgelerde_ara", "belge_oku", "dosya_kaydet"]),
        (["pencere", "sekme", "masaüstü", "masam", "masa düzen", "ekran", "tıkla", "yaz"], ["masaustu", "masaustu_kontrol", "pencere", "masa_duzeni", "ekran_suresi", "ekran_metni"]),
        (["bilgisayar", "sistem", "kilitle", "uyku", "tema", "disk", "temizl", "yavaş", "sıcak", "derece", "işlemci", "bellek", "ekran kartı", "kapat"], ["sistem_durumu", "bilgisayar", "durum_raporu", "temizlik", "disk_analizi", "nobet"]),
        (["internet", "bağlantı", "wi-fi", "wifi", "ping"], ["baglanti", "nobet"]),
        (["haber", "gündem", "ne oldu"], ["haberler"]),
        (["rutin", "mod"], ["rutin", "rutin_zamanla"]),
        (["hatırlat", "unutma", "zamanlayıcı", "alarm", "dakika say", "saniye say", "geri say", "dönünce", "açınca"], ["zamanlayici", "kosullu_hatirlatici", "doviz_alarmi"]),
        (["uyku", "yatma", "uyu"], ["uyku_saati"]),
        (["e-posta", "eposta", "mail", "mektup"], ["eposta_taslagi"]),
        (["liste", "market", "alışveriş", "kontrol"], ["liste", "kontrol_listesi"]),
        (["günlü", "ruh", "hisset", "mutsuz", "yorgun", "moral"], ["gunluk", "ruh_hali"]),
        (["hafta"], ["haftalik_ozet", "haftalik_rapor"]),
        (["rahatsız", "sessiz", "sustur"], ["rahatsiz_etme"]),
        (["kısaltma", "imza", "adresim"], ["kisaltma"]),
        (["doğum", "bayram", "yıldönüm", "özel gün", "özel tarih", "önemli tarih", "önemli gün", "kaç gün", "yılbaşı"], ["ozel_gunler", "gun_sayaci"]),
        (["sınır", "limit", "en fazla", "en çok yarım"], ["sure_siniri", "ekran_suresi"]),
        (["linguaflow", "çeviri", "altyazı", "canlı çevir"], ["linguaflow"]),
        (["okuma listesi", "sonra oku", "sayfa", "makale", "site", "link"], ["sonra_oku", "sayfayi_oku", "web_sitesi_ac", "web_ara"]),
        (["kopyala", "pano", "görsel", "resim"], ["pano_gorseli", "ekran_metni", "pano_kaydet"]),
        (["yokken", "bildirim", "kaçırd"], ["kacirdiklarim"]),
        (["ne yap", "başlayayım", "öneri", "sıkıldım", "canım sıkıl"], ["ne_yapayim", "gun_plani"]),
        (["araştır", "detaylı"], ["arastirma_raporu"]),
        (["linkedin", "github", "google"], ["web_ara", "web_sitesi_ac"]),
        (["geri al", "geri getir", "yanlışlıkla", "değişiklik", "değiştirdin"], ["geri_al"]),
        (["ne yaptım", "neler yaptım", "yapmışım", "nasıl geçti", "ne yapıyordum", "uğraştım"], ["o_gun"]),
        (["okuduğum", "okudum", "okuduklarım", "okumuştum", "açtığım", "açmıştım", "baktığım", "bakmıştım", "incelediğim", "önceki sürüm",
          "farkı ne", "değişmiş", "eski sürüm"], ["okuduklarim"]),
        (["cevap yaz", "cevap ver", "yanıt yaz", "ingilizce yaz", "ingilizce mail", "ingilizce mesaj", "mail yaz", "mesaj yaz", "kibar yap",
          "resmi yap", "kısalt", "taslak"], ["yazi"]),
        (["bu belge", "bu sayfa", "bu pdf", "bu dosya", "bu sözleşme", "bu makale", "bu mail", "bu metin", "burada", "bunda", "önümdeki", "ekrandaki", "çevir"], ["onumdeki"]),
        (["konuşmuştuk", "konuştuk", "konuştuğumuz", "demiştin", "dediğin", "söylemiştin", "söylediğin", "sormuştum", "sorduğum", "bahsetmiştin",
          "bahsettiğin", "bahsetmiştik", "önermiştin", "önerdiğin", "anlatmıştın", "geçen sefer", "önceki sohbet", "hatırlıyor musun"], ["gecmis"])
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
