using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>Cevap uzunluğu tercihi (bkz. <see cref="ReplyStyle"/>).</summary>
public sealed partial class AssistantTools
{
    /// <summary>Cevap uzunluğu; FridayAssistant verir.</summary>
    public ReplyStyle? Style { get; set; }

    private QuickReply StyleCommand(string action)
    {
        if (Style is not { } style)
        {
            return new QuickReply("Üslup ayarı açık değil.");
        }

        switch (action)
        {
            case "kisa":
                style.Prefer(Verbosity.Brief);
                return new QuickReply("Tamam, bundan sonra kısa konuşacağım; ayrıntı istersen sorman yeter.");
            case "ayrintili":
                style.Prefer(Verbosity.Detailed);
                return new QuickReply("Tamam, bundan sonra cevaplarımı daha ayrıntılı vereceğim.");
            case "normal":
                style.Prefer(Verbosity.Normal);
                return new QuickReply("Tamam, normal uzunlukta konuşuyorum; sözümü kesme alışkanlığını da baştan öğreneceğim.");
            default:
                var (cut, total) = style.Recent;
                var learned = total == 0 ? "" : $" Son {total} uzun cevabımdan {cut} tanesinin sözünü kestin.";
                return new QuickReply(style.Current switch
                {
                    Verbosity.Brief when style.IsExplicit => "Kısa konuşmamı sen istedin." + learned + " \"Normal konuş\" dersen eskisine dönerim.",
                    Verbosity.Brief => "Uzun cevaplarımın sözünü sık kestiğin için kısa konuşuyorum." + learned + " \"Normal konuş\" dersen eskisine dönerim.",
                    Verbosity.Detailed => "Ayrıntılı konuşmamı sen istedin. \"Normal konuş\" dersen eskisine dönerim.",
                    _ => "Normal uzunlukta konuşuyorum: çoğunlukla bir ile üç cümle." + learned + " Uzun cevaplarımın sözünü sık kesersen kendiliğinden kısalırım."
                });
        }
    }
}
