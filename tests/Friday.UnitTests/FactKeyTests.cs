using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// Hafızada güncellenen bilgiler: aynı kişinin aynı tek değerli özelliği (telefon, adres, okul, işyeri…) yeni bir
/// değerle söylenince eskisinin yerine geçmeli; başka kişinin ya da başka bir özelliğin bilgisi karışmamalı.
/// </summary>
public class FactKeyTests
{
    public static TheoryData<string, string> Updates => new()
    {
        { "Ayşe'nin telefon numarası 0532 111 22 33.", "Ayşe'nin yeni numarası 0533 444 55 66." },
        { "Kullanıcı Maslak'ta çalışıyor.", "Kullanıcı artık Levent'te çalışıyor." },
        { "İşyerim Maslak'ta.", "Kullanıcının işyeri Levent'e taşındı." },
        { "Kullanıcının kızının okulu Kadıköy'de.", "Kullanıcının kızı artık Ataşehir'deki okula gidiyor." },
        { "Kullanıcının arabasının muayenesi 20 Kasım'da.", "Arabanın muayenesi 3 Aralık'a ertelendi." },
        { "Kullanıcının adresi Kadıköy Moda Caddesi 12.", "Kullanıcı Ataşehir'e taşındı." },
        { "Kullanıcı Kadıköy'de oturuyor.", "Kullanıcı artık Ataşehir'de yaşıyor." },
        { "Kullanıcının maaşı 60 bin lira.", "Kullanıcının maaşı 75 bine çıktı." },
        { "Kullanıcının kirası 25 bin lira.", "Kira 30 bin liraya yükseldi." },
        { "Doruk 9 yaşında.", "Doruk artık 10 yaşında." },
        { "Ekinsu'nun piyano öğretmeni Nermin Hanım.", "Ekinsu'nun yeni piyano öğretmeni Selma Hanım." },
        { "Kullanıcının doktoru Dr. Ahmet Yılmaz.", "Kullanıcının yeni doktoru Dr. Ayşe Kaya." },
        { "Selin'in doğum günü 3 Mart.", "Selin'in doğum günü aslında 5 Mart." },
        { "Kullanıcının e-posta adresi ali@ornek.com.", "Kullanıcının yeni e-posta adresi ali.yeni@ornek.com." },
        { "Arabanın plakası 34 ABC 123.", "Arabanın yeni plakası 34 XYZ 99." },
        { "Kullanıcının boyu 1.80.", "Kullanıcının boyu 182 santim." },
        { "Kızının ayakkabı numarası 32.", "Kızının ayakkabı numarası artık 33." },
        { "Kullanıcının patronu Ilgaz Bey.", "Kullanıcının yeni patronu Murat Bey." },
        { "Kullanıcı Bursa'da yaşıyor.", "Kullanıcı İstanbul'a taşındı." },
        { "Annem Kadıköy'de oturuyor.", "Annem artık Bostancı'da oturuyor." },
    };

    public static TheoryData<string, string> Distinct => new()
    {
        { "Ayşe'nin telefon numarası 0532 111 22 33.", "Ayşe'nin adresi Kadıköy." },
        { "Kızının doğum günü 3 Mart.", "Oğlunun doğum günü 5 Nisan." },
        { "Kızının okulu Kadıköy'de.", "Oğlunun okulu Ataşehir'de." },
        { "Kullanıcının hobisi resim.", "Kullanıcının hobisi gitar." },
        { "Kullanıcı fıstığa alerjik.", "Kullanıcı kedilere alerjik." },
        { "Kullanıcının eski adresi Kadıköy.", "Kullanıcının adresi Ataşehir." },
        { "Kullanıcının arabasının plakası 34 ABC 123.", "Eşinin arabasının plakası 06 XY 45." },
        { "Ekinsu 9 yaşında.", "Doruk 6 yaşında." },
        { "Kızının ayakkabı numarası 32.", "Kızının telefon numarası 0555 000 11 22." },
        { "Kullanıcı Maslak'ta çalışıyor.", "Eşi Levent'te çalışıyor." },
        { "Kullanıcının doktoru Dr. Ahmet.", "Kızının doktoru Dr. Selin." },
        { "Kahveyi şekersiz içer.", "Çayı şekersiz içer." },
        { "Patronu Ilgaz Bey.", "Müdürü Murat Bey." },
        { "Kullanıcının Maslak'ta toplantısı var.", "Kullanıcının Levent'te toplantısı var." },
        { "Kullanıcı 5 kilometre koşuyor.", "Kullanıcı 10 kilo verdi." },
        { "Kullanıcının adı Kürşat.", "Kullanıcının kızının adı Ekinsu." },
        { "Ayşe Ataşehir'de oturuyor.", "Kullanıcı Ataşehir'de oturuyor." },
        { "Kullanıcının kızının adı Ekinsu.", "Kullanıcının oğlunun adı Doruk." },
        { "Maaş günü ayın 15'i.", "Kullanıcının maaşı 60 bin." },
        { "Kullanıcı Maslak'ta çalışıyordu.", "Kullanıcı Levent'te çalışıyor." },
    };

    [Theory]
    [MemberData(nameof(Updates))]
    public void AnUpdateReplacesTheOldFact(string older, string newer) =>
        Assert.True(FactKey.Same(older, newer) || MemoryStore.SameFact(older, newer), $"{FactKey.Of(older)} / {FactKey.Of(newer)}");

    [Theory]
    [MemberData(nameof(Distinct))]
    public void OtherFactsStayApart(string first, string second) =>
        Assert.False(FactKey.Same(first, second) || (MemoryStore.SameFact(first, second) && !FactKey.Differ(first, second)), $"{FactKey.Of(first)} / {FactKey.Of(second)}");

    [Fact]
    public void TheOldRuleAloneMissedMostUpdates()
    {
        // Ölçüm: eski kural (sözcüklerin %60'ı ortak) 20 güncellemenin yalnızca birkaçını tanıyordu.
        var old = Updates.Count(row => MemoryStore.SameFact((string)row[0], (string)row[1]));
        Assert.True(old <= 5, $"{old}/20");
    }

    [Fact]
    public void TheStoreReplacesAnUpdatedFactAndSaysWhatChanged()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "friday-factkey-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MemoryStore(Path.Combine(folder, "memory.json"));
            store.Remember("Ayşe'nin telefon numarası 0532 111 22 33.");
            store.Remember("Kullanıcının kızının adı Ekinsu.");
            var saved = store.Remember("Ayşe'nin yeni numarası 0533 444 55 66.", out var replaced);
            Assert.Equal("Ayşe'nin telefon numarası 0532 111 22 33.", replaced);
            Assert.Equal(2, store.Items.Count);
            Assert.Contains(store.Items, item => item.Text == saved.Text);
            Assert.DoesNotContain(store.Items, item => item.Text.Contains("0532", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
