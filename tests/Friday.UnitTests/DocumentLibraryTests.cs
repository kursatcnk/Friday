using Friday.Infrastructure;

namespace Friday.UnitTests;

/// <summary>Belge hafızası taraması: metni çıkmayan dosyalar her taramada baştan okunmaz.</summary>
public sealed class DocumentLibraryTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public async Task ATextlessFileIsRememberedUntilItChanges()
    {
        var folder = Path.Combine(_temp.Path, "docs");
        var data = Path.Combine(_temp.Path, "data");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(data);
        var blank = Path.Combine(folder, "taranmis.txt");
        await File.WriteAllTextAsync(blank, "   \n  \n");
        await File.WriteAllTextAsync(Path.Combine(folder, "kira.txt"), "Kira sözleşmesi: depozito 2.500 lira, sözleşme 31 Mart 2027'de bitiyor.");
        using var library = new DocumentLibrary(data) { Folders = [folder] };

        await library.ScanAsync();
        var skipped = await File.ReadAllTextAsync(Path.Combine(data, "documents-skipped.json"));
        Assert.Contains("taranmis.txt", skipped, StringComparison.Ordinal);
        Assert.NotEmpty(library.Search("depozito ne kadar"));

        // Dosya değişince yeniden okunur ve aranabilir olur.
        await File.WriteAllTextAsync(blank, "Toplantı notu: bütçe 1,2 milyon lira olarak onaylandı.");
        File.SetLastWriteTimeUtc(blank, DateTime.UtcNow.AddMinutes(1));
        await library.ScanAsync();
        Assert.DoesNotContain("taranmis.txt", await File.ReadAllTextAsync(Path.Combine(data, "documents-skipped.json")), StringComparison.Ordinal);
        Assert.Contains(library.Search("bütçe onaylandı"), hit => hit.Path.EndsWith("taranmis.txt", StringComparison.Ordinal));
    }

    public void Dispose() => _temp.Dispose();
}
