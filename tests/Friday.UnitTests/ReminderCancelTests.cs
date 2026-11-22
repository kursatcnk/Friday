using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Hatırlatma silmenin söyleyişleri ve hangi hatırlatmanın silineceğinin bulunması.</summary>
public class ReminderCancelTests
{
    [Theory]
    [InlineData("dişçi hatırlatmasını sil", "dişçi")]
    [InlineData("dişçi hatırlatmasını iptal et", "dişçi")]
    [InlineData("dişçiyle ilgili hatırlatmayı sil", "dişçi")]
    [InlineData("dişçi hatırlatmasına gerek yok", "dişçi")]
    [InlineData("dişçi hatırlatmasını unut", "dişçi")]
    [InlineData("annemi arama hatırlatmasını kaldır", "annemi arama")]
    public void UnderstandsCancelling(string said, string which)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("hatirlatici", command?.Tool);
        Assert.Equal("sil", command!.Arguments["islem"]);
        Assert.Equal(which, command.Arguments["metin"]);
    }

    [Fact]
    public void FindsTheReminderByWordStems()
    {
        var folder = Path.Combine(Path.GetTempPath(), "friday-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTime(2026, 11, 21, 14, 0, 0);
            var store = new ReminderStore(Path.Combine(folder, "reminders.json"));
            store.Add(now.AddDays(1), "dişçiyi aramamı", now);
            store.Add(now.AddDays(1), "annemi aramayı", now);
            Assert.Equal(1, store.Cancel("dişçi"));
            Assert.Equal(1, store.Cancel("annemi arama"));
            Assert.Equal(0, store.Cancel("kira"));
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
