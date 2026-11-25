using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public sealed class SettingsUndoTests : IDisposable
{
    private readonly string _home = Path.Combine(AppContext.BaseDirectory, "friday-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Fact]
    public void UndoesASettingChangeInPlace()
    {
        var store = new JsonSettingsStore(new FridayPaths(_home));
        var settings = store.Load();
        store.Live = settings;
        var restored = 0;
        store.Restored += (_, _) => restored++;
        var city = settings.City;
        var log = new UndoLog();

        using (log.Begin("şehrimi Ankara yap ve imza kısaltması ekle"))
        {
            settings.City = "Ankara";
            settings.Snippets["imza"] = "Saygılarımla";
            store.Save(settings);
        }

        Assert.Equal(["ayarlar"], log.Entries.Single().Titles);
        log.Undo();
        Assert.Equal(city, settings.City);
        Assert.False(settings.Snippets.ContainsKey("Imza"));
        Assert.Equal(1, restored);
        Assert.Equal(city, new JsonSettingsStore(new FridayPaths(_home)).Load().City);

        log.Redo();
        Assert.Equal("Ankara", settings.City);
        Assert.True(settings.Snippets.ContainsKey("Imza"));
    }
}
