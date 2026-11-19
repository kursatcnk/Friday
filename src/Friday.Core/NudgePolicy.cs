using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

/// <summary>Bir dürtme türünün durumu: üst üste kaç kez geri çevrildiği ve ne zamana kadar susturulduğu.</summary>
public sealed class NudgeState
{
    public int Declines { get; set; }

    public DateTime? QuietUntil { get; set; }
}

/// <summary>
/// Friday'in kendiliğinden konuşmalarının (öneri, hatırlatma, soru) kullanıcıdan öğrenmesi. Aynı türden bir öneri üst
/// üste üç kez geri çevrilirse o tür bir hafta susar; "bunu bir daha sorma" denirse kalıcı susar. "Evet" denince
/// sayaç sıfırlanır. Kullanıcının kendi kurduğu hatırlatmalar bu sınıra girmez. D:\Friday\data\nudges.json'da durur.
/// </summary>
public sealed class NudgePolicy
{
    public const int DeclineLimit = 3;
    public static readonly TimeSpan BackOff = TimeSpan.FromDays(7);

    /// <summary>Dürtme türleri ve okunuşları.</summary>
    public static IReadOnlyDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["rutin_onerisi"] = "rutin ve uygulama önerileri",
        ["arife"] = "doğum günü arifesi hatırlatması",
        ["gun_sonu"] = "gün sonu sorusu",
        ["seri"] = "alışkanlık serisi hatırlatması",
        ["son_tarih"] = "son tarih hatırlatması",
        ["gorusme_notu"] = "görüşme sonrası not sorusu",
        ["uyku"] = "uyku saati hatırlatması",
        ["hava"] = "akşam hava uyarısı",
        ["goz"] = "göz molası",
        ["kayip"] = "ben yokken söylediklerimin tekrarı"
    };

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _file;
    private readonly object _gate = new();
    private readonly Dictionary<string, NudgeState> _states;

    public NudgePolicy(string file)
    {
        _file = file;
        try
        {
            _states = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, NudgeState>>(File.ReadAllText(file), Options) ?? [] : [];
        }
        catch (JsonException)
        {
            _states = [];
        }
    }

    /// <summary>Bu tür şu an söylenebilir mi?</summary>
    public bool Allowed(string kind, DateTime now)
    {
        lock (_gate)
        {
            return !_states.TryGetValue(kind, out var state) || state.QuietUntil is not { } until || now >= until;
        }
    }

    /// <summary>Kullanıcı öneriye cevap verdi. Üst üste <see cref="DeclineLimit"/> "hayır" o türü bir hafta susturur.</summary>
    /// <returns>Bu cevapla tür susturulduysa true (kullanıcıya bir kez söylenir).</returns>
    public bool Respond(string kind, bool accepted, DateTime now)
    {
        lock (_gate)
        {
            var state = _states.TryGetValue(kind, out var known) ? known : _states[kind] = new NudgeState();
            if (accepted)
            {
                state.Declines = 0;
                Save();
                return false;
            }

            state.Declines++;
            var silenced = state.Declines >= DeclineLimit;
            if (silenced)
            {
                state.Declines = 0;
                state.QuietUntil = now + BackOff;
            }

            Save();
            return silenced;
        }
    }

    /// <summary>"Bunu bir daha sorma": tür kalıcı susar (yeniden açılana kadar).</summary>
    public void Silence(string kind)
    {
        lock (_gate)
        {
            var state = _states.TryGetValue(kind, out var known) ? known : _states[kind] = new NudgeState();
            state.QuietUntil = DateTime.MaxValue;
            state.Declines = 0;
            Save();
        }
    }

    /// <summary>Susturulmuş bütün türler yeniden açılır; açılan sayısı.</summary>
    public int Reset()
    {
        lock (_gate)
        {
            var count = _states.Count(pair => pair.Value.QuietUntil is not null);
            _states.Clear();
            Save();
            return count;
        }
    }

    /// <summary>Şu an susturulmuş türler ve ne zamana kadar (kalıcıysa null).</summary>
    public IReadOnlyList<(string Kind, DateTime? Until)> Silenced(DateTime now)
    {
        lock (_gate)
        {
            return [.. _states.Where(pair => pair.Value.QuietUntil is { } until && until > now)
                .Select(pair => (pair.Key, pair.Value.QuietUntil == DateTime.MaxValue ? (DateTime?)null : pair.Value.QuietUntil))];
        }
    }

    public static string Label(string kind) => Labels.GetValueOrDefault(kind, kind);

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_states, Options));
    }
}
