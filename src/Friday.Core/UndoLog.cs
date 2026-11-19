namespace Friday.Core;

/// <summary>Kaydı geri alınabilen veri deposu (yapılacaklar, harcamalar, hatırlatıcılar…).</summary>
public interface IRestorableStore
{
    /// <summary>Konuşmada geçen adı: "yapılacaklar", "harcamalar"…</summary>
    string Title { get; }

    /// <summary>Dosyayı ve bellekteki durumu bu içeriğe döndürür; null, dosyanın henüz olmadığı durumdur.</summary>
    void Restore(string file, string? content);
}

/// <summary>Bir isteğin bir depoda yaptığı değişiklik: dosyanın önceki ve sonraki içeriği.</summary>
public sealed record UndoChange(IRestorableStore Store, string File, string? Before, string? After);

/// <summary>Tek bir isteğin ("süt almayı ekle") bütün kayıt değişiklikleri; geri alma bunları birlikte döndürür.</summary>
public sealed record UndoEntry(string Request, DateTime At, IReadOnlyList<UndoChange> Changes)
{
    public IReadOnlyList<string> Titles => [.. Changes.Select(change => change.Store.Title).Distinct(StringComparer.Ordinal)];
}

/// <summary>Geri alma ya da yineleme sonucu: hangi kayıt döndü, hangileri bu arada değiştiği için dokunulmadan kaldı.</summary>
public sealed record UndoOutcome(UndoEntry Entry, IReadOnlyList<string> Restored, IReadOnlyList<string> Skipped);

/// <summary>
/// Friday'in kayıtlarda yaptığı her değişikliğin günlüğü. Bir istek (sesli komut ya da dil modelinin araç çağrıları)
/// <see cref="Begin"/> ile açılır; depolar dosyalarını yazmadan hemen önce <see cref="Touching"/> der ve dosyanın o anki
/// içeriği saklanır. İstek bitince değişen dosyalar tek bir kayıt olur: "geri al" hepsini birlikte eski haline döndürür,
/// "geri getir" geri alınanı yineler. Kapsam AsyncLocal'dır: zamanı gelen bir hatırlatıcının arka planda yazması gibi
/// isteğe ait olmayan değişiklikler karışmaz. Değişiklikten sonra dosya başka bir yoldan (panel, el ile) değiştiyse o
/// dosyaya dokunulmaz; sonraki iş kaybolmaz.
/// </summary>
public sealed class UndoLog(TimeProvider? clock = null)
{
    public const int Capacity = 20;

    /// <summary>Bundan eski değişiklikler geri alınmaz; "geri al" çok eski bir şeyi beklenmedik biçimde bozmasın.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(6);

    private static readonly AsyncLocal<Scope?> Active = new();

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly List<UndoEntry> _done = [];
    private readonly List<UndoEntry> _undone = [];

