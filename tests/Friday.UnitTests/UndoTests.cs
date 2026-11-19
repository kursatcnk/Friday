using Friday.Core;

namespace Friday.UnitTests;

public sealed class UndoTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 19, 14, 0, 0);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "friday-undo-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void UndoesEverythingOneRequestChangedAndRedoesIt()
    {
        var log = new UndoLog();
        var todos = new TodoStore(Path.Combine(_folder, "todos.json"));
        var expenses = new ExpenseBook(Path.Combine(_folder, "expenses.json"));
        todos.Add("faturayı öde", Now);

        using (log.Begin("süt al ekle ve 50 lira market yaz"))
        {
            todos.Add("süt al", Now);
            expenses.Add(50, "market", Now);
        }

        var entry = Assert.Single(log.Entries);
        Assert.Equal(["yapılacaklar", "harcamalar"], entry.Titles.Order(StringComparer.Ordinal).Reverse());

        var outcome = log.Undo();
        Assert.NotNull(outcome);
        Assert.Empty(outcome.Skipped);
        Assert.Equal(["Faturayı öde"], todos.Visible(Now).Select(item => item.Text));
        Assert.Empty(expenses.Between(Now.AddDays(-1), Now.AddDays(1)));
        Assert.False(File.Exists(Path.Combine(_folder, "expenses.json")));
        Assert.Empty(log.Entries);

        // Diskten yeniden okununca da eski hali görülür.
        Assert.Single(new TodoStore(Path.Combine(_folder, "todos.json")).Visible(Now));

        Assert.NotNull(log.Redo());
        Assert.Equal(2, todos.Visible(Now).Count);
        Assert.Single(expenses.Between(Now.AddDays(-1), Now.AddDays(1)));
        Assert.Single(log.Entries);
    }

    [Fact]
    public void IgnoresChangesOutsideARequestAndRepeatedUndoGoesFurtherBack()
    {
        var log = new UndoLog();
        var todos = new TodoStore(Path.Combine(_folder, "todos.json"));
        todos.Add("panelden eklenen", Now);
        Assert.Empty(log.Entries);

        using (log.Begin("bir"))
        {
            todos.Add("bir", Now);
        }

        using (log.Begin("saat kaç"))
        {
            // Kayda dokunmayan istek kayıt bırakmaz.
        }

        using (log.Begin("iki"))
        {
            todos.Add("iki", Now);
        }

        Assert.Equal(["bir", "iki"], log.Entries.Select(entry => entry.Request));
        Assert.Equal("iki", log.Undo()!.Entry.Request);
        Assert.Equal("bir", log.Undo()!.Entry.Request);
        Assert.Null(log.Undo());
        Assert.Equal(["Panelden eklenen"], todos.Visible(Now).Select(item => item.Text));
    }

    [Fact]
    public void LeavesAFileAloneWhenItChangedAfterwards()
    {
        var log = new UndoLog();
        var todos = new TodoStore(Path.Combine(_folder, "todos.json"));
        using (log.Begin("süt al ekle"))
        {
            todos.Add("süt al", Now);
        }

        todos.Add("panelden sonra eklenen", Now);
        var outcome = log.Undo();
        Assert.NotNull(outcome);
        Assert.Empty(outcome.Restored);
        Assert.Equal(["yapılacaklar"], outcome.Skipped);
        Assert.Equal(2, todos.Visible(Now).Count);
    }

    [Fact]
    public async Task FollowsTheRequestAcrossAwaitsButNotIntoUnrelatedWork()
    {
        var log = new UndoLog();
        var todos = new TodoStore(Path.Combine(_folder, "todos.json"));
        var lists = new ListStore(Path.Combine(_folder, "lists.json"));
        var gate = new TaskCompletionSource();
        var background = Task.Run(async () =>
        {
            await gate.Task;
            lists.Add("market", "ekmek");
        });

        using (log.Begin("süt al ekle"))
        {
            await Task.Run(() => todos.Add("süt al", Now));
            using (log.Begin("iç içe"))
            {
                await Task.Yield();
                todos.Add("yumurta al", Now);
            }

            gate.SetResult();
            await background;
        }

        var entry = Assert.Single(log.Entries);
        Assert.Equal("süt al ekle", entry.Request);
        Assert.Equal(["yapılacaklar"], entry.Titles);
        log.Undo();
        Assert.Empty(todos.Visible(Now));
        Assert.Equal(["ekmek"], lists.Items("market"));
    }

    [Fact]
    public void RestoresAppendOnlyFilesToo()
    {
        var log = new UndoLog();
        var notes = new NoteStore(Path.Combine(_folder, "notes.md"));
        notes.Add("ilk not", Now);
        using (log.Begin("not al: ikinci"))
        {
            notes.Add("ikinci", Now);
        }

        Assert.Equal(2, notes.All.Count);
        log.Undo();
        Assert.Equal(["ilk not"], notes.All.Select(note => note.Text));
    }

    [Fact]
    public void ForgetsOldChanges()
    {
        var clock = new ManualClock(Now);
        var log = new UndoLog(clock);
        var todos = new TodoStore(Path.Combine(_folder, "todos.json"));
        using (log.Begin("süt al ekle"))
        {
            todos.Add("süt al", Now);
        }

        clock.Advance(UndoLog.Window + TimeSpan.FromMinutes(1));
        Assert.Empty(log.Entries);
        Assert.Null(log.Undo());
        Assert.Single(todos.Visible(Now));
    }

    [Theory]
    [InlineData("geri al", "geri_al")]
    [InlineData("Friday, onu geri al.", "geri_al")]
    [InlineData("son yaptığını geri al", "geri_al")]
    [InlineData("hayır, geri al", "geri_al")]
    [InlineData("az önce eklediğini geri alır mısın", "geri_al")]
    [InlineData("geri getir", "geri_getir")]
    [InlineData("geri aldığını geri getir", "geri_getir")]
    [InlineData("neleri değiştirdin", "liste")]
    [InlineData("son değişikliklerin neler", "liste")]
    public void UnderstandsUndoRequests(string text, string action)
    {
        var command = QuickIntents.Match(text);
        Assert.NotNull(command);
        Assert.Equal("geri_al", command.Tool);
        Assert.Equal(action, command.Arguments["islem"]);
    }

    [Theory]
    [InlineData("son harcamayı geri al")]
    [InlineData("koşu işaretini geri al")]
    [InlineData("indirilenler düzenlemeyi geri al")]
    public void KeepsSpecificUndoCommands(string text) =>
        Assert.NotEqual("geri_al", QuickIntents.Match(text)?.Tool);

    private sealed class ManualClock(DateTime start) : TimeProvider
    {
        private DateTimeOffset _now = new(start);

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan by) => _now += by;
    }
}
