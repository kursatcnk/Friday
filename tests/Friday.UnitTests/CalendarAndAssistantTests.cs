using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public class CalendarTests
{
    private static string Ics(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Concat(events.Select(body => $"BEGIN:VEVENT\r\n{body}\r\nEND:VEVENT\r\n")) + "END:VCALENDAR\r\n";

    private static readonly DateTime Monday = new(2026, 11, 16);

    [Fact]
    public void ReadsLocalAllDayAndUtcEvents()
    {
        var ics = Ics(
            "DTSTART:20261118T090000\r\nDTEND:20261118T100000\r\nSUMMARY:Haftalık toplantı\r\nLOCATION:Ofis\\, 3. kat",
            "DTSTART;VALUE=DATE:20261119\r\nSUMMARY:Annemin doğum günü",
            $"DTSTART:{new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Local).ToUniversalTime():yyyyMMdd'T'HHmmss}Z\r\nSUMMARY:Öğle yemeği");
        var events = Calendar.Parse(ics, Monday, Monday.AddDays(7));

        Assert.Equal(["Haftalık toplantı", "Öğle yemeği", "Annemin doğum günü"], events.Select(item => item.Title));
        Assert.Equal("Ofis, 3. kat", events[0].Location);
        Assert.Equal(new DateTime(2026, 11, 18, 12, 0, 0), events[1].Start);
        Assert.True(events[2].AllDay);
        Assert.Equal(new DateTime(2026, 11, 20), events[2].End);
    }

    [Fact]
    public void ExpandsWeeklyRulesWithDaysAndExceptions()
    {
        var ics = Ics("DTSTART:20261102T093000\r\nDTEND:20261102T094500\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,WE\r\nEXDATE:20261118T093000\r\nSUMMARY:Ayakta toplantı");
        var events = Calendar.Parse(ics, Monday, Monday.AddDays(7));
        Assert.Equal([new DateTime(2026, 11, 16, 9, 30, 0)], events.Select(item => item.Start));
    }

    [Fact]
    public void FindsTodayInAnOldDailySeries()
    {
        var ics = Ics("DTSTART:20200101T080000\r\nRRULE:FREQ=DAILY\r\nSUMMARY:Spor");
        var events = Calendar.Parse(ics, Monday, Monday.AddDays(2));
        Assert.Equal([Monday.AddHours(8), Monday.AddDays(1).AddHours(8)], events.Select(item => item.Start));
    }

    [Fact]
    public void RespectsCountUntilAndCancellation()
    {
        var ics = Ics(
            "DTSTART:20261116T100000\r\nRRULE:FREQ=DAILY;COUNT=2\r\nSUMMARY:İki gün",
            "DTSTART:20261116T110000\r\nRRULE:FREQ=DAILY;UNTIL=20261117T235959Z\r\nSUMMARY:Salıya kadar",
            "DTSTART:20261116T120000\r\nSTATUS:CANCELLED\r\nSUMMARY:İptal");
        var events = Calendar.Parse(ics, Monday, Monday.AddDays(7));
        Assert.Equal(2, events.Count(item => item.Title == "İki gün"));
        Assert.Equal(2, events.Count(item => item.Title == "Salıya kadar"));
        Assert.DoesNotContain(events, item => item.Title == "İptal");
    }

    [Fact]
    public void UnfoldsLongLines()
    {
        var ics = Ics("DTSTART:20261118T090000\r\nSUMMARY:Çok uzun bir toplantı\r\n  başlığı");
        Assert.Equal("Çok uzun bir toplantı başlığı", Assert.Single(Calendar.Parse(ics, Monday, Monday.AddDays(7))).Title);
    }

    [Fact]
    public void DescribesEvents()
    {
        var now = new DateTime(2026, 11, 18, 8, 0, 0);
        Assert.Equal("yarın tüm gün", Calendar.Describe(new CalendarEvent("Doğum günü", now.Date.AddDays(1), now.Date.AddDays(2), true, null), now));
        Assert.StartsWith("bugün 10:00", Calendar.Describe(new CalendarEvent("Toplantı", now.AddHours(2), now.AddHours(3), false, null), now),
            StringComparison.Ordinal);
    }
}

public class DownloadSorterTests
{
    [Fact]
    public void PlansByTypeAndLeavesTheRest()
    {
        var now = new DateTime(2026, 11, 18, 12, 0, 0);
        var old = now.AddDays(-2);
        var plan = DownloadSorter.Plan(@"D:\İndirilenler",
        [
            (@"D:\İndirilenler\fatura.pdf", old),
            (@"D:\İndirilenler\tatil.JPG", old),
            (@"D:\İndirilenler\kurulum.exe", old),
            (@"D:\İndirilenler\film.mkv.crdownload", old),
            (@"D:\İndirilenler\yeni.pdf", now.AddSeconds(-30)),
            (@"D:\İndirilenler\gizemli.xyz", old),
        ], now);

        Assert.Equal(
            [@"D:\İndirilenler\Belgeler\fatura.pdf", @"D:\İndirilenler\Kurulumlar\kurulum.exe", @"D:\İndirilenler\Resimler\tatil.JPG"],
            plan.Select(move => move.To));
        Assert.Equal("1 belge, 1 kurulum dosyası ve 1 resim", DownloadSorter.Summarize(plan));
    }

    [Fact]
    public void AvoidsNameClashesWithinThePlan()
    {
        var old = new DateTime(2026, 1, 1);
        var plan = DownloadSorter.Plan(@"D:\İndirilenler", [(@"D:\İndirilenler\a.pdf", old), (@"D:\İndirilenler\A.PDF", old)], new DateTime(2026, 11, 18));
        Assert.Equal(2, plan.Select(move => move.To.ToLowerInvariant()).Distinct().Count());
    }
}

public class AssistantIntentTests
{
    [Theory]
    [InlineData("yapay zekâ ajanları hakkında rapor hazırla", "yapay zekâ ajanları")]
    [InlineData("elektrikli arabaları araştır", "elektrikli arabalar")]
    [InlineData("Rust dili ile ilgili araştırma yap", "rust dili")]
    public void StartsResearch(string sentence, string topic)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("rapor", command?.Tool);
        Assert.Equal(topic, command!.Arguments["konu"]);
    }

    [Theory]
    [InlineData("bunu internette araştır")]
    [InlineData("şunu araştır")]
    public void PlainSearchIsNotAReport(string sentence) => Assert.NotEqual("rapor", QuickIntents.Match(sentence)?.Tool);

    [Fact]
    public void OpensTheLastReport() => Assert.Equal("ac", QuickIntents.Match("raporu aç")?.Arguments["islem"]);

    [Theory]
    [InlineData("bugün neler var", "bugun")]
    [InlineData("yarın programım ne", "yarin")]
    [InlineData("bu hafta takvimimde ne var", "hafta")]
    [InlineData("toplantım var mı", "bugun")]
    [InlineData("sıradaki toplantım ne", "sonraki")]
    public void AsksTheCalendar(string sentence, string day)
    {
        var command = QuickIntents.Match(sentence);
        Assert.Equal("takvim", command?.Tool);
        Assert.Equal(day, command!.Arguments["gun"]);
    }

    [Fact]
    public void ABareWhatsUpIsNotTheCalendar() => Assert.NotEqual("takvim", QuickIntents.Match("ne var")?.Tool);

    [Theory]
    [InlineData("indirilenleri düzenle", "plan")]
    [InlineData("düzenlemeyi geri al", "geri_al")]
    public void SortsDownloads(string sentence, string action) => Assert.Equal(action, QuickIntents.Match(sentence)?.Arguments["islem"]);

    [Theory]
    [InlineData("günü kapat")]
    [InlineData("günün özetini çıkar")]
    public void EndsTheDay(string sentence) => Assert.Equal("gun_sonu", QuickIntents.Match(sentence)?.Tool);

    [Theory]
    [InlineData("Evet.", true, false)]
    [InlineData("tamam yap", true, false)]
    [InlineData("vazgeç", false, true)]
    [InlineData("yarın hava nasıl", false, false)]
    public void RecognisesYesAndNo(string sentence, bool yes, bool no)
    {
        Assert.Equal(yes, QuickIntents.IsYes(sentence));
        Assert.Equal(no, QuickIntents.IsNo(sentence));
    }

    [Fact]
    public void SummarisesAReport()
    {
        const string report = "## Özet\nElektrikli araçlar hızla yaygınlaşıyor [1]. Fiyatlar düşüyor. Menzil artıyor.\n\n## Pazar\n- Büyüme %30";
        Assert.Equal("Elektrikli araçlar hızla yaygınlaşıyor. Fiyatlar düşüyor.", AssistantTools.Summary(report));
        Assert.Equal("yapay-zeka-ajanlari", AssistantTools.Slug("Yapay Zekâ Ajanları"));
        Assert.Equal("## Özet\nMetin.", AssistantTools.WithoutTitle("# Konu Raporu\n\n## Özet\nMetin."));
    }
}
