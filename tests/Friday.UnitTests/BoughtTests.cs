using Friday.Core;

namespace Friday.UnitTests;

/// <summary>"Sütü aldım" gibi cümlelerden alınanların çıkarılması ve listeden düşülmesi.</summary>
public class BoughtTests
{
    [Theory]
    [InlineData("sütü aldım", "sütü")]
    [InlineData("süt ve ekmeği aldım", "süt|ekmeği")]
    [InlineData("marketten yumurta aldım", "yumurta")]
    [InlineData("bugün deterjan aldım", "deterjan")]
    [InlineData("annemi aradım", "")]
    [InlineData("aldım", "")]
    public void FindsWhatWasBought(string said, string expected) =>
        Assert.Equal(expected.Length == 0 ? [] : expected.Split('|'), DoneStatement.Bought(said));

    [Fact]
    public void TheListLosesWhatWasBought()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "friday-bought-" + Guid.NewGuid().ToString("N"));
        try
        {
            var lists = new ListStore(Path.Combine(folder, "lists.json"));
            lists.Add("market", "süt, ekmek, yumurta");
            foreach (var item in DoneStatement.Bought("süt ve ekmeği aldım"))
            {
                Assert.NotNull(lists.Remove("market", item));
            }

            Assert.Equal(["yumurta"], lists.Items("market"));
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
