using System.Text.Encodings.Web;
using System.Text.Json;

namespace Friday.Core;

public enum Verbosity
{
    Normal,
    Brief,
    Detailed
}

/// <summary>
/// Cevap uzunluğunu kullanıcıdan öğrenir. Açık tercih ("daha kısa konuş", "cevaplarını daha ayrıntılı ver") hemen ve
/// kalıcı uygulanır. Açık tercih yoksa davranıştan öğrenilir: kullanıcı uzun cevapların sözünü sık kesiyorsa (son 12
/// uzun cevabın %40'ı) Friday kısalır ve bunu bir kez söyler; kesmeyi bırakırsa eski haline döner. Talimat önbelleği
/// bozulmasın diye üslup, sistem talimatına değil sorunun yanındaki satıra eklenir. D:\Friday\data\reply-style.json.
/// </summary>
public sealed class ReplyStyle(string file)
{
    public const int Window = 12;
    public const int MinSamples = 5;
    public const double CutRatio = 0.4;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private StyleData? _data;
    private bool _notice;

    public Verbosity Current
    {
        get
        {
            lock (_gate)
            {
                return Effective(Load());
            }
        }
    }

    /// <summary>Kullanıcı açıkça bir uzunluk istedi mi (davranıştan öğrenilen değil).</summary>
    public bool IsExplicit
    {
        get
        {
            lock (_gate)
            {
                return Load().Preferred is not null;
            }
        }
    }

    /// <summary>Son uzun cevaplardan kaçının sözü kesildi.</summary>
    public (int Cut, int Total) Recent
    {
        get
        {
            lock (_gate)
            {
                var outcomes = Load().Outcomes;
                return (outcomes.Count(cut => cut), outcomes.Count);
            }
        }
    }

    /// <summary>Sorunun yanına eklenecek üslup satırı; normal üslupta null.</summary>
    public string? Hint => Current switch
    {
        Verbosity.Brief => "[Üslup: kullanıcı kısa cevap istiyor; en fazla iki kısa cümleyle cevap ver, ayrıntıyı ancak isterse ver.]",
        Verbosity.Detailed => "[Üslup: kullanıcı ayrıntılı cevap seviyor; gerektiğinde dört-altı cümleyle, örnekle açıkla.]",
        _ => null
    };

    /// <summary>Uzun bir cevabın sonucu: sözü kesildi mi? Davranıştan öğrenilen üslup değiştiyse true.</summary>
    public bool RecordLongAnswer(bool interrupted)
    {
        lock (_gate)
        {
            var data = Load();
            var before = Effective(data);
            data.Outcomes.Add(interrupted);
            if (data.Outcomes.Count > Window)
            {
                data.Outcomes.RemoveAt(0);
            }

            Save(data);
            var after = Effective(data);
            if (after == Verbosity.Brief && before != Verbosity.Brief)
            {
                _notice = true;
            }

            return after != before;
        }
    }

    /// <summary>Kullanıcının açık tercihi; Normal açık tercihi kaldırır ve öğrenileni sıfırlar.</summary>
    public void Prefer(Verbosity verbosity)
    {
        lock (_gate)
        {
            var data = Load();
            data.Preferred = verbosity == Verbosity.Normal ? null : verbosity;
            if (verbosity == Verbosity.Normal)
            {
                data.Outcomes.Clear();
            }

            _notice = false;
            Save(data);
        }
    }

    /// <summary>Davranıştan kısalmaya geçildiyse bir kez true (Friday bunu söyler).</summary>
    public bool TakeNotice()
    {
        lock (_gate)
        {
            var notice = _notice;
            _notice = false;
            return notice;
        }
    }

    private static Verbosity Effective(StyleData data)
    {
        if (data.Preferred is { } preferred)
        {
            return preferred;
        }

        return data.Outcomes.Count >= MinSamples && data.Outcomes.Count(cut => cut) >= Math.Ceiling(data.Outcomes.Count * CutRatio)
            ? Verbosity.Brief
            : Verbosity.Normal;
    }

    private StyleData Load()
    {
        if (_data is not null)
        {
            return _data;
        }

        try
        {
            _data = File.Exists(file) ? JsonSerializer.Deserialize<StyleData>(File.ReadAllText(file), Options) ?? new StyleData() : new StyleData();
        }
        catch (JsonException)
        {
            _data = new StyleData();
        }

        return _data;
    }

    private void Save(StyleData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(data, Options));
        }
        catch (IOException)
        {
            // Kaydedilemezse bu oturumda bellekte kalır.
        }
    }

    private sealed class StyleData
    {
        public Verbosity? Preferred { get; set; }

        public List<bool> Outcomes { get; set; } = [];
    }
}
