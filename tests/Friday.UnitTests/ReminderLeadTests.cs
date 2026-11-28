using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// "Yarın 9'da dişçi var, bir saat önce hatırlat" hatırlatmayı 08:00'e kurar; "dişçiye ne zaman gidiyordum" kayıtlı
/// hatırlatmadan cevaplanır (model bir ölçümde durum satırındaki şimdiki saati söyledi).
/// </summary>
public class ReminderLeadTests
{
    [Theory]
    [InlineData("dişçi var, bir saat önce", 60, "dişçi")]
    [InlineData("toplantı, 15 dakika önceden", 15, "toplantı")]
    [InlineData("annemin doğum günü, iki gün önce", 2880, "annemin doğum günü")]
    public void ReadsTheLeadTime(string text, double minutes, string rest)
    {
        var found = ReminderStore.LeadTime(text);
        Assert.Equal((TimeSpan.FromMinutes(minutes), rest), found);
    }

    [Fact]
    public void LeavesPlainTextAlone() => Assert.Null(ReminderStore.LeadTime("annemi ara"));

    [Theory]
    [InlineData("dişçiye ne zaman gidiyordum", "dişçi")]
    [InlineData("Mert'i ne zaman arayacaktım", "Mert")]
    [InlineData("toplantı ne zamandı", "toplantı")]
    public void AsksWhenSomethingIs(string sentence, string what)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("ne_zaman", command?.Arguments["islem"]);
        Assert.Equal(what, command!.Arguments["metin"]);
    }

    [Fact]
    public void FindsTheNearestMatchingReminder()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "friday-find-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTime(2026, 11, 28, 14, 0, 0);
            var store = new ReminderStore(Path.Combine(folder, "reminders.json"));
            store.Add(now.AddDays(1).Date.AddHours(9), "Dişçi randevusu", now);
            store.Add(now.AddDays(8), "Dişçi kontrolü", now);
            Assert.Equal("Dişçi randevusu", store.Find("dişçi", now)?.Text);
            Assert.Null(store.Find("kuaför", now));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
