using Friday.Core;

namespace Friday.UnitTests;

/// <summary>Okuduklarım: kayıt, zaman aralığı, iki dilli arama, sürümler ve farklar.</summary>
public sealed class ReadingLogTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 25); // Çarşamba
    private readonly TempDirectory _temp = new();

    private ReadingLog NewLog() => new(Path.Combine(_temp.Path, "reading.json"));

    [Theory]
    [InlineData("bugün okuduğum", "2026-11-25", "2026-11-25")]
    [InlineData("dün baktığım rapor", "2026-11-24", "2026-11-24")]
    [InlineData("bu hafta neler okudum", "2026-11-23", "2026-11-25")]
    [InlineData("geçen hafta okuduğum sözleşme", "2026-11-16", "2026-11-22")]
    [InlineData("geçen hafta salı okuduğum", "2026-11-17", "2026-11-17")]
    [InlineData("pazartesi açtığım pdf", "2026-11-23", "2026-11-23")]
    [InlineData("geçen ay okuduklarım", "2026-10-01", "2026-10-31")]
    [InlineData("son 3 günde", "2026-11-23", "2026-11-25")]
    [InlineData("18 kasım", "2026-11-18", "2026-11-18")]
    public void UnderstandsWhenSomethingWasRead(string said, string from, string to)
    {
        var period = ReadingLog.Period(said, Today);
        Assert.Equal((DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture)), period);
    }

    [Fact]
    public void NoTimeMeansNoPeriod() => Assert.Null(ReadingLog.Period("okuduğum sözleşmede depozito ne kadardı", Today));

    [Theory]
    [InlineData("Teklif_v2.pdf", "teklif")]
    [InlineData("teklif v1.pdf", "teklif")]
    [InlineData("Rapor (1).docx", "rapor")]
    [InlineData("rental-agreement FINAL.pdf", "rental agreement")]
    [InlineData("Proje Planı 2026-11-20.docx", "proje planı")]
    [InlineData("sozlesme_son_guncel.pdf", "sozlesme")]
    public void VersionMarksAreIgnoredInTheName(string name, string baseName) => Assert.Equal(baseName, ReadingLog.BaseName(name));

    [Fact]
    public void ADocumentIsFoundByItsTurkishDigestEvenWhenItIsEnglish()
    {
        var log = NewLog();
        var (lease, _) = log.Touch(@"C:\Users\x\Downloads\rental-agreement.pdf", new DateTime(2026, 11, 18, 10, 0, 0), 300);
        log.SetDigest(lease, "Ankara'daki bir daire için 12 aylık kira sözleşmesi; aylık kira 2.000 dolar, depozito 2.500 dolar.",
            "kira sözleşmesi, kira, depozito, ev sahibi, lease, rent, deposit, tenant, landlord");
        var (report, _) = log.Touch(@"C:\Users\x\Documents\Q3-report.pdf", new DateTime(2026, 11, 24, 15, 0, 0), 600);
        log.SetDigest(report, "Üçüncü çeyrek satış raporu.", "satış, rapor, çeyrek, sales, revenue");

        var hits = log.Search("geçen hafta okuduğum kira sözleşmesinde depozito ne kadardı", ReadingLog.Period("geçen hafta", Today));
        Assert.Equal("rental-agreement.pdf", hits[0].Entry.Name);
        Assert.Single(hits);
        Assert.Equal("Q3-report.pdf", log.Search("okuduğum satış raporu", null)[0].Entry.Name);
        Assert.Empty(log.Search("okuduğum tatil broşürü", null));
    }

    [Fact]
    public void TimeAndReadingWordsAreNotSearchedFor() =>
        Assert.Equal(["depozito"], ReadingLog.QueryWords("geçen hafta okuduğum belgede depozito neydi"));

    [Fact]
    public void ReadingTimeAndDaysAddUp()
    {
        var log = NewLog();
        var path = @"C:\a\notes.pdf";
        log.Touch(path, new DateTime(2026, 11, 24, 9, 0, 0), 30);
        var (entry, added) = log.Touch(path, new DateTime(2026, 11, 25, 9, 0, 0), 15);
        Assert.False(added);
        Assert.Equal(45, entry.Seconds);
        Assert.Equal(2, entry.Days.Count);
        Assert.Single(log.InPeriod(Today, Today));
    }

    [Fact]
    public void AChangedFileKeepsItsPreviousVersion()
    {
        var log = NewLog();
        var (entry, _) = log.Touch(@"C:\a\offer.pdf", DateTime.Now, 60);
        Assert.True(log.StoreText(entry, "The monthly fee is 2,000 USD. Delivery takes 30 days.", new DateTime(2026, 11, 1), 100, true));
        log.SetDigest(entry, "Bir teklif.", "teklif");
        Assert.Null(log.PreviousText(entry));

        Assert.True(log.StoreText(entry, "The monthly fee is 2,300 USD. Delivery takes 30 days.", new DateTime(2026, 11, 20), 101, true));
        Assert.Contains("2,000", log.PreviousText(entry), StringComparison.Ordinal);
        Assert.Contains("2,300", log.Text(entry), StringComparison.Ordinal);
        Assert.NotNull(entry.Changed);
        Assert.Equal("", entry.Gist);
        Assert.Same(entry, log.NextToDigest());
    }

    [Fact]
    public void AnotherFileWithAVersionedNameIsTheEarlierVersion()
    {
        var log = NewLog();
        var (first, _) = log.Touch(@"C:\a\teklif_v1.pdf", new DateTime(2026, 11, 10), 60);
        var (second, _) = log.Touch(@"C:\a\teklif_v2.pdf", new DateTime(2026, 11, 20), 60);
        log.Touch(@"C:\a\fatura.pdf", new DateTime(2026, 11, 15), 60);
        Assert.Same(first, log.EarlierVersion(second));
        Assert.Null(log.EarlierVersion(first));
    }

    [Fact]
    public void ItIsSavedAndForgotten()
    {
        var file = Path.Combine(_temp.Path, "reading.json");
        var log = new ReadingLog(file);
        var (entry, _) = log.Touch(@"C:\a\plan.docx", DateTime.Now, 40);
        log.StoreText(entry, "Bir proje planı, üç aşama ve teslim tarihleri.", DateTime.Now, 10, false);
        Assert.Equal(1, new ReadingLog(file).Count);

        Assert.True(log.Forget(entry));
        Assert.Null(log.Text(entry));
        Assert.Equal(0, new ReadingLog(file).Count);
    }

    public void Dispose() => _temp.Dispose();
}

