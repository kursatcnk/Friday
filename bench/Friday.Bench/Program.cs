using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Friday.Bench;
using Friday.Core;

// Friday'in uçtan uca ölçümü: FridayAssistant gerçek cevap hattıyla (hızlı komutlar, hafıza, bağlam, araç seçimi, dil
// modeli) yazıyla çalıştırılır; ses, mikrofon, ekran ve pano kullanılmaz. Kullanım için bench/README.md.
Console.OutputEncoding = Encoding.UTF8;
var options = Options.Parse(args);
if (options is null)
{
    Console.WriteLine("""
        Kullanım: dotnet run --project bench/Friday.Bench -c Release -- [seçenekler]
          --model friday-smart|friday-text   Ölçülecek model (varsayılan friday-smart)
          --groups hafiza,akil,...           Yalnızca bu gruplar
          --ask "soru"                       Tek soru: araçlar, düşünme, gecikme adım adım yazılır
          --no-history                       Sonucu bench/history.csv'ye ekleme
        """);
    return 1;
}

if (Process.GetProcessesByName("Friday").Length > 0)
{
    Console.WriteLine("UYARI: Friday açık. Boşta modeli GPU'dan boşaltması ölçümü bozar; ölçmeden önce Friday'i kapat.");
}

var root = RepoRoot();
var now = DateTime.Now;
using var world = new BenchWorld(Path.Combine(AppContext.BaseDirectory, "bench-home"), now);
var warm = Stopwatch.StartNew();
await world.StartAsync(options.Model);
Console.WriteLine($"Model {options.Model} hazır: {warm.Elapsed.TotalSeconds:0.0} sn");

long firstAt = 0;
var lastReply = "";
world.Assistant.AssistantSaid += (_, text) =>
{
    if (text.Trim().Length > 0)
    {
        Interlocked.CompareExchange(ref firstAt, Stopwatch.GetTimestamp(), 0);
    }
};
world.Assistant.TurnFinished += (_, result) => lastReply = result.Reply;

if (options.Ask is { } question)
{
    var start = Stopwatch.GetTimestamp();
    string At() => $"{Stopwatch.GetElapsedTime(start).TotalSeconds,6:0.00}";
    world.Assistant.Diagnostics += (_, line) => Console.WriteLine($"{At()} model  {line}");
    world.Assistant.ToolStarted += (_, tool) => Console.WriteLine($"{At()} araç   {tool}");
    world.Assistant.AssistantSaid += (_, text) => Console.WriteLine($"{At()} ekran  {text.ReplaceLineEndings(" ")}");
    var quick = QuickIntents.Match(question);
    Console.WriteLine("Hızlı komut: " + (quick is null ? "yok" : $"{quick.Tool} {string.Join(";", quick.Arguments.Select(pair => $"{pair.Key}={pair.Value}"))}{(quick.Tentative ? " (belirsiz)" : "")}"));
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    await world.Assistant.AskAsync(question, limit.Token);
    Console.WriteLine($"{At()} cevap  {lastReply}");
    Console.WriteLine($"Araçlar: {string.Join(", ", world.TakeUsedTools())}");
    return 0;
}

var cases = Cases.For(now).Where(item => options.Groups.Count == 0 || options.Groups.Contains(item.Group)).ToList();
var results = new List<Result>();
var report = new StringBuilder();
void Write(string line)
{
    Console.WriteLine(line);
    report.AppendLine(line);
}

foreach (var item in cases)
{
    world.Chats.NewChat();
    world.Tools.EndConversation();
    world.TakeUsedTools();
    var allTools = new List<string>();
    double first = -1, total = 0;
    foreach (var turn in item.Turns)
    {
        Interlocked.Exchange(ref firstAt, 0);
        lastReply = "";
        var clock = Stopwatch.GetTimestamp();
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await world.Assistant.AskAsync(turn, limit.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or InvalidOperationException)
        {
            lastReply = $"HATA: {exception.GetType().Name} {exception.Message}";
        }

        total = Stopwatch.GetElapsedTime(clock).TotalSeconds;
        allTools.AddRange(world.TakeUsedTools());
        var said = Interlocked.Read(ref firstAt);
        first = said > 0 ? Stopwatch.GetElapsedTime(clock, said).TotalSeconds : -1;
    }

    var reply = lastReply;
    var tools = allTools;
    var pass = reply.Length > 0 && !reply.StartsWith("HATA", StringComparison.Ordinal)
               && (item.Expect.Length == 0 || Regex.IsMatch(reply, item.Expect))
               && (item.Tools is null || tools.Any(tool => Regex.IsMatch(tool, $@"^(?:{item.Tools})(?:\(|$)")))
               && (item.Forbid is null || !Regex.IsMatch(reply, item.Forbid))
               && (item.ForbidTools is null || !allTools.Any(tool => Regex.IsMatch(tool, $@"^(?:{item.ForbidTools})(?:\(|$)")));
    results.Add(new Result(item, pass, first, total));
    var shown = reply.ReplaceLineEndings(" ");
    Write($"{(pass ? "OK  " : "FAIL")} [{item.Group}] {string.Join(" → ", item.Turns)} | ilk {first:0.00} sn, toplam {total:0.00} sn | {string.Join(",", tools)} | {(shown.Length > 220 ? shown[..220] + "…" : shown)}");
}

