using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public sealed class ToolBudgetTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly HttpClient _http = new();
    private readonly AssistantTools _tools;

    public ToolBudgetTests()
    {
        var paths = new FridayPaths(_temp.Path);
        _tools = new AssistantTools(_http, new OllamaChatClient(_http, new OllamaServer(paths, _http)),
            new MemoryStore(Path.Combine(_temp.Path, "memory.json")), new PrivacyLedger(_temp.Path), new ChatStore(Path.Combine(_temp.Path, "chats")),
            new ReminderStore(Path.Combine(_temp.Path, "reminders.json")), new NoteStore(Path.Combine(_temp.Path, "notes.md")));
    }

    private static readonly System.Text.Json.JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static IReadOnlyList<string> Names(System.Text.Json.Nodes.JsonArray tools) =>
        [.. tools.Select(node => node!["function"]!["name"]!.GetValue<string>())];

    [Fact]
    public void EveryRoutedToolExists()
    {
        var all = Names(_tools.Definitions);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(ToolRouter.Core, name => Assert.Contains(name, all));
        // Bütün konulardaki araçlar gerçekten tanımlı olmalı (yazım hatası alt kümede sessizce kaybolurdu).
        var everyTopic = ToolRouter.Select("harca dolar alışkanlık kelime plan odak müzik dosya pencere bilgisayar internet haber rutin hatırlat uyku mail liste günlük hafta rahatsız kısaltma doğum sınır linguaflow sayfa kopyala yokken araştır github", false)!;
        Assert.All(everyTopic, name => Assert.Contains(name, all));
    }

    [Fact]
    public void SendsASmallStableCoreForFreeQuestions()
    {
        var free = _tools.DefinitionsFor("Güneş neden sarı görünür?", false);
        Assert.Equal(ToolRouter.Core.Count, free.Count);
        Assert.Same(free, _tools.DefinitionsFor("Bana kısa bir fıkra anlat", false));
        // Tüm tanımların dörtte birinden az: her soruda ~3.000 belirteç ve ~0,8 sn kazanılır.
        Assert.True(free.ToJsonString(Relaxed).Length * 4 < _tools.Definitions.ToJsonString(Relaxed).Length,
            $"{free.ToJsonString(Relaxed).Length} / {_tools.Definitions.ToJsonString(Relaxed).Length}");
    }

    [Theory]
    [InlineData("bu ay ne kadar harcadım, kirayı ödemiş miyim", "harcama", "sabit_gider")]
    [InlineData("spotify'ı biraz kısar mısın ve bir sonraki şarkıya geç", "uygulama_sesi", "medya")]
    [InlineData("geçen hafta indirdiğim pdf'i bulup özetler misin", "dosya_bul", "belge_oku")]
    [InlineData("bilgisayar neden bu kadar yavaş", "sistem_durumu", "durum_raporu")]
    public void AddsToolsForTheTopic(string question, string first, string second)
    {
        var names = Names(_tools.DefinitionsFor(question, false));
        Assert.Contains(first, names);
        Assert.Contains(second, names);
        Assert.True(names.Count < 30, $"{names.Count}");
    }

    [Theory]
    [InlineData("Gökkuşağı neden kavisli görünür?", false)]
    [InlineData("Bana kısa bir fıkra anlatır mısın?", false)]
    [InlineData("Ağrı Dağı kaç metre?", true)]
    [InlineData("Marie Curie kimdir?", true)]
    [InlineData("Dünkü maçın sonucu ne oldu?", true)]
    [InlineData("İstanbul'un nüfusu ne kadar?", true)]
    public void ThinksOnlyForFactualQuestions(string question, bool factual) => Assert.Equal(factual, AgentPlan.IsFactual(question));

    [Fact]
    public void KeepsRecentToolsAndSendsEverythingForMultiStepTasks()
    {
        Assert.Contains("liste", Names(_tools.DefinitionsFor("onu da ekle", false, ["liste"])));
        Assert.Same(_tools.Definitions, _tools.DefinitionsFor("dün indirdiğim PDF'i bul, özetle ve notlarıma ekle", true));
    }

    public void Dispose()
    {
        _tools.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }
}