public class TextDiffTests
{
    [Fact]
    public void FindsChangedAddedAndRemovedSentences()
    {
        var before = "This lease starts on 1 December 2026. The monthly rent is 2,000 USD. Pets are not allowed in the apartment. The tenant pays for electricity.";
        var after = "This lease starts on 1 December 2026. The monthly rent is 2,300 USD. The tenant pays for electricity. The landlord pays for internet access.";
        var changes = TextDiff.Compare(before, after);
        Assert.Equal(("The monthly rent is 2,000 USD.", "The monthly rent is 2,300 USD."), Assert.Single(changes.Changed));
        Assert.Equal("The landlord pays for internet access.", Assert.Single(changes.Added));
        Assert.Equal("Pets are not allowed in the apartment.", Assert.Single(changes.Removed));
        Assert.Contains("DEĞİŞTİ", changes.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void SameTextHasNoChanges() =>
        Assert.True(TextDiff.Compare("Bir cümle burada duruyor. İkinci cümle de var.", "Bir cümle  burada duruyor.\nİkinci cümle de var.").IsEmpty);

    [Fact]
    public void NumbersWithDotsAreNotSplit() =>
        Assert.Single(TextDiff.Sentences("Kira bedeli 2.500 TL olarak belirlenmiştir ve her ay ödenir."));
}

/// <summary>Yazma köprüsü: istem, çıktı, üslup ve kapılar.</summary>
public sealed class WritingTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public void ParsesTheDraft()
    {
        var output = Writing.Parse("LANG: İngilizce\nTEXT:\nHi Sarah,\n\nThursday works for me, but after 3 pm.\n\nBest,\nKürşat\nTR: Perşembe uygun ama saat üçten sonra diyor.", "");
        Assert.Equal("İngilizce", output.Language);
        Assert.StartsWith("Hi Sarah,", output.Text, StringComparison.Ordinal);
        Assert.EndsWith("Kürşat", output.Text, StringComparison.Ordinal);
        Assert.Equal("Perşembe uygun ama saat üçten sonra diyor.", output.Turkish);
    }

    [Theory]
    [InlineData("Deutsch", "Almanca")]
    [InlineData("English", "İngilizce")]
    [InlineData("Français", "Fransızca")]
    [InlineData("İngilizce", "İngilizce")]
    public void TheLanguageIsNamedInTurkish(string said, string name) => Assert.Equal(name, Writing.Parse($"LANG: {said}\nTEXT:\nHallo\nTR: Merhaba", "").Language);

    [Fact]
    public void AnUnformattedAnswerIsTheDraftItself()
    {
        var output = Writing.Parse("Could we move the meeting to tomorrow?", "İngilizce");
        Assert.Equal("Could we move the meeting to tomorrow?", output.Text);
        Assert.Equal("İngilizce", output.Language);
    }

    [Fact]
    public void TheInstructionCarriesLanguageSignatureAndStyle()
    {
        var instruction = Writing.Instruction("İngilizce", "Kürşat", "Best regards,\nKürşat", ["The user prefers short, to-the-point messages; keep it brief."]);
        Assert.Contains("Write it in İngilizce", instruction, StringComparison.Ordinal);
        Assert.Contains("Best regards,", instruction, StringComparison.Ordinal);
        Assert.Contains("keep it brief", instruction, StringComparison.Ordinal);
        Assert.Contains("same language as the message", Writing.Instruction("", "", "", []), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("kısalt", "kisa")]
    [InlineData("daha kısa yap", "kisa")]
    [InlineData("daha resmi yap", "resmi")]
    [InlineData("biraz daha samimi olsun", "samimi")]
    [InlineData("daha kibar yap", "kibar")]
    [InlineData("şunu da ekle: cuma da olur", "")]
    public void KnowsWhatKindOfChangeWasAsked(string change, string kind) => Assert.Equal(kind, Writing.Kind(change));

    [Fact]
    public void LearnsAPreferenceAfterTheSameChangeTwiceAndForgetsItForTheOpposite()
    {
        var file = Path.Combine(_temp.Path, "writing-style.json");
        var style = new WritingStyle(file);
        Assert.False(style.Learn("kisa"));
        Assert.True(style.Learn("kisa"));
        Assert.Single(new WritingStyle(file).Notes);
        Assert.Equal(["kısa"], style.Learned);
        style.Learn("uzun");
        Assert.Empty(style.Notes);
    }

    public void Dispose() => _temp.Dispose();
}

public class ReadingAndWritingIntentTests
{
    [Theory]
    [InlineData("bu hafta neler okudum", "liste", "bu hafta")]
    [InlineData("dün hangi belgelere baktım", "liste", "dün")]
    [InlineData("okuduklarım", "liste", "")]
    public void ListsWhatWasRead(string said, string action, string when)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("okuduklarim", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(when, command.Arguments.GetValueOrDefault("zaman", ""));
    }

    [Theory]
    [InlineData("geçen hafta okuduğum sözleşmede depozito ne kadardı", "sor")]
    [InlineData("dün baktığım pdf'te teslim tarihi neydi", "sor")]
    [InlineData("pazartesi okuduğum raporu aç", "ac")]
    [InlineData("bunun öncekinden farkı ne", "fark")]
    [InlineData("bu belgede neler değişmiş", "fark")]
    [InlineData("okuduklarımı unut", "unut")]
    public void UnderstandsQuestionsAboutWhatWasRead(string said, string action)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("okuduklarim", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
    }

    [Theory]
    [InlineData("az önce açtığım pencereyi kapat")]
    [InlineData("baktığım şey neydi")]
    public void OpeningSomethingIsNotReading(string said) => Assert.NotEqual("okuduklarim", QuickIntents.Match(said)?.Tool);

    [Theory]
    [InlineData("Buna cevap yaz: perşembe olur ama saat üçten sonra. Sunumu da getiririm.", "cevap", "perşembe olur ama saat üçten sonra. Sunumu da getiririm.", "")]
    [InlineData("bu maile İngilizce cevap yaz: teşekkürler, belgeleri yarın gönderirim", "cevap", "teşekkürler, belgeleri yarın gönderirim", "İngilizce")]
    [InlineData("şunu İngilizce yaz: toplantıyı yarına alabilir miyiz", "yaz", "toplantıyı yarına alabilir miyiz", "İngilizce")]
    [InlineData("Friday, şunu Almancaya çevir: siparişim ne zaman gelir", "yaz", "siparişim ne zaman gelir", "Almanca")]
    public void WritesWhatIsSaid(string said, string action, string content, string language)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("yazi", command?.Tool);
        Assert.Equal(action, command!.Arguments["islem"]);
        Assert.Equal(content, command.Arguments["icerik"]);
        Assert.Equal(language, command.Arguments.GetValueOrDefault("dil", ""));
    }

    [Theory]
    [InlineData("daha kibar yap")]
    [InlineData("kısalt")]
    [InlineData("biraz daha resmi yaz")]
    [InlineData("şunu da ekle: cuma da olur")]
    public void RevisesTheDraft(string said)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("yazi", command?.Tool);
        Assert.Equal("duzelt", command!.Arguments["islem"]);
    }
}
