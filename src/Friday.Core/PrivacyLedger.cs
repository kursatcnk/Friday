using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

public enum DataFlow
{
    /// <summary>Bilgisayarda kaldı (mikrofon, ekran görüntüsü, pano, dosya araması…).</summary>
    Local,

    /// <summary>Bilgisayardan çıktı (hava durumu servisi, tarayıcıda açılan site…).</summary>
    Outbound
}

public sealed record LedgerEntry(DateTimeOffset Time, DataFlow Flow, string Category, string Detail);

/// <summary>
/// Gizlilik defteri: Friday'in yaptığı her hassas iş ve bilgisayardan çıkan her istek kaydedilir. "Seni dinlemiyorum"
/// iddiası böylece görünür ve denetlenebilir olur. Kayıtlar gün gün D:\Friday\data\ledger altında tutulur.
/// </summary>
public sealed class PrivacyLedger
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _directory;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly List<LedgerEntry> _today = [];
    private DateOnly _day;

    public PrivacyLedger(string directory, TimeProvider? clock = null)
    {
        _directory = directory;
        _clock = clock ?? TimeProvider.System;
        _day = Today();
        var file = FileFor(_day);
        if (File.Exists(file))
        {
            foreach (var line in File.ReadLines(file).Where(line => line.Length > 0))
            {
                if (JsonSerializer.Deserialize<LedgerEntry>(line, Options) is { } entry)
                {
                    _today.Add(entry);
                }
            }
        }
    }

    public event EventHandler<LedgerEntry>? Added;

    public IReadOnlyList<LedgerEntry> TodayEntries
    {
        get
        {
            lock (_gate)
            {
                RollOver();
                return [.. _today];
            }
        }
    }

    public void Record(DataFlow flow, string category, string detail)
    {
        var entry = new LedgerEntry(_clock.GetLocalNow(), flow, category, detail);
        lock (_gate)
        {
            RollOver();
            _today.Add(entry);
            Directory.CreateDirectory(_directory);
            File.AppendAllText(FileFor(_day), JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
        }

        Added?.Invoke(this, entry);
    }

    private void RollOver()
    {
        var today = Today();
        if (today != _day)
        {
            _day = today;
            _today.Clear();
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    private string FileFor(DateOnly day) => Path.Combine(_directory, $"{day:yyyy-MM-dd}.jsonl");
}
