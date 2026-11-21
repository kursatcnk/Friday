using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// Hafıza büyüdükçe her soruya yalnızca ilgili bilgiler eklenir. Bilgi bir sözcüklerle kaydedilip başka sözcüklerle
/// sorulduğunda ("İşyerim Maslak'ta" ↔ "nerede çalışıyorum") da bulunmalı.
/// </summary>
public class MemoryRecallCoverageTests
{
    private static readonly string[] Facts =
    [
        "Kızımın adı Ekinsu, 9 yaşında.", "Arabam 2019 model Toyota Corolla.", "Doktorum Dr. Selim, Acıbadem'de.",
        "Annemin doğum günü 3 Mart.", "Kan grubum A Rh pozitif.", "Spor salonu üyeliğim Mart'ta bitiyor.",
        "Ev sahibim Nuri Bey, kira 25 bin lira.", "İşyerim Maslak'ta.", "Patronum Ilgaz Bey.", "En sevdiğim yemek mantı.",
        "Fıstığa alerjim var.", "Eşimin adı Deniz.", "Oğlum Doruk futbol kursuna gidiyor.", "Kedimin adı Pamuk.",
        "Wi-Fi modemim salonda.", "Pasaportum 2028'de bitiyor.", "Bankam Ziraat.", "Diş hekimim Ayşe Hanım.",
        "Sabahları kahve yerine çay içerim.", "Gözlük numaram eksi iki.", "İngilizce seviyem B2.", "Vegan değilim ama kırmızı et yemem.",
        "Kardeşim Kerem Ankara'da yaşıyor.", "Evlilik yıldönümümüz 12 Haziran.", "Arabanın sigortası Nisan'da yenilenecek.",
        "Laptopum Lenovo ThinkPad.", "Kulaklık numaram 44.", "Ayakkabı numaram 42.", "Okuduğum son kitap Suç ve Ceza.", "Hobim fotoğrafçılık."
    ];

    private static readonly IReadOnlyList<MemoryItem> Memories =
        [.. Facts.Select((fact, index) => new MemoryItem($"m{index}", fact, DateTimeOffset.Now.AddDays(-index)))];

    [Theory]
    [InlineData("kızım kaç yaşında", "Kızımın adı Ekinsu")]
    [InlineData("arabam ne model", "Arabam 2019 model")]
    [InlineData("doktorum kimdi", "Doktorum Dr. Selim")]
    [InlineData("annemin doğum günü ne zaman", "Annemin doğum günü")]
    [InlineData("kan grubum ne", "Kan grubum")]
    [InlineData("kiramı kime ödüyorum", "Ev sahibim Nuri Bey")]
    [InlineData("nerede çalışıyorum", "İşyerim Maslak")]
    [InlineData("neye alerjim var", "Fıstığa alerjim")]
    [InlineData("en sevdiğim yemek ne", "En sevdiğim yemek")]
    [InlineData("eşimin adı ne", "Eşimin adı Deniz")]
    [InlineData("kedimin adı neydi", "Kedimin adı Pamuk")]
    [InlineData("pasaportumun süresi ne zaman doluyor", "Pasaportum 2028")]
    [InlineData("hangi bankayı kullanıyorum", "Bankam Ziraat")]
    [InlineData("dişçim kimdi", "Diş hekimim")]
    [InlineData("sabah ne içerim", "çay içerim")]
    [InlineData("kardeşim nerede oturuyor", "Kardeşim Kerem")]
    [InlineData("evlilik yıldönümümüz ne zaman", "Evlilik yıldönümümüz")]
    [InlineData("hangi bilgisayarı kullanıyorum", "Laptopum Lenovo")]
    [InlineData("ayak numaram kaç", "Ayakkabı numaram")]
    [InlineData("boş zamanlarımda ne yaparım", "Hobim fotoğrafçılık")]
    public void FindsTheFactForAParaphrasedQuestion(string question, string expected)
    {
        var found = MemoryRecall.Relevant(Memories, question, new HashSet<MemoryItem>());
        Assert.Contains(found, item => item.Text.Contains(expected, StringComparison.Ordinal));
    }
}
