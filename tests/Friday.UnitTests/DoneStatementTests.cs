using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// "Yaptım" cümlelerinin açık işleri kapatması. Ölçüm: 12 gündelik söyleyişten yalnızca "raporu bitirdim" bir iş
/// kapatıyordu; "annemi aradım", "mail attım" dil modeline gidiyor, "sütü aldım" alışkanlık sanılıp boşa düşüyordu.
/// </summary>
public class DoneStatementTests
{
    [Theory]
    [InlineData("annemi aradım", "Annemi ara")]
    [InlineData("raporu gönderdim", "Raporu gönder")]
    [InlineData("mail attım", "Mail at")]
    [InlineData("sütü aldım", "Süt al")]
    [InlineData("faturayı ödedim", "Faturayı öde")]
    [InlineData("dişçiye gittim", "Dişçiye git")]
    [InlineData("sunumu hazırladım", "Sunumu hazırla")]
    [InlineData("çöpü çıkardım", "Çöpü çıkar")]
    [InlineData("formu doldurdum", "Formu doldur")]
    [InlineData("kitabı iade ettim", "Kitabı iade et")]
    [InlineData("bugün sonunda annemi aradım", "Annemi ara")]
    [InlineData("Ilgaz Bey'e raporu gönderdim", "Ilgaz Bey'e raporu gönder")]
    public void ASaidDeedClosesTheTask(string said, string task) => Assert.True(DoneStatement.Fulfills(said, task), $"{said} / {task}");

    [Theory]
    [InlineData("annemi aradım", "Babamı ara")]
    [InlineData("raporu okudum", "Raporu gönder")]
    [InlineData("koştum", "Koş")]
    [InlineData("annemi aradım", "Ara")]
    [InlineData("sütü aldım", "Ekmek al")]
    [InlineData("annemi arayacağım", "Annemi ara")]
    [InlineData("annemi aramadım", "Annemi ara")]
    public void OtherSentencesDoNot(string said, string task) => Assert.False(DoneStatement.Fulfills(said, task), $"{said} / {task}");

    [Theory]
    [InlineData("annemi aradım")]
    [InlineData("raporu gönderdim")]
    [InlineData("mail attım")]
    [InlineData("çöpü çıkardım")]
    public void TheQuickPathTriesTheTodoList(string said)
    {
        var command = QuickIntents.Match(said);
        Assert.Equal("yapilacak", command?.Tool);
        Assert.Equal("yaptim", command!.Arguments["islem"]);
        Assert.True(command.Tentative);
    }

    [Fact]
    public void TheStoreFindsTheTaskThatWasDone()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "friday-done-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTime(2026, 11, 23, 10, 0, 0);
            var store = new TodoStore(Path.Combine(folder, "todos.json"));
            store.Add("Annemi ara", now);
            store.Add("Babamı ara", now);
            store.Add("Raporu gönder", now);
            Assert.Equal("Annemi ara", store.FindDone("annemi aradım")?.Text);
            Assert.Equal("Raporu gönder", store.FindDone("raporu gönderdim")?.Text);
            Assert.Null(store.FindDone("ekmek aldım"));
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
