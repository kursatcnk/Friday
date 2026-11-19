using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Kendiliğinden konuşmaların kullanıcıdan öğrenmesi (bkz. <see cref="NudgePolicy"/>).</summary>
public sealed partial class AssistantTools
{
    private string? _pendingKind;
    private string? _awaitingKind;

    /// <summary>Dürtme politikası (App verir); yoksa her şey serbest.</summary>
    public NudgePolicy? Nudges { get; set; }

    /// <summary>Kendiliğinden söylenen son şeyin türü ve zamanı ("bunu bir daha söyleme" ona uygulanır).</summary>
    public string? LastNudge { get; private set; }

    private DateTime _lastNudgeAt;

    public bool AllowsNudge(string kind) => Nudges?.Allowed(kind, DateTime.Now) ?? true;

    public void MarkNudge(string kind)
    {
        LastNudge = kind;
        _lastNudgeAt = DateTime.Now;
    }

    /// <summary>Onay sorusunun türü politika anahtarına çevrilir; kullanıcının kendi istediği sorular (plan, odak) sayılmaz.</summary>
    private static string? NudgeKey(string? askKind) => askKind switch
    {
        "Alışkanlık" => "rutin_onerisi",
        "Özel gün" => "arife",
        _ => null
    };

    /// <summary>Cevap politikaya işlenir; tür bu "hayır"la susturulduysa söylenecek ek cümle.</summary>
    private string Learn(string? kind, bool accepted)
    {
        if (kind is null || Nudges is not { } policy || !policy.Respond(kind, accepted, DateTime.Now))
        {
            return "";
        }

        ledger.Record(DataFlow.Local, "Öğrenme", $"{NudgePolicy.Label(kind)} üst üste geri çevrildi, bir hafta susturuldu");
        return $" Üç kez hayır dedin; {NudgePolicy.Label(kind)} bir hafta söylemeyeceğim.";
    }

    private QuickReply NudgeCommand(string action)
    {
        if (Nudges is not { } policy)
        {
            return new QuickReply("Bu özellik hazır değil.");
        }

        switch (Simplify(action))
        {
            case "sustur":
                if (LastNudge is not { } kind || DateTime.Now - _lastNudgeAt > TimeSpan.FromMinutes(30))
                {
                    return new QuickReply("Son yarım saatte kendiliğimden bir şey söylemedim; hangisini susturmamı istediğini söyler misin? Ör. \"göz molasını kapat\" ya da Ayarlar'dan kapatabilirsin.");
                }

                policy.Silence(kind);
                ledger.Record(DataFlow.Local, "Öğrenme", $"{NudgePolicy.Label(kind)} kullanıcının isteğiyle susturuldu");
                return new QuickReply($"Tamam, {NudgePolicy.Label(kind)} artık söylemeyeceğim. Geri istersen \"kapattığın hatırlatmaları aç\" de.");
            case "ac":
                var opened = policy.Reset();
                return new QuickReply(opened == 0 ? "Susturduğum bir şey yok." : $"{opened} türü yeniden açtım; yine söyleyeceğim.");
            default:
                var silenced = policy.Silenced(DateTime.Now);
                if (silenced.Count == 0)
                {
                    return new QuickReply("Hiçbir hatırlatmayı susturmadım.");
                }

                ShowListCard("Susturulanlar", "", silenced.Select(item => new CardRow(NudgePolicy.Label(item.Kind), item.Until is { } until ? $"{until:d MMM}'e kadar" : "kalıcı")));
                return new QuickReply("Susturduklarım: " + TurkishGrammar.Join([.. silenced.Select(item => NudgePolicy.Label(item.Kind) + (item.Until is null ? "" : " (bir süreliğine)"))]) + ".");
        }
    }
}
