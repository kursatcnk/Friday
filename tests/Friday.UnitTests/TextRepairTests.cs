using Friday.Core;

namespace Friday.UnitTests;

public class TextRepairTests
{
    [Theory]
    [InlineData("Ä°stanbul", "İstanbul")]
    [InlineData("gÃ¶rev yÃ¶neticisi", "görev yöneticisi")]
    [InlineData("Ã§alÄ±ÅŸma modu", "çalışma modu")]
    [InlineData("Ã–zel ÄŸ Ãœ", "Özel ğ Ü")]
    public void RepairsTurkishLettersReadAsWindows1252(string broken, string expected) =>
        Assert.Equal(expected, TextRepair.FixMojibake(broken));

    [Theory]
    [InlineData("İstanbul")]
    [InlineData("çalışma modu")]
    [InlineData("Ärger mit Åse")]
    [InlineData("")]
    public void LeavesCorrectTextAlone(string text) => Assert.Equal(text, TextRepair.FixMojibake(text));
}