Write("");
foreach (var group in results.GroupBy(result => result.Case.Group))
{
    Write($"{group.Key,-8} {group.Count(result => result.Pass)}/{group.Count()} doğru · ilk söz medyan {Median(group.Select(result => result.First)):0.00} sn · toplam ort {group.Average(result => result.Total):0.00} sn");
}

// Model boş dönüp yeniden denenen turlar (gizli düşünme) ve üretim sınırına takılan cevaplar Friday'in günlüğünden sayılır.
var log = Directory.Exists(world.LogFolder) ? string.Concat(Directory.GetFiles(world.LogFolder).Select(File.ReadAllText)) : "";
var retries = Regex.Count(log, "Model boş döndü");
var cuts = Regex.Count(log, "üretim sınırına takıldı");
var firsts = results.Select(result => result.First).Where(value => value >= 0).OrderBy(value => value).ToList();
var p90 = firsts.Count > 0 ? firsts[Math.Min(firsts.Count - 1, (int)(firsts.Count * 0.9))] : 0;
var passed = results.Count(result => result.Pass);
var mean = results.Count > 0 ? results.Average(result => result.Total) : 0;
Write($"TOPLAM   {passed}/{results.Count} doğru · ilk söz medyan {Median(firsts):0.00} sn, p90 {p90:0.00} sn · toplam ort {mean:0.00} sn · boş dönüp yinelenen {retries} · sınıra takılan {cuts}");

if (root is not null)
{
    var stamp = now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);
    var folder = Path.Combine(root, "bench", "results");
    Directory.CreateDirectory(folder);
    var file = Path.Combine(folder, $"{stamp}-{options.Model}{(options.Groups.Count > 0 ? "-" + string.Join("+", options.Groups) : "")}.txt");
    File.WriteAllText(file, report.ToString());
    Console.WriteLine($"Ayrıntı: {file}");

    if (options.History && options.Groups.Count == 0)
    {
        var history = Path.Combine(root, "bench", "history.csv");
        if (!File.Exists(history))
        {
            File.WriteAllText(history, "tarih,surum,commit,model,dogru,toplam,ilk_soz_medyan_sn,ilk_soz_p90_sn,toplam_ort_sn,yinelenen,sinira_takilan\n");
        }

        var line = string.Join(",", now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), Version(root), Commit(root), options.Model,
            passed, results.Count, Median(firsts).ToString("0.00", CultureInfo.InvariantCulture), p90.ToString("0.00", CultureInfo.InvariantCulture),
            mean.ToString("0.00", CultureInfo.InvariantCulture), retries, cuts);
        File.AppendAllText(history, line + "\n");
        Console.WriteLine($"Geçmişe eklendi: {history}");
    }
}

return passed == results.Count ? 0 : 2;

static double Median(IEnumerable<double> values)
{
    var list = values.Where(value => value >= 0).OrderBy(value => value).ToList();
    return list.Count == 0 ? 0 : list[list.Count / 2];
}

static string? RepoRoot()
{
    for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "Friday.slnx")))
        {
            return folder.FullName;
        }
    }

    return null;
}

static string Version(string root)
{
    var project = File.ReadAllText(Path.Combine(root, "src", "Friday.Desktop", "Friday.Desktop.csproj"));
    return Regex.Match(project, "<Version>(?<v>[^<]+)</Version>").Groups["v"].Value;
}

static string Commit(string root)
{
    try
    {
        var head = File.ReadAllText(Path.Combine(root, ".git", "HEAD")).Trim();
        var id = head.StartsWith("ref: ", StringComparison.Ordinal) ? File.ReadAllText(Path.Combine(root, ".git", head[5..])).Trim() : head;
        return id.Length >= 7 ? id[..7] : id;
    }
    catch (IOException)
    {
        return "";
    }
}

internal sealed record Result(Case Case, bool Pass, double First, double Total);

/// <summary>Komut satırı seçenekleri.</summary>
internal sealed record Options(string Model, IReadOnlySet<string> Groups, string? Ask, bool History)
{
    public static Options? Parse(string[] args)
    {
        var model = FridaySettings.SmartTextModel;
        var groups = new HashSet<string>(StringComparer.Ordinal);
        string? ask = null;
        var history = true;
        for (var index = 0; index < args.Length; index++)
        {
            string Next() => index + 1 < args.Length ? args[++index] : "";
            switch (args[index])
            {
                case "--model":
                    model = Next();
                    break;
                case "--groups":
                    groups.UnionWith(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--ask":
                    ask = Next();
                    break;
                case "--no-history":
                    history = false;
                    break;
                default:
                    return null;
            }
        }

        return model.Length == 0 ? null : new Options(model, groups, ask, history);
    }
}
