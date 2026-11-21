using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Dil modelinin bilgisayarda yapabileceği işler. Bilerek dar tutuldu: dosya silme, kapatma, komut çalıştırma
/// gibi geri alınamayan araçlar yoktur. Her araç gizlilik defterine ne yaptığını ve verinin bilgisayardan çıkıp
/// çıkmadığını yazar.
/// </summary>
public sealed partial class AssistantTools(
    HttpClient http,
    OllamaChatClient chat,
    MemoryStore memory,
    PrivacyLedger ledger,
    ChatStore history,
    ReminderStore reminders,
    NoteStore notes) : IDisposable
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Araç çalışırken arayüzde gösterilen kısa açıklamalar.</summary>
    private static readonly Dictionary<string, string> Activities = new()
    {
        ["saat_ve_tarih"] = "Saate bakıyorum",
        ["uygulama_ac"] = "Uygulamayı açıyorum",
        ["web_sitesi_ac"] = "Siteyi açıyorum",
        ["web_ara"] = "Web'de arıyorum",
        ["medya"] = "Medyayı kontrol ediyorum",
        ["hava_durumu"] = "Hava durumuna bakıyorum",
        ["zamanlayici"] = "Zamanlayıcı kuruyorum",
        ["ekrana_bak"] = "Ekrana bakıyorum",
        ["pano"] = "Panodaki metinle çalışıyorum",
        ["hatirla"] = "Hafızama yazıyorum",
        ["unut"] = "Hafızamdan siliyorum",
        ["dosya_bul"] = "Dosya radarı taranıyor",
        ["dosya_ac"] = "Dosyayı açıyorum",
        ["sistem_durumu"] = "Sistemi inceliyorum",
        ["rutin"] = "Rutini çalıştırıyorum",
        ["gunluk_ozet"] = "Günün özetini hazırlıyorum",
        ["gecmis"] = "Sohbet geçmişine bakıyorum",
        ["linguaflow"] = "LinguaFlow'a bağlanıyorum",
        ["masaustu"] = "Masaüstüne bakıyorum",
        ["masaustu_kontrol"] = "Masaüstünde işlem yapıyorum",
        ["internette_ara"] = "İnternette araştırıyorum",
        ["sayfayi_oku"] = "Sayfayı okuyorum",
        ["doviz"] = "Kurlara bakıyorum",
        ["video_ac"] = "Videoyu buluyorum",
        ["ritim"] = "Ritmine bakıyorum",
        ["durum_raporu"] = "Durum raporu hazırlıyorum",
        ["bilgisayar"] = "Bilgisayarı ayarlıyorum",
        ["nobet"] = "Nöbet kuruyorum",
        ["sistem"] = "Bilgisayarı ayarlıyorum",
        ["sans"] = "Şansına bakıyorum",
        ["kronometre"] = "Kronometreye bakıyorum",
        ["ozel_komut"] = "Komutu öğreniyorum",
        ["tetik"] = "Komutunu çalıştırıyorum",
        ["secili"] = "Seçili metinle çalışıyorum",
        ["yaz"] = "Yazıyorum",
        ["pencere"] = "Pencereyi yerleştiriyorum",
        ["masa_duzeni"] = "Masayı hazırlıyorum",
        ["ekran_suresi"] = "Ekran süreni topluyorum",
        ["rahatsiz_etme"] = "Sessize alıyorum",
        ["kosullu_hatirlatici"] = "Hatırlatmayı kuruyorum",
        ["liste"] = "Listeye bakıyorum",
        ["haftalik_ozet"] = "Haftanı topluyorum",
        ["sonra_oku"] = "Okuma listene bakıyorum",
        ["sarki_kaydet"] = "Şarkıyı kaydediyorum",
        ["disk_analizi"] = "Diskini tarıyorum",
        ["gunluk"] = "Günlüğüne bakıyorum",
        ["odak_hedefi"] = "Odak hedefine bakıyorum",
        ["eposta_taslagi"] = "Taslağı hazırlıyorum",
        ["pomodoro"] = "Pomodoroyu ayarlıyorum",
        ["ruh_hali"] = "Not ediyorum",
        ["temizlik"] = "Diski kontrol ediyorum",
        ["kontrol_listesi"] = "Listeyi açıyorum",
        ["bilgi"] = "Vikipedi'ye bakıyorum",
        ["belgelerde_ara"] = "Belgelerine bakıyorum",
        ["belge_oku"] = "Belgeyi okuyorum",
        ["dosya_kaydet"] = "Dosyayı kaydediyorum",
        ["haber"] = "Haberlere bakıyorum",
        ["haberler"] = "Haberlere bakıyorum",
        ["rapor"] = "Araştırmayı başlatıyorum",
        ["arastirma_raporu"] = "Araştırmayı başlatıyorum",
        ["takvim"] = "Takvime bakıyorum",
        ["indirilenler"] = "İndirilenlere bakıyorum",
        ["gun_sonu"] = "Günü özetliyorum",
        ["onay"] = "Uyguluyorum",
        ["yapilacak"] = "Yapılacaklara bakıyorum",
        ["yapilacaklar"] = "Yapılacaklara bakıyorum",
        ["ortam"] = "Ortam sesini ayarlıyorum",
        ["pano_gecmisi"] = "Pano geçmişine bakıyorum",
        ["uygulama_kapat"] = "Uygulamayı kapatıyorum",
        ["hatirlatici"] = "Hatırlatıcıya bakıyorum",
        ["not"] = "Not defterine bakıyorum",
        ["hesap"] = "Hesaplıyorum",
        ["gun_sayaci"] = "Günleri sayıyorum",
        ["aliskanlik"] = "Alışkanlıklarına bakıyorum",
        ["harcama"] = "Harcamalarına bakıyorum",
        ["ozel_gunler"] = "Takvimdeki özel günlere bakıyorum",
        ["kisaltma"] = "Kısaltmaya bakıyorum",
        ["kelime"] = "Kelime kartlarını hazırlıyorum",
        ["pano_gorseli"] = "Görsele bakıyorum",
        ["gun_plani"] = "Gününü planlıyorum",
        ["sabit_gider"] = "Sabit giderlerine bakıyorum",
        ["doviz_alarmi"] = "Kur alarmını kuruyorum",
        ["her_yerde_ara"] = "Kayıtlarında arıyorum",
        ["haftalik_rapor"] = "Haftalık raporu yazıyorum",
        ["ne_yapayim"] = "Önceliklere bakıyorum",
        ["geri_al"] = "Değişikliğe bakıyorum",
        ["durtme"] = "Ayarını değiştiriyorum",
        ["fis"] = "Fişi okuyorum",
        ["kacirdiklarim"] = "Son bildirimlere bakıyorum",
        ["kelime_cevir"] = "Kelimenin anlamına bakıyorum",
        ["ekran_metni"] = "Ekrandaki yazıyı okuyorum",
        ["pano_kaydet"] = "Panodakini kaydediyorum",
        ["uyku_saati"] = "Uyku saatini ayarlıyorum",
        ["sure_siniri"] = "Süre sınırını ayarlıyorum",
        ["isle_odak"] = "Odak başlatıyorum",
        ["baglanti"] = "Bağlantını ölçüyorum",
        ["uygulama_sesi"] = "Uygulama sesini ayarlıyorum",
    };

    /// <summary>Hatırlatma ya da zamanlayıcı dolunca söylenecek metin.</summary>
    public event EventHandler<string>? TimerElapsed;

    public FridaySettings Settings { get; set; } = new();

    public static string DescribeActivity(string tool) => Activities.GetValueOrDefault(tool, "Çalışıyorum");

    public JsonArray Definitions { get; } =
    [
        Tool("saat_ve_tarih", "Şu anki saati, tarihi ve günü verir.", []),
        Tool("uygulama_ac", "Bilgisayarda bir uygulamayı açar (ör. not defteri, hesap makinesi, Spotify, Chrome, Steam).",
            [("ad", "string", "Uygulamanın adı", true)]),
        Tool("web_sitesi_ac", "Varsayılan tarayıcıda bir web sitesini açar.",
            [("adres", "string", "Site adresi, ör. youtube.com", true)]),
        Tool("web_ara", "Tarayıcıda Google arama sayfasını açar. Yalnızca kullanıcı aramayı tarayıcıda görmek isterse kullan; bilgi gerekiyorsa internette_ara kullan.",
            [("sorgu", "string", "Aranacak metin", true)]),
        Tool("video_ac", "YouTube'da video arar ve ilk sonucu tarayıcıda açar; video adresi verilirse onu açar. 'X videosunu aç', 'YouTube'da Y aç', 'Z şarkısının klibini aç' için.",
            [("arama", "string", "Aranacak video ya da video adresi", true)]),
        Tool("doviz", "Güncel döviz kuru (dolar, euro, sterlin…) Merkez Bankası'nın resmî günlük kurlarından. 'Dolar kaç lira' gibi sorular için önce bunu kullan.",
            [("birim", "string", "dolar, euro, sterlin… ya da boş (hepsi)", false)]),
        Tool("internette_ara", "İnternette arar, sonuç sayfalarını kendisi okur ve bilgiyi getirir. Güncel bilgi, haber, fiyat, kur, maç sonucu, tarifler ve bilmediğin her şey için kullan.",
            [("soru", "string", "Aranacak soru ya da anahtar sözcükler", true)]),
        Tool("sayfayi_oku", "Bir web sayfasının metnini okur. Adres verilmezse tarayıcıda açık olan sekmeyi okur (ör. 'bu sayfayı özetle', 'bu makalede ne diyor').",
            [("adres", "string", "Sayfa adresi; açık sekme için boş bırak", false)]),
        Tool("masaustu", "Bilgisayarda açık olanları gerçek verilerle okur: hangi pencereler/programlar açık, tarayıcıda hangi sekmeler açık, öndeki pencere ne, bir penceredeki yazılar.",
            [("islem", "string", "pencereler, sekmeler, aktif ya da oku", true), ("uygulama", "string", "oku için uygulama adı, ör. Outlook", false)]),
        Tool("masaustu_kontrol", "Masaüstünde işlem yapar: sekmeye_gec, sekmeyi_kapat, one_getir (pencereyi öne al), tikla (hedef 'Uygulama > düğme adı'), yaz (hedef pencereye metin yazar). Araç onay isterse kullanıcıya sor.",
            [("islem", "string", "sekmeye_gec, sekmeyi_kapat, one_getir, tikla ya da yaz", true), ("hedef", "string", "Sekme adı, uygulama adı ya da 'Uygulama > düğme'", true),
             ("metin", "string", "yaz için metin", false)]),
        Tool("medya", "Müzik/videoyu ve sesi kontrol eder; ne_caliyor ile o an çalan şarkıyı/videoyu söyler; ses_ayarla ile ses yüzdesini ayarlar.",
            [("komut", "string", "oynat_duraklat, sonraki, onceki, ses_ac, ses_kis, sessiz, ses_ayarla, ses_seviyesi ya da ne_caliyor", true),
             ("deger", "number", "ses_ayarla için yüzde (0–100)", false)]),
        Tool("hava_durumu", "Bir şehrin şu anki hava durumu ve gün gün tahmini (en fazla 7 gün): sıcaklık, yağış ihtimali ve miktarı. Hava ve yağmur soruları için internette_ara yerine bunu kullan.",
            [("sehir", "string", "Şehir adı; söylenmediyse boş bırak", false),
             ("gun", "number", "Kaç günlük tahmin: bugün/yarın için 2, hafta için 7", false)]),
        Tool("zamanlayici", "Belirtilen süre sonra kullanıcıya sesli hatırlatma yapar.",
            [("dakika", "number", "Kaç dakika sonra", true), ("not", "string", "Hatırlatılacak şey", false)]),
        Tool("hatirlatici", "Belirli bir zamanda sesli hatırlatma kurar (Friday kapansa bile kaybolmaz), kurulu hatırlatmaları listeler ya da siler.",
            [("islem", "string", "ekle, listele ya da sil", true),
             ("zaman", "string", "ekle için kullanıcının söylediği zaman, ör. 'yarın 9'da', 'akşam 8'de', 'pazartesi 10:30'da'", false),
             ("metin", "string", "Hatırlatılacak şey (ekle) ya da silinecek hatırlatmada geçen sözcük (sil)", false)]),
        Tool("not", "Kullanıcının sesli notlarını kaydeder ya da son notları okur (D:\\Friday\\data\\notes.md).",
            [("islem", "string", "ekle ya da oku", true), ("metin", "string", "ekle için not", false)]),
        Tool("hesap", "Aritmetik, yüzde ve birim çevirme hesaplarını kesin olarak yapar. Hesap sorularında kendi hesaplama, bunu kullan.",
            [("soru", "string", "Ör. '345 çarpı 12', '200'ün yüzde 15'i kaç', '5 mil kaç kilometre'", true)]),
        Tool("gun_sayaci", "Bir güne kaç gün kaldığını söyler: yılbaşı, bayramlar, bir tarih ya da hafızadaki doğum günü gibi tarihler.",
            [("hedef", "string", "Ör. 'yılbaşı', '23 nisan', 'doğum günüm'", true)]),
        Tool("ekrana_bak", "Kullanıcının ekranına bakar ve soruyu ekranda görünene göre cevaplar (ör. bu hata ne, burada ne yazıyor, bunu çevir).",
            [("soru", "string", "Ekranla ilgili soru", true), ("tum_ekran", "boolean", "Sadece aktif pencere yerine tüm ekran", false)]),
        Tool("pano", "Kullanıcının kopyaladığı (panodaki) metinle çalışır. oku dışındaki işlemlerin sonucu panoya geri konur.",
            [("islem", "string", "oku, ozetle, cevir, duzelt, resmilestir, maddele ya da acikla", true),
             ("dil", "string", "cevir için hedef dil, ör. İngilizce", false)]),
        Tool("hatirla", "Kullanıcının kalıcı olarak hatırlamanı istediği bir bilgiyi kaydeder.",
            [("bilgi", "string", "Kullanıcıdan üçüncü şahısla söz eden, kendi içinde anlamlı tek cümle, ör. 'Kullanıcının arabasının muayenesi 20 Kasım'da.'", true)]),
        Tool("unut", "Hafızadaki bir bilgiyi siler.", [("konu", "string", "Silinecek bilgide geçen sözcük", true)]),
        Tool("dosya_bul", "Bilgisayarda dosya arar (Windows Arama dizini). Sonuçlar numaralıdır.",
            [("arama", "string", "Dosya adında ya da içinde geçen sözcükler; yoksa boş", false),
             ("tur", "string", "pdf, word, excel, sunum, resim, video, muzik, arsiv, kod, kurulum ya da boş", false),
             ("son_gun", "number", "Son kaç gün içinde değişmiş; dün için 1, bilinmiyorsa 0", false),
             ("klasor", "string", "indirilenler, belgeler, masaustu, resimler ya da boş", false),
             ("ac", "boolean", "En uygun sonucu hemen aç", false)]),
        Tool("dosya_ac", "dosya_bul sonuçlarından birini açar.",
            [("numara", "number", "Sonuç numarası", true), ("klasorde_goster", "boolean", "Dosyayı açmak yerine klasörünü göster", false)]),
        Tool("sistem_durumu", "Bilgisayarın durumu: işlemci, bellek, disk, ekran kartı sıcaklığı (derece), fan ve güç, en çok kaynak kullanan uygulamalar. 'Bilgisayar kaç derece', 'neden yavaş' için.", []),
        Tool("rutin", "Tanımlı bir rutini çalıştırır (ör. çalışma modu, mola).", [("ad", "string", "Rutinin adı", true)]),
        Tool("gunluk_ozet", "Günaydın brifingi: tarih, hava, zamanlayıcılar ve hatırlatmalar.", []),
        Tool("gecmis", "Geçmiş sohbetler: belirli bir günde neler konuşulduğunu verir ya da bütün sohbetlerde bir konuyu arar ('geçen hafta X hakkında ne konuşmuştuk').",
            [("gun", "string", "bugun, dun ya da yyyy-aa-gg", false), ("ara", "string", "Sohbetlerde aranacak sözcük", false)]),
        Tool("ritim", "Çalışma ritmi koçu: mola başlatır, odak oturumu açar/bitirir, molayı erteler, sesli nefes egzersizi ya da esneme yaptırır, içilen suyu sayar, günün çalışma özetini ve moladan önce kalınan yeri söyler.",
            [("islem", "string", "mola, mola_bitir, odak, odak_bitir, ertele, nefes, esneme, su, durum (bugün), hafta ya da kaldigim_yer", true),
             ("dakika", "number", "odak ya da erteleme süresi (dakika)", false)]),
        Tool("linguaflow", "LinguaFlow canlı çeviri uygulamasını açıp ekrandaki video/toplantı için çeviriyi başlatır ya da durdurur.",
            [("islem", "string", "baslat ya da durdur", true)]),
        Tool("durum_raporu", "Kısa durum raporu: işlemci, bellek, ekran kartı sıcaklığı, disk, sıradaki hatırlatma, günün çalışma süresi, nöbetler. 'Durum raporu', 'her şey yolunda mı' için.", []),
        Tool("bilgisayar", "Bilgisayarı kilitler, uyku moduna alır, ekranı kapatır, masaüstünü gösterir, koyu ya da açık temaya geçer, ekran görüntüsü alır, belirli dakika sonra kapatır ya da kapatmayı iptal eder. Yalnızca kullanıcı açıkça isterse kullan.",
            [("islem", "string", "kilitle, uyku, ekran_kapat, masaustu, tema_koyu, tema_acik, ekran_goruntusu, kapat ya da kapat_iptal", true),
             ("dakika", "number", "kapat için kaç dakika sonra (en az 1)", false)]),
        Tool("nobet", "Bir şey olunca haber vermek için bekler: indirme bitince, bilgisayar boşa çıkınca (render, derleme, kurulum bitti), internet gelince ya da ekran kartı sıcaklığı sınırı geçince. Bekleyenleri listeler ya da iptal eder.",
            [("islem", "string", "indirme, bosta, internet, gpu, liste ya da iptal", true), ("derece", "number", "gpu için sıcaklık sınırı", false)]),
        Tool("haberler", "Güncel haber başlıklarını getirir (gündem, spor, ekonomi, teknoloji, dünya). 'Bugün neler oldu', 'gündemde ne var' için.",
            [("kategori", "string", "gundem, spor, ekonomi, teknoloji ya da dunya", false)]),
        Tool("pencere", "Kullanıcının etkin penceresini yerleştirir: sola ya da sağa yaslar, tam ekran yapar, küçültür ya da son iki pencereyi yan yana koyar.",
            [("islem", "string", "sol, sag, buyut, kucult ya da bol", true)]),
        Tool("belgelerde_ara", "Kullanıcının bilgisayarındaki belgelerde (PDF, Word, Excel, PowerPoint, metin) arar ve en ilgili parçaları dosya adlarıyla verir. 'Sözleşmede iptal şartı neydi', 'raporda bütçe kaçtı', 'notlarımda X geçiyor muydu' gibi kullanıcının kendi belgeleriyle ilgili sorularda kullan; cevabı parçalara dayandır ve dosya adını söyle.",
            [("soru", "string", "Aranacak soru ya da anahtar sözcükler", true)]),
        Tool("belge_oku", "Adı (ya da yolu) verilen belgenin metnini okur; özetlemek, içinden bilgi çıkarmak ya da soru cevaplamak için.",
            [("ad", "string", "Belgenin adı ya da bir kısmı, ör. kira sözleşmesi, rapor.pdf", true)]),
        Tool("dosya_kaydet", "Bir metni (özet, plan, liste, taslak) Friday'in Kaydedilenler klasörüne Markdown dosyası olarak kaydeder; var olan dosyanın üstüne yazmaz.",
            [("ad", "string", "Dosya adı, uzantısız", true), ("icerik", "string", "Kaydedilecek metin", true)]),
        Tool("bilgi", "Vikipedi'den kısa, doğrulanmış özet: kişi, yer, kurum, olay ya da kavram hakkında uzunluk, nüfus, tarih, kim kimdir gibi ansiklopedik bilgiler. Hızlıdır; emin olmadığın her sayıda önce buna bak.",
            [("konu", "string", "Konunun adı, ör. Kızılırmak, Ağrı Dağı, Marie Curie", true)]),
        Tool("kosullu_hatirlatici", "Saate değil olaya bağlı hatırlatma: kullanıcı bilgisayara dönünce ya da belirli bir uygulamayı açınca söylenir. 'Dönünce X'i hatırlat', 'Chrome'u açınca X'i hatırlat'.",
            [("kosul", "string", "donus ya da uygulama:<uygulama adı>", true), ("metin", "string", "Hatırlatılacak şey", true)]),
        Tool("sonra_oku", "Okuma listesi: tarayıcıda açık sayfayı sonra okumak için kaydeder (kaydet), listeyi söyler (listele) ya da sıradaki sayfayı açıp listeden çıkarır (ac).",
            [("islem", "string", "kaydet, listele ya da ac", true)]),
        Tool("eposta_taslagi", "E-posta taslağı: metni sen yaz, bu araç varsayılan e-posta uygulamasında alıcı, konu ve metinle yeni bir taslak açar ve metni panoya kopyalar. Asla kendin göndermez; gönderme kararı kullanıcının.",
            [("alici", "string", "E-posta adresi (biliniyorsa)", false), ("konu", "string", "Konu", true), ("metin", "string", "E-postanın tam metni", true)]),
        Tool("pomodoro", "Pomodoro serisi: 25 dk odak + 5 dk mola döngüleri; baslat (adet ile) ya da durdur.",
            [("islem", "string", "baslat ya da durdur", true), ("adet", "string", "Kaç pomodoro (varsayılan 4)", false)]),
        Tool("ruh_hali", "Kullanıcının ruh halini 1-10 arası günlüğe kaydeder; haftalık özette ortalaması söylenir.",
            [("puan", "string", "1-10", true), ("kelime", "string", "Kullanıcının kendi sözü (yorgun, iyi…)", false)]),
        Tool("temizlik", "Geçici dosyaların ve geri dönüşüm kutusunun boyutunu söyler, onay alınca temizler.", []),
        Tool("kontrol_listesi", "Adı verilen listeyi sesli kontrol listesi olarak adım adım okur; kullanıcı her öğe için tamam/yok der, sonunda eksikleri söyler.",
            [("ad", "string", "Liste adı, ör. seyahat", true)]),
        Tool("odak_hedefi", "Günlük odak hedefi: dakika verilirse hedefi ayarlar; verilmezse bugünkü ilerlemeyi ve kaç gündür üst üste tutturulduğunu söyler.",
            [("dakika", "string", "Yeni günlük hedef (dakika), ör. 180", false)]),
        Tool("sarki_kaydet", "O an çalan şarkıyı 'beğendiğim şarkılar' listesine ekler.", []),
        Tool("disk_analizi", "Bilgisayarda en çok yer kaplayan klasörleri ve en büyük dosyaları bulur (hiçbir şey silmez); yer açmak için öneri verir.", []),
        Tool("gunluk", "Sesli günlük: yazı ekler (yaz) ya da bugün, dün veya bu hafta yazılanları getirir (oku); kullanıcının günlüğünü özetlemek ya da içinde aramak için oku.",
            [("islem", "string", "yaz ya da oku", true), ("metin", "string", "Yazılacak şey", false), ("donem", "string", "bugun, dun ya da hafta", false)]),
        Tool("haftalik_ozet", "Son 7 günün özeti: bilgisayar başında geçen süre ve en çok kullanılan uygulamalar, tamamlanan ve bekleyen işler, sohbetler, yaklaşan hatırlatmalar. 'Bu hafta neler yaptım', 'haftamı özetle'.", []),
        Tool("liste", "Adlandırılmış listeler (market, alışveriş, okunacaklar…): öğe ekler, çıkarır, okur, temizler ya da listeleri sayar.",
            [("islem", "string", "ekle, cikar, oku, temizle ya da listeler", true), ("ad", "string", "Listenin adı, ör. market", false), ("oge", "string", "Öğe(ler); birden çoksa virgül ya da 've' ile", false)]),
        Tool("rahatsiz_etme", "Rahatsız etme modu: belirtilen süre boyunca Friday kendiliğinden konuşmaz (mola önerisi, alışkanlık önerisi, odak hatırlatması); kullanıcının kurduğu hatırlatmalar yine gelir.",
            [("islem", "string", "ac ya da kapat", true), ("dakika", "string", "Kaç dakika (varsayılan 60)", false)]),
        Tool("ekran_suresi", "Ekran süresi: bugün ya da dün bilgisayar başında ne kadar kalındığını, en çok kullanılan uygulamaları ve sitelerin (YouTube, Netflix…) süresini söyler.",
            [("gun", "string", "bugun ya da dun", false), ("uygulama", "string", "Yalnızca bu uygulama ya da site, ör. YouTube", false)]),
        Tool("masa_duzeni", "Masa düzenleri: o an açık programları ve pencerelerin yerini bir adla kaydeder (kaydet), kaydedilen düzeni geri kurar; eksik programları açıp pencereleri yerine koyar (kur), kayıtlı düzenleri sayar (listele) ya da siler (sil).",
            [("islem", "string", "kaydet, kur, listele ya da sil", true), ("ad", "string", "Düzenin adı, ör. kodlama, oyun", false)]),
        Tool("rutin_zamanla", "Bir rutini belirli saatte kendiliğinden çalışacak şekilde zamanlar ya da zamanlamayı kaldırır. 'Çalışma modunu hafta içi her gün 9'da çalıştır', 'günaydın rutini Friday açılınca çalışsın'.",
            [("ad", "string", "Rutinin adı", true), ("zaman", "string", "SS:dd ve gün: '09:00 haftaici', '18:30 hergun', '10:00 haftasonu'; açılışta için 'acilis'; kaldırmak için 'kapat'", true)]),
        Tool("arastirma_raporu", "Bir konuyu arka planda internette araştırıp kaynaklı bir rapor dosyası hazırlar; bitince kullanıcıya haber verilir. 'X hakkında rapor hazırla', 'X'i detaylı araştır' için.",
            [("konu", "string", "Araştırılacak konu", true)]),
        Tool("takvim", "Kullanıcının takvimindeki etkinlikleri söyler (Google/Outlook takvimi bağlıysa).",
            [("gun", "string", "bugun, yarin, hafta ya da sonraki", false)]),
        Tool("yapilacaklar", "Kullanıcının yapılacaklar listesi: iş ekler, listeler, tamamlar.",
            [("islem", "string", "ekle, listele ya da tamamla", true), ("metin", "string", "İşin metni (ekle ve tamamla için)", false)]),
        Tool("geri_al", "Friday'in kayıtlarda (yapılacaklar, harcamalar, hatırlatıcılar, listeler, notlar, hafıza, alışkanlıklar, günlük, kelime kartları) son isteğinde yaptığı değişikliği geri alır, geri alınanı geri getirir ya da son değişiklikleri sayar. Uygulama açma, ses, pencere gibi işler geri alınamaz.",
            [("islem", "string", "geri_al, geri_getir ya da liste", true)]),
        Tool("ne_yapayim", "Şimdi ne yapılmalı: geciken ve bugün teslim işleri, mola ihtiyacı, ödeme günü, alışkanlıklar ve gün planına bakıp tek bir öneri verir. 'Ne yapayım', 'nereden başlayayım'.", []),
        Tool("haftalik_rapor", "Son 7 günün raporunu (odak, ekran süresi, alışkanlıklar, harcamalar, işler, günlük, kelime pratiği) Kaydedilenler klasörüne Markdown dosyası olarak yazar.", []),
        Tool("her_yerde_ara", "Kullanıcının Friday'deki bütün kayıtlarında arar: notlar, günlük, yapılacaklar, listeler, hafıza, harcamalar, sabit giderler, alışkanlıklar, kısaltmalar ve sohbetler. 'X hakkında ne kaydetmiştim', 'kayıtlarımda X'.",
            [("sorgu", "string", "Aranacak sözcükler", true)]),
        Tool("kacirdiklarim", "Friday'in son 6 saatte kendiliğinden söylediği hatırlatma ve bildirimleri tekrar eder ('ben yokken ne dedin').", []),
        Tool("ekran_metni", "Etkin penceredeki yazıyı görme modeliyle aynen çıkarıp panoya koyar ('ekrandaki yazıyı kopyala').", []),
        Tool("doviz_alarmi", "Kur alarmı: dolar, euro ya da sterlin belirli bir TL değerinin üstüne çıkınca ya da altına inince haber verir (15 dk'da bir kontrol); alarmları listeler ya da siler.",
            [("islem", "string", "ekle, listele ya da sil", true), ("birim", "string", "dolar, euro, sterlin", false), ("deger", "number", "Eşik (TL)", false), ("yon", "string", "ust, alt ya da boş (şimdiki kura göre)", false)]),
        Tool("uyku_saati", "Uyku saati: ayarlar (saat SS:dd), kapatır ya da söyler; uyku saatinden yarım saat önce ekranları bırakmayı hatırlatır.",
            [("islem", "string", "ayarla, kapat ya da durum", true), ("saat", "string", "SS:dd, ör. 23:30", false)]),
        Tool("sabit_gider", "Her ay ödenen sabit giderler (kira, fatura, abonelik): ekler (ad, tutar TL, ayın günü), listeler, siler ya da bu ay ödendiğini işler (odendi; harcamalara eklenir). Ödeme günü sabah hatırlatılır.",
            [("islem", "string", "ekle, listele, sil ya da odendi", true), ("ad", "string", "Gider adı, ör. kira, Netflix", false),
             ("tutar", "number", "Aylık tutar (TL)", false), ("gun", "number", "Ayın kaçında ödendiği (1–31)", false)]),
        Tool("sure_siniri", "Uygulama ya da site için günlük süre sınırı (ör. YouTube'a günde 60 dk): ekler, kaldırır ya da sınırları ve bugünkü kullanımı söyler; sınır dolunca Friday nazikçe hatırlatır.",
            [("islem", "string", "ekle, sil ya da listele", true), ("ad", "string", "Uygulama ya da site adı", false), ("dakika", "number", "Günlük sınır (dakika)", false)]),
        Tool("gun_plani", "Günün kalanını planlar: takvim etkinlikleri ve saatli hatırlatmalar yerinde kalır, bekleyen işler odak bloklarına, yapılmamış alışkanlıklar akşama yerleşir; blokların başında hatırlatmayı önerir.", []),
        Tool("isle_odak", "Belirli bir iş için odak oturumu başlatır; bitince işi bitirip bitirmediğini sorar ve yapılacaklardan düşer.",
            [("is", "string", "Odaklanılacak iş", true), ("dakika", "number", "Süre (varsayılan 25)", false)]),
        Tool("baglanti", "İnternet bağlantısını ölçer (modem ve internet gecikmesi, paket kaybı, DNS) ve sorun nerede söyler. 'İnternet yavaş mı', 'bağlantımı kontrol et'.", []),
        Tool("pano_gorseli", "Panoya kopyalanan görsele (ekran alıntısı, resim) bakar: içindeki yazıyı aynen çıkarıp panoya koyar (oku) ya da görselle ilgili soruyu cevaplar (acikla).",
            [("islem", "string", "oku ya da acikla", true), ("soru", "string", "acikla için soru", false)]),
        Tool("kelime", "İngilizce kelime pratiği (aralıklı tekrar): çalışmayı başlatır (calis), desteye kelime ekler (ekle, kelime ve anlam ile), kaç kelime öğrenildiğini söyler (durum) ya da kelime siler (sil).",
            [("islem", "string", "calis, ekle, durum, sil ya da bitir", true), ("kelime", "string", "İngilizce kelime", false), ("anlam", "string", "Türkçe anlamı (ekle için)", false)]),
        Tool("uygulama_sesi", "Uygulama bazında ses (ses karıştırıcı): bir uygulamanın (Spotify, Chrome, Discord…) sesini kısar, açar, yüzdeye ayarlar, sessize alır ya da ses çıkaran uygulamaları listeler.",
            [("islem", "string", "kis, ac, ayarla, sessiz, sesli ya da listele", true), ("ad", "string", "Uygulama adı", false), ("deger", "number", "ayarla için yüzde (0–100)", false)]),
        Tool("kisaltma", "Metin kısaltmaları: sık yazılan bir metni (imza, adres, IBAN…) adıyla kaydeder (ekle), açık pencereye yazar (kullan), listeler ya da siler.",
            [("islem", "string", "ekle, kullan, listele ya da sil", true), ("ad", "string", "Kısaltmanın adı, ör. imza", false), ("metin", "string", "ekle için kaydedilecek metin", false)]),
        Tool("ozel_gunler", "Yaklaşan önemli günler: hafızadaki doğum günleri, yıldönümleri ve resmî günler (iki ay içinde).", []),
        Tool("aliskanlik", "Günlük alışkanlık takipçisi (kitap okumak, spor, meditasyon…): yeni alışkanlık ekler, bugün yapıldığını işaretler (yaptim), işareti kaldırır (geri_al), siler ya da serileri ve bu haftayı söyler (listele).",
            [("islem", "string", "ekle, yaptim, geri_al, sil ya da listele", true), ("ad", "string", "Alışkanlığın adı", false)]),
        Tool("harcama", "Harcama kaydı: harcama ekler (tutar TL ve ne için), bugün/hafta/ay harcamalarını kategori kategori özetler, son harcamayı siler, aylık bütçe koyar ya da hepsini Excel'e (CSV) aktarır.",
            [("islem", "string", "ekle, ozet, sil_son, butce ya da disa_aktar", true), ("tutar", "number", "TL cinsinden tutar (ekle ve butce için)", false),
             ("aciklama", "string", "Ne için, ör. kahve, market", false), ("donem", "string", "bugun, dun, hafta, ay ya da gecen_ay", false),
             ("kategori", "string", "Özet için kategori ya da sözcük, ör. market", false)]),
    ];

    private readonly Dictionary<string, JsonArray> _toolSets = new(StringComparer.Ordinal);

    /// <summary>Soru için gönderilecek araç tanımları (bkz. <see cref="ToolRouter"/>); aynı küme tekrar kurulmaz.</summary>
    public JsonArray DefinitionsFor(string question, bool everything, IEnumerable<string>? recent = null)
    {
        if (ToolRouter.Select(question, everything, recent) is not { } names)
        {
            return Definitions;
        }

        var key = string.Join(',', names.Order(StringComparer.Ordinal));
        lock (_toolSets)
        {
            if (_toolSets.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var subset = new JsonArray([.. Definitions.Where(node => node?["function"]?["name"]?.GetValue<string>() is { } name && names.Contains(name)).Select(node => node!.DeepClone())]);
            if (_toolSets.Count > 64)
            {
                _toolSets.Clear();
            }

            _toolSets[key] = subset;
            return subset;
        }
    }

    public async Task<string> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        call = call with { Arguments = Shielded(call.Name, call.Arguments) };
        try
        {
            return call.Name switch
            {
                "saat_ve_tarih" => DateTime.Now.ToString("d MMMM yyyy dddd, HH:mm", Turkish),
                "uygulama_ac" => OpenApp(Argument(call, "ad")),
                "web_sitesi_ac" => OpenWebsite(Argument(call, "adres")),
                "web_ara" => Search(Argument(call, "sorgu")),
                "medya" => await MediaAsync(Argument(call, "komut"), Argument(call, "deger")).ConfigureAwait(false),
                "hava_durumu" => await WeatherAsync(Argument(call, "sehir"), cancellationToken,
                    int.TryParse(Argument(call, "gun"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) ? days : 2).ConfigureAwait(false),
                "zamanlayici" => StartTimer(Argument(call, "dakika"), Argument(call, "not")),
                "ekrana_bak" => await LookAtScreenAsync(Argument(call, "soru"), Flag(call, "tum_ekran"), cancellationToken).ConfigureAwait(false),
                "pano" => await ClipboardAsync(Argument(call, "islem"), Argument(call, "dil"), cancellationToken).ConfigureAwait(false),
                "hatirla" => Remember(Argument(call, "bilgi")),
                "unut" => Forget(Argument(call, "konu")),
                "dosya_bul" => FindFiles(call),
                "dosya_ac" => OpenFound(Argument(call, "numara"), Flag(call, "klasorde_goster")),
                "sistem_durumu" => await SystemAsync(cancellationToken).ConfigureAwait(false),
                "rutin" => await RunRoutineAsync(Argument(call, "ad"), cancellationToken).ConfigureAwait(false),
                "gunluk_ozet" => await BriefingAsync(cancellationToken).ConfigureAwait(false),
                "gecmis" => History(Argument(call, "gun"), Argument(call, "ara")),
                "durum_raporu" => (await StatusReportAsync(cancellationToken).ConfigureAwait(false)).Text,
                "bilgisayar" => SystemCommand(Argument(call, "islem"), Argument(call, "dakika")).Text,
                "nobet" => (await WatchCommandAsync(Argument(call, "islem"), Argument(call, "derece"), cancellationToken).ConfigureAwait(false)).Text,
                "haberler" => (await NewsAsync(Argument(call, "kategori"), cancellationToken).ConfigureAwait(false)).Text,
                "arastirma_raporu" => ResearchCommand("hazirla", Argument(call, "konu")).Text,
                "takvim" => (await CalendarCommandAsync(Argument(call, "gun"), cancellationToken).ConfigureAwait(false)).Text,
                "yapilacaklar" => TodoCommand(Argument(call, "islem"), Argument(call, "metin"))?.Text ?? "Listede böyle bir iş yok.",
                "pencere" => WindowCommand(Argument(call, "islem")).Text,
                "belgelerde_ara" => await Task.Run(() => SearchDocuments(Argument(call, "soru")), cancellationToken).ConfigureAwait(false),
                "belge_oku" => await Task.Run(() => ReadDocument(Argument(call, "ad")), cancellationToken).ConfigureAwait(false),
                "dosya_kaydet" => SaveDocument(Argument(call, "ad"), Argument(call, "icerik")),
                "bilgi" => await EncyclopediaAsync(Argument(call, "konu"), cancellationToken).ConfigureAwait(false),
                "ekran_suresi" => ScreenTimeCommand(Argument(call, "gun"), Argument(call, "uygulama")).Text,
                "rahatsiz_etme" => QuietCommand(Argument(call, "islem"), Argument(call, "dakika")),
                "kosullu_hatirlatici" => TriggeredReminder(Argument(call, "kosul"), Argument(call, "metin")),
                "haftalik_ozet" => WeeklyReview(),
                "sonra_oku" => ReadLater(Argument(call, "islem")),
                "sarki_kaydet" => await SaveSongAsync().ConfigureAwait(false),
                "odak_hedefi" => FocusGoal(Argument(call, "dakika")),
                "eposta_taslagi" => EmailDraft(Argument(call, "alici"), Argument(call, "konu"), Argument(call, "metin")),
                "pomodoro" => PomodoroCommand(Argument(call, "islem"), Argument(call, "adet")),
                "ruh_hali" => MoodCommand(Argument(call, "puan"), Argument(call, "kelime")),
                "temizlik" => (await Task.Run(CleanupOffer, cancellationToken).ConfigureAwait(false)).Text,
                "kontrol_listesi" => StartChecklist(Argument(call, "ad")).Text,
                "disk_analizi" => await Task.Run(DiskReport, cancellationToken).ConfigureAwait(false),
                "gunluk" => JournalCommand(Argument(call, "islem"), Argument(call, "metin"), Argument(call, "donem")),
                "liste" => ListCommand(Argument(call, "islem"), Argument(call, "ad"), Argument(call, "oge")),
                "masa_duzeni" => (await WorkspaceCommandAsync(Argument(call, "islem"), Argument(call, "ad"), cancellationToken).ConfigureAwait(false)).Text,
                "rutin_zamanla" => ScheduleRoutine(Argument(call, "ad"), Argument(call, "zaman")),
                "linguaflow" => await LinguaFlowAsync(Argument(call, "islem"), cancellationToken).ConfigureAwait(false),
                "video_ac" => await OpenVideoAsync(Argument(call, "arama"), cancellationToken).ConfigureAwait(false),
                "doviz" => await ExchangeRatesAsync(Argument(call, "birim"), cancellationToken).ConfigureAwait(false),
                "ritim" => RhythmCommand(Argument(call, "islem"), Argument(call, "dakika")).Text,
                "hatirlatici" => ReminderCommand(Argument(call, "islem"), Argument(call, "zaman"), Argument(call, "metin")),
                "not" => NoteCommand(Argument(call, "islem"), Argument(call, "metin")),
                "hesap" => SpokenMath.Answer(Argument(call, "soru")) ?? "Bu hesabı çözemedim; sayıları ve işlemi açıkça söyle.",
                "gun_sayaci" => CountdownAnswer(Argument(call, "hedef")),
                "ozel_gunler" => SpecialDaysCommand().Text,
                "her_yerde_ara" => SearchEverywhere(Argument(call, "sorgu")).Text,
                "haftalik_rapor" => WeeklyReportFile().Text,
                "ne_yapayim" => NextAction().Text,
                "geri_al" => UndoCommand(Argument(call, "islem")).Text,
                "kacirdiklarim" => MissedAnnouncements().Text,
                "ekran_metni" => (await ScreenTextAsync(cancellationToken).ConfigureAwait(false)).Text,
                "doviz_alarmi" => (await RateAlertCommandAsync(Argument(call, "islem"), Argument(call, "birim"), Argument(call, "deger"), Argument(call, "yon")).ConfigureAwait(false)).Text,
                "uyku_saati" => BedTimeCommand(Argument(call, "islem"), Argument(call, "saat")).Text,
                "sabit_gider" => BillCommand(Argument(call, "islem"), Argument(call, "ad"), Argument(call, "tutar"), Argument(call, "gun"))?.Text ?? "Böyle bir sabit gider ya da iş yok.",
                "sure_siniri" => LimitCommand(Argument(call, "islem"), Argument(call, "ad"), Argument(call, "dakika")).Text,
                "gun_plani" => (await DayPlanAsync(cancellationToken).ConfigureAwait(false)).Text,
                "isle_odak" => FocusOnTask(Argument(call, "is"), Argument(call, "dakika")).Text,
                "baglanti" => (await NetworkCommandAsync(cancellationToken).ConfigureAwait(false)).Text,
                "pano_gorseli" => (await ClipboardImageAsync(Argument(call, "islem"), Argument(call, "soru"), cancellationToken).ConfigureAwait(false)).Text,
                "kelime" => VocabularyCommand(Argument(call, "islem"), Argument(call, "kelime"), Argument(call, "anlam")).Text,
                "uygulama_sesi" => await Task.Run(() => AppVolumeCommand(Argument(call, "islem"), Argument(call, "ad"), Argument(call, "deger")).Text, cancellationToken).ConfigureAwait(false),
                "kisaltma" => SnippetCommand(Argument(call, "islem"), Argument(call, "ad"), Argument(call, "metin"))?.Text ?? "Böyle bir kısaltma yok.",
                "aliskanlik" => HabitCommand(Argument(call, "islem"), Argument(call, "ad"))?.Text ?? "Böyle bir alışkanlık takip edilmiyor; eklemek için islem=ekle kullan.",
                "harcama" => ExpenseCommand(Argument(call, "islem"), Argument(call, "tutar"), Argument(call, "aciklama"), Argument(call, "donem"), Argument(call, "kategori")).Text,
                "internette_ara" => await SearchWebAsync(Argument(call, "soru"), cancellationToken).ConfigureAwait(false),
                "sayfayi_oku" => await ReadPageAsync(Argument(call, "adres"), cancellationToken).ConfigureAwait(false),
                // UI Automation çağrıları zaman alabilir; asistanın akışını bekletmesin diye arka planda çalışır.
                "masaustu" => await Task.Run(() => DesktopInfo(Argument(call, "islem"), Argument(call, "uygulama")), cancellationToken).ConfigureAwait(false),
                "masaustu_kontrol" => await Task.Run(() => DesktopControl(Argument(call, "islem"), Argument(call, "hedef"), Argument(call, "metin")), cancellationToken).ConfigureAwait(false),
                _ => $"Böyle bir araç yok: {call.Name}"
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Aracın kendi zaman aşımı: kullanıcı iptal etmedi, kaynak yanıt vermedi. Model bunu söyleyebilsin.
            return "Hata: kaynak zamanında yanıt vermedi. Kullanıcıya bunu söyle, uydurma.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return $"Hata: {exception.Message}";
        }
    }

    private string OpenApp(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Uygulama adı söylenmedi.";
        }

        return FindApp(name) is { } app ? Open(app) : $"'{name}' adında bir uygulama bulunamadı.";
    }

    private string OpenWebsite(string address)
    {
        var text = address.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return "Geçerli bir web adresi değil.";
        }

        Launch(uri.AbsoluteUri);
        ledger.Record(DataFlow.Outbound, "Tarayıcı", $"{uri.Host} açıldı");
        return $"{uri.Host} açıldı.";
    }

    private string Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Aranacak bir şey söylenmedi.";
        }

        Launch("https://www.google.com/search?q=" + Uri.EscapeDataString(query));
        ledger.Record(DataFlow.Outbound, "Google araması", $"\"{query}\" tarayıcıda arandı");
        return $"'{query}' için arama sonuçları tarayıcıda açıldı.";
    }

    private async Task<string> MediaAsync(string command, string value = "")
    {
        if (Simplify(command) is "sesayarla" or "sesseviyesi")
        {
            return SystemVolume.Apply(Simplify(command) == "sesayarla"
                ? int.TryParse(value.Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? level : -1
                : -1);
        }

        if (Simplify(command) is "necaliyor" or "calan")
        {
            ledger.Record(DataFlow.Local, "Medya", "Çalan şarkının bilgisi Windows'tan okundu");
            return await NowPlaying.DescribeAsync().ConfigureAwait(false);
        }

        var (key, repeat) = Simplify(command) switch
        {
            "oynatduraklat" or "oynat" or "duraklat" or "durdur" => (VirtualKey.PlayPause, 1),
            "sonraki" => (VirtualKey.Next, 1),
            "onceki" => (VirtualKey.Previous, 1),
            "sesac" or "sesiac" => (VirtualKey.VolumeUp, 5),
            "seskis" or "sesikis" => (VirtualKey.VolumeDown, 5),
            "sessiz" => (VirtualKey.Mute, 1),
            _ => ((VirtualKey)0, 0)
        };
        if (repeat == 0)
        {
            return $"Bilinmeyen medya komutu: {command}";
        }

        for (var index = 0; index < repeat; index++)
        {
            NativeMethods.keybd_event((byte)key, 0, 0, 0);
            NativeMethods.keybd_event((byte)key, 0, 2, 0);
        }

        return "Tamam.";
    }

    /// <param name="days">Tahmin günü sayısı (2–7); haftalık sorular için 7.</param>
    private async Task<string> WeatherAsync(string city, CancellationToken cancellationToken, int days = 2)
    {
        var forecast = await FetchForecastAsync(city, days, cancellationToken).ConfigureAwait(false);
        if (forecast is null)
        {
            return $"'{(string.IsNullOrWhiteSpace(city) ? Settings.City : city)}' bulunamadı.";
        }

        static string Degrees(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);
        string Day(ForecastDay day, int index)
        {
            var label = index switch { 0 => "Bugün", 1 => "Yarın", _ => day.Date.ToString("dddd d MMMM", Turkish) };
            return $"{label}: {WeatherCodes.Describe(day.Code)}, {Degrees(day.Min)}–{Degrees(day.Max)} derece, " +
                   $"yağış ihtimali yüzde {Degrees(day.RainChance)}" + (day.Rain >= 0.1 ? $", {day.Rain.ToString("0.#", Turkish)} mm yağış" : "");
        }

        return $"{forecast.Place}: şu an {Degrees(forecast.Temperature)} derece (hissedilen {Degrees(forecast.Feels)}), " +
               $"{WeatherCodes.Describe(forecast.Code)}, rüzgâr {Degrees(forecast.Wind)} km/sa.\n" +
               string.Join('\n', forecast.Days.Select(Day));
    }

    private string StartTimer(string minutesText, string note)
    {
        if (!double.TryParse(minutesText, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) || minutes <= 0 || minutes > 24 * 60)
        {
            return "Geçerli bir süre söylenmedi.";
        }

        // Hatırlatma kalıcıdır: Friday kapanıp açılsa da kaybolmaz. Notsuzsa "10 dakika doldu." diye söylenir.
        var now = DateTime.Now;
        reminders.Add(now.AddMinutes(minutes), note.Trim(), now);
        EnsureReminderClock();
        ledger.Record(DataFlow.Local, "Hatırlatma", $"{TurkishGrammar.Duration(minutes)} sonrasına kuruldu");
        return $"{minutes.ToString("0.#", Turkish)} dakikalık zamanlayıcı kuruldu.";
    }

    private async Task<string> SystemAsync(CancellationToken cancellationToken)
    {
        ledger.Record(DataFlow.Local, "Sistem", "İşlemci, bellek ve disk kullanımı okundu");
        return await SystemStatus.DescribeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> LinguaFlowAsync(string action, CancellationToken cancellationToken)
    {
        var start = Simplify(action) is not ("durdur" or "kapat" or "bitir");
        ledger.Record(DataFlow.Local, "LinguaFlow", start ? "Canlı çeviri başlatıldı" : "Canlı çeviri durduruldu");
        return await LinguaFlowLauncher.ToggleAsync(Settings.LinguaFlowPath, start, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonNode?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var text = await http.GetStringAsync(new Uri(url), timeout.Token).ConfigureAwait(false);
        return JsonNode.Parse(text);
    }

    private static string Round(JsonNode? value) =>
        Math.Round(value?.GetValue<double>() ?? 0).ToString(CultureInfo.InvariantCulture);

    private static void Launch(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    private static IEnumerable<string> StartMenuShortcuts()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.StartMenu })
        {
            var root = Environment.GetFolderPath(folder);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                // Kaldırma kısayolları asla açılmaz.
                if (!name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("kaldır", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>"Visual Studio Code" ile "visual studio kod" gibi söylenişleri karşılaştırılabilir yapar.</summary>
    internal static string Simplify(string text) =>
        new(text.ToLower(Turkish)
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string Argument(ToolCall call, string name) =>
        call.Arguments.TryGetValue(name, out var value) ? value : "";

    private static bool Flag(ToolCall call, string name) =>
        Simplify(Argument(call, name)) is "true" or "evet" or "1";

    private static JsonObject Tool(string name, string description, (string Name, string Type, string Description, bool Required)[] parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var parameter in parameters)
        {
            properties[parameter.Name] = new JsonObject { ["type"] = parameter.Type, ["description"] = parameter.Description };
            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required }
            }
        };
    }

    private enum VirtualKey : byte
    {
        Mute = 0xAD,
        VolumeDown = 0xAE,
        VolumeUp = 0xAF,
        Next = 0xB0,
        Previous = 0xB1,
        PlayPause = 0xB3
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
    }
}
