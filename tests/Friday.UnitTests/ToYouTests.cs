using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Kullanıcının ağzından söylenen hatırlatma metninin Friday'in ağzına çevrilmesi.</summary>
public class ToYouTests
{
    [Theory]
    [InlineData("dişçiyi aramamı", "dişçiyi aramanı")]
    [InlineData("akşam ilacımı", "akşam ilacını")]
    [InlineData("ilaçlarımı", "ilaçlarını")]
    [InlineData("annemi ara", "anneni ara")]
    [InlineData("annemin doğum günü", "annenin doğum günü")]
    [InlineData("oğlumu okuldan al", "oğlunu okuldan al")]
    [InlineData("kızımın piyano dersi", "kızının piyano dersi")]
    [InlineData("eşime çiçek al", "eşine çiçek al")]
    [InlineData("faturalarımı öde", "faturalarını öde")]
    [InlineData("çamaşırları asmamı", "çamaşırları asmanı")]
    [InlineData("Babamı ara", "Babanı ara")]
    [InlineData("dişçiye gitmem gerektiğini", "dişçiye gitmen gerektiğini")]
    [InlineData("annemi aramam lazım", "anneni araman lazım")]
    public void SpeaksToTheUser(string text, string expected) => Assert.Equal(expected, TurkishGrammar.ToYou(text));

    [Theory]
    // "kalemi" (kalem+i), "programı", "tamamı", "dedim", "kızma" iyelik değildir.
    [InlineData("kalemi al")]
    [InlineData("programı kontrol et")]
    [InlineData("tamamı ödendi")]
    [InlineData("Ahmet'i ara")]
    [InlineData("toplantıyı unutma")]
    [InlineData("evrakları getir")]
    [InlineData("eşyaları topla")]
    public void LeavesOtherWordsAlone(string text) => Assert.Equal(text, TurkishGrammar.ToYou(text));

    [Fact]
    public void TheReminderIsAnnouncedInTheSecondPerson()
    {
        var now = new DateTime(2026, 11, 21, 9, 0, 0);
        var reminder = new Reminder("test", now.AddHours(1), "dişçiyi aramamı", now);
        Assert.Equal("Dişçiyi aramanı unutma, zamanı geldi.", ReminderStore.Announcement(reminder));
    }
}
