using Friday.Infrastructure;

namespace Friday.UnitTests;

/// <summary>
/// Doğal söylenişlerde dil modeline doğru aracın gidip gitmediği: yönlendirici bir aracı göndermezse model onu
/// çağıramaz, ya uydurur ya da "yapamıyorum" deyip bütün araçlarla yeniden denenir (yavaş).
/// </summary>
public class RoutingCoverageTests
{
    [Theory]
    [InlineData("geçen haftaki market masraflarım ne kadar tuttu", "harcama")]
    [InlineData("geçen hafta tatil hakkında ne konuşmuştuk", "gecmis")]
    [InlineData("bana önerdiğin dizinin adı neydi", "gecmis")]
    [InlineData("kredi kartı ekstremi ne zaman ödenecek", "sabit_gider")]
    [InlineData("sterlin bugün ne alemde", "doviz")]
    [InlineData("kitap okuma serimi bozdum mu", "aliskanlik")]
    [InlineData("bana birkaç İngilizce kelime sor", "kelime")]
    [InlineData("bugünkü ajandamda neler var", "takvim")]
    [InlineData("önümüzdeki günlerde hatırlamam gereken özel tarihler var mı", "ozel_gunler")]
    [InlineData("annemin doğum gününe kaç gün kaldı", "gun_sayaci")]
    [InlineData("yirmi beş dakikalık bir çalışma seansı başlat", "pomodoro")]
    [InlineData("biraz mola versem iyi olur mu", "ritim")]
    [InlineData("şu çalan parçayı geç", "medya")]
    [InlineData("geçen ay indirdiğim faturayı bul", "dosya_bul")]
    [InlineData("kira kontratında depozito ne kadardı", "belgelerde_ara")]
    [InlineData("bilgisayar neden bu kadar kasıyor", "sistem_durumu")]
    [InlineData("modem bağlantım koptu galiba", "baglanti")]
    [InlineData("bugün gündemde neler var", "haberler")]
    [InlineData("akşam rutinimi başlat", "rutin")]
    [InlineData("yumurta için on dakika say", "zamanlayici")]
    [InlineData("bu gece kaçta yatmam lazım", "uyku_saati")]
    [InlineData("hocama izin isteyen bir mail yaz", "eposta_taslagi")]
    [InlineData("alışveriş listeme deterjan koy", "liste")]
    [InlineData("bugün kendimi çok yorgun hissediyorum", "ruh_hali")]
    [InlineData("bu hafta nasıl geçti özetle", "haftalik_ozet")]
    [InlineData("bir saat kimse beni rahatsız etmesin", "rahatsiz_etme")]
    [InlineData("instagramda günde en fazla yarım saat kalayım", "sure_siniri")]
    [InlineData("bu makaleyi sonra okumak için kaydet", "sonra_oku")]
    [InlineData("ben ortalıkta yokken neler söyledin", "kacirdiklarim")]
    [InlineData("canım sıkılıyor ne yapsam", "ne_yapayim")]
    [InlineData("elektrikli arabaları detaylıca araştır", "arastirma_raporu")]
    [InlineData("az önce eklediğin şeyi geri alır mısın", "geri_al")]
    [InlineData("geçen perşembe neler yaptım", "o_gun")]
    [InlineData("panodaki resimde ne yazıyor", "pano_gorseli")]
    [InlineData("spotifyın sesini biraz kıs", "uygulama_sesi")]
    [InlineData("imzamı buraya yaz", "kisaltma")]
    [InlineData("günlüğüme bugün terfi aldığımı yaz", "gunluk")]
    [InlineData("toplantıda konuşulanları canlı çevir", "linguaflow")]
    [InlineData("ekranda yazanları kopyala", "ekran_metni")]
    [InlineData("euro kırk lirayı geçince haber ver", "doviz_alarmi")]
    [InlineData("haftalık raporumu dosyaya yaz", "haftalik_rapor")]
    [InlineData("rapora odaklanmam lazım kırk dakika", "isle_odak")]
    [InlineData("youtubeda lofi müzik aç", "video_ac")]
    [InlineData("çalan şarkıyı listeme kaydet", "sarki_kaydet")]
    [InlineData("diskimde ne çok yer kaplıyor", "disk_analizi")]
    [InlineData("gereksiz dosyaları temizle", "temizlik")]
    [InlineData("işlemci ısınırsa beni uyar", "nobet")]
    [InlineData("pencereyi sağa yasla", "pencere")]
    [InlineData("çalışma masamı kaydet", "masa_duzeni")]
    [InlineData("bugün ekranda ne kadar zaman geçirdim", "ekran_suresi")]
    [InlineData("tatil hazırlık kontrol listesini aç", "kontrol_listesi")]
    [InlineData("bu sayfayı oku ve özetle", "sayfayi_oku")]
    [InlineData("her sabah dokuzda derin çalışma rutinini başlat", "rutin_zamanla")]
    [InlineData("bugün kaç bardak su içtim", "ritim")]
    [InlineData("Einstein kimdir", "bilgi")]
    [InlineData("şu seçili kelimenin anlamı ne", "kelime_cevir")]
    [InlineData("panoya kopyaladığım şeyi not olarak sakla", "pano_kaydet")]
    [InlineData("bu fişi harcamalara işle", "fis")]
    public void SendsTheRightTool(string question, string tool)
    {
        var selected = ToolRouter.Select(question, everything: false);
        Assert.NotNull(selected);
        Assert.Contains(tool, selected);
    }
}
