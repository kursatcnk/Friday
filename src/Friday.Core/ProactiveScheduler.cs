namespace Friday.Core;

/// <summary>Kendiliğinden konuşma anındaki durum.</summary>
public sealed record ProactiveContext(DateTime Now, bool Muted, bool Away, bool FullScreen);

/// <summary>
/// Kendiliğinden söylenebilecek bir şey. Produce o an söylenecek cümleyi üretir (zamanı değilse null); günde bir kez gibi
/// kendi kuralları içindedir. UserSet: kullanıcının kendi kurduğu uyarı (kur alarmı, süre sınırı) — rahatsız etme ve
/// öğrenme politikası ona uygulanmaz. Spaced: akşam dürtmesi — aralarında en az <see cref="ProactiveScheduler.Spacing"/> olur.
/// </summary>
public sealed record ProactiveCandidate(string Kind, int Priority, Func<ProactiveContext, Task<string?>> Produce)
{
    public bool UserSet { get; init; }

    public bool NeedsPresence { get; init; }

    public bool AvoidFullScreen { get; init; }

    public bool Spaced { get; init; }

    public bool ListenAfter { get; init; }
}

/// <summary>
/// Kendiliğinden konuşmaların tek kapısı. Her dakika bütün adaylar önem sırasıyla denenir; ilk söylenebilir olan seçilir
/// (aynı dakikada ikinci bir şey söylenmez). Rahatsız etme, bilgisayar başında olmamak, tam ekran, akşam dürtmeleri
/// arasındaki boşluk ve kullanıcının geri çevirdikleri tek yerde uygulanır.
/// </summary>
public sealed class ProactiveScheduler
{
    public static readonly TimeSpan Spacing = TimeSpan.FromMinutes(45);

    private DateTime _lastSpaced = DateTime.MinValue;

    /// <summary>Bu tür söylenebilir mi (öğrenme politikası)? Varsayılan: her şey.</summary>
    public Func<string, bool> Allowed { get; set; } = _ => true;

    public async Task<(ProactiveCandidate Candidate, string Text)?> NextAsync(IEnumerable<ProactiveCandidate> candidates, ProactiveContext context)
    {
        foreach (var candidate in candidates.OrderByDescending(item => item.Priority))
        {
            if (!candidate.UserSet && (context.Muted || !Allowed(candidate.Kind)))
            {
                continue;
            }

            if ((candidate.NeedsPresence && context.Away) || (candidate.AvoidFullScreen && context.FullScreen)
                || (candidate.Spaced && context.Now - _lastSpaced < Spacing))
            {
                continue;
            }

            if (await candidate.Produce(context).ConfigureAwait(false) is { Length: > 0 } text)
            {
                if (candidate.Spaced)
                {
                    _lastSpaced = context.Now;
                }

                return (candidate, text);
            }
        }

        return null;
    }
}