    /// <summary>Geri alınabilecek değişiklikler, en yenisi sonda.</summary>
    public IReadOnlyList<UndoEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                Expire();
                return [.. _done];
            }
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_gate)
            {
                return _undone.Count > 0;
            }
        }
    }

    /// <summary>Depolar dosyalarını yazmadan hemen önce çağırır; açık bir istek yoksa hiçbir şey yapmaz.</summary>
    public static void Touching(IRestorableStore store, string file)
    {
        if (Active.Value is { Closed: false } scope)
        {
            scope.Touch(store, file);
        }
    }

    /// <summary>Bir isteğin değişikliklerini toplamaya başlar; iç içe çağrılarda dıştaki istek toplar.</summary>
    public IUndoScope Begin(string request)
    {
        if (Active.Value is { Closed: false })
        {
            return Nested.Instance;
        }

        var scope = new Scope(this, request.Trim(), Active.Value);
        Active.Value = scope;
        return scope;
    }

    /// <summary>En son değişikliği geri alır; yoksa ya da çok eskiyse null.</summary>
    public UndoOutcome? Undo()
    {
        UndoEntry entry;
        lock (_gate)
        {
            Expire();
            if (_done.Count == 0)
            {
                return null;
            }

            entry = _done[^1];
            _done.RemoveAt(_done.Count - 1);
            _undone.Add(entry);
        }

        return Apply(entry, undo: true);
    }

    /// <summary>En son geri alınanı yineler; yoksa null.</summary>
    public UndoOutcome? Redo()
    {
        UndoEntry entry;
        lock (_gate)
        {
            if (_undone.Count == 0)
            {
                return null;
            }

            entry = _undone[^1];
            _undone.RemoveAt(_undone.Count - 1);
            _done.Add(entry);
        }

        return Apply(entry, undo: false);
    }

    /// <summary>Depoların <see cref="IRestorableStore.Restore"/> içinde dosyayı eski içeriğine döndürmesi için.</summary>
    public static void WriteBack(string file, string? content)
    {
        if (content is null)
        {
            File.Delete(file);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private static string? Read(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static UndoOutcome Apply(UndoEntry entry, bool undo)
    {
        var restored = new List<string>();
        var skipped = new List<string>();

        // Geri alma kendi başına yeni bir değişiklik sayılmaz ("geri al" iki kez söylenince bir öncekine gider).
        var outer = Active.Value;
        Active.Value = null;
        try
        {
            foreach (var change in entry.Changes)
            {
                var expected = undo ? change.After : change.Before;
                var target = undo ? change.Before : change.After;
                if (!string.Equals(Read(change.File), expected, StringComparison.Ordinal))
                {
                    skipped.Add(change.Store.Title);
                    continue;
                }

                change.Store.Restore(change.File, target);
                restored.Add(change.Store.Title);
            }
        }
        finally
        {
            Active.Value = outer;
        }

        return new UndoOutcome(entry, [.. restored.Distinct(StringComparer.Ordinal)], [.. skipped.Distinct(StringComparer.Ordinal).Except(restored, StringComparer.Ordinal)]);
    }

    private void Commit(Scope scope)
    {
        var changes = scope.Changes();
        if (changes.Count == 0)
        {
            return;
        }

        var entry = new UndoEntry(scope.Request, _clock.GetLocalNow().DateTime, changes);
        scope.Entry = entry;
        lock (_gate)
        {
            _done.Add(entry);
            _undone.Clear();
            if (_done.Count > Capacity)
            {
                _done.RemoveAt(0);
            }
        }
    }

    private void Expire()
    {
        var now = _clock.GetLocalNow().DateTime;
        _done.RemoveAll(entry => now - entry.At > Window);
    }

    private sealed class Scope(UndoLog log, string request, Scope? parent) : IUndoScope
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, (IRestorableStore Store, string? Before)> _touched = new(StringComparer.OrdinalIgnoreCase);

        public string Request => request;

        public bool Closed { get; private set; }

        public UndoEntry? Entry { get; set; }

        public void Touch(IRestorableStore store, string file)
        {
            lock (_gate)
            {
                if (!Closed && !_touched.ContainsKey(file))
                {
                    _touched[file] = (store, Read(file));
                }
            }
        }

        public List<UndoChange> Changes()
        {
            lock (_gate)
            {
                return
                [
                    .. _touched
                        .Select(pair => new UndoChange(pair.Value.Store, pair.Key, pair.Value.Before, Read(pair.Key)))
                        .Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal))
                ];
            }
        }

        public void Dispose()
        {
            if (Closed)
            {
                return;
            }

            Closed = true;
            Active.Value = parent;
            log.Commit(this);
        }
    }

    private sealed class Nested : IUndoScope
    {
        public static readonly Nested Instance = new();

        public UndoEntry? Entry => null;

        public void Dispose()
        {
        }
    }
}

/// <summary>Açık bir isteğin kapsamı; kapatılınca değişiklikler tek kayıt olur.</summary>
public interface IUndoScope : IDisposable
{
    /// <summary>Kapsam kapandıktan sonra, istek bir kaydı değiştirdiyse o kayıt.</summary>
    UndoEntry? Entry { get; }
}
