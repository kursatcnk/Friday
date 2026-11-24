using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>
/// Yazma köprüsü (bkz. <see cref="Writing"/>): "buna cevap yaz: perşembe olur ama üçten sonra" denince Friday önündeki
/// e-postayı ya da mesajı okur, gönderilecek cevabı mesajın dilinde yazar ve panoya koyar; Türkçesini kısaca söyler.
/// "Daha kibar yap", "kısalt", "şunu da ekle: …" son taslağı değiştirir; sık istenen düzeltmeler üslup olarak öğrenilir.
/// </summary>
public sealed partial class AssistantTools
{
    private WritingDraft? _draft;
    private bool _draftHintGiven;

    /// <summary>Kullanıcının yazı üslubu (App verir).</summary>
    public WritingStyle? WritingStyle { get; set; }

    /// <summary>Düzeltilebilecek bir taslak var mı ("kısalt" ancak o zaman taslağa gider).</summary>
    public bool HasDraft => _draft is { } draft && DateTime.Now - draft.At < Writing.DraftLife;

    private async Task<string> WriteAsync(string action, string content, string language, CancellationToken cancellationToken)
    {
        var signature = Settings.Snippets.TryGetValue("imza", out var stored) ? stored : "";
        if (Simplify(action) == "duzelt")
        {
            if (!HasDraft || _draft is not { } draft)
            {
                return "Düzeltecek bir taslak yok. Önce \"buna cevap yaz: …\" ya da \"şunu İngilizce yaz: …\" de.";
            }

            var revised = Writing.Parse(await chat.CompleteAsync(
                Settings.OllamaModel,
                Writing.Instruction(draft.Language, Settings.UserName, signature, WritingStyle?.Notes ?? []),
                Writing.Revision(draft, content),
                null, null, cancellationToken).ConfigureAwait(false), draft.Language);
            if (revised.Text.Length == 0)
            {
                return "Taslağı değiştiremedim, bir daha söyler misin?";
            }

            var kind = Writing.Kind(content);
            var learned = WritingStyle?.Learn(kind) == true;
            return Deliver(draft with { Text = revised.Text, Turkish = revised.Turkish, At = DateTime.Now }, true, learned ? kind : "");
        }

        var replying = Simplify(action) == "cevap";
        var context = "";
        if (replying && await FrontTextAsync(false, cancellationToken).ConfigureAwait(false) is var (_, text))
        {
            context = text.Length > 4000 ? text[..4000] : text;
        }

        var target = language.Trim().Length > 0 ? language.Trim() : context.Length > 0 ? "" : "İngilizce";
        var output = Writing.Parse(await chat.CompleteAsync(
            Settings.OllamaModel,
            Writing.Instruction(target, Settings.UserName, signature, WritingStyle?.Notes ?? []),
            Writing.Request(context, content),
            null, null, cancellationToken).ConfigureAwait(false), target.Length > 0 ? target : "İngilizce");
        if (output.Text.Length == 0)
        {
            return "Taslağı yazamadım, bir daha dener misin?";
        }

        var reply = Deliver(new WritingDraft(context, content, output.Language, output.Text, output.Turkish, DateTime.Now), false, "");
        return replying && context.Length == 0 ? "Önünde cevaplanacak bir yazı göremedim, söylediğinden yazdım. " + reply : reply;
    }

    /// <summary>Taslağı panoya koyar, kartta gösterir ve ne yapıldığını söyler.</summary>
    private string Deliver(WritingDraft draft, bool revised, string learnedKind)
    {
        _draft = draft;
        ClipboardText.Set(draft.Text);
        ledger.Record(DataFlow.Local, "Yazma köprüsü", $"{draft.Language} taslak panoya kondu ({draft.Text.Length} karakter)");
        ShowCard(new AnswerCard($"Taslak · {draft.Language}", "")
        {
            Rows = [.. draft.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxCardRows).Select(line => new CardRow("", line))]
        });

        var turkish = draft.Language.StartsWith("Türk", StringComparison.OrdinalIgnoreCase);
        var said = revised
            ? "Değiştirdim, yenisi panoda."
            : $"{draft.Language} {(draft.Context.Length > 0 ? "cevabı" : "metni")} panoya koydum, Ctrl V ile yapıştırabilirsin.";
        if (!turkish && draft.Turkish.Length > 0)
        {
            said += " Kısaca: " + draft.Turkish.TrimEnd('.') + ".";
        }

        if (learnedKind.Length > 0)
        {
            var adjective = learnedKind switch { "kisa" => "kısa", "uzun" => "daha ayrıntılı", "resmi" => "resmi", "samimi" => "samimi", _ => "kibar" };
            said += $" Bunu sık istediğini fark ettim; bundan sonra taslakları baştan {adjective} yazacağım.";
        }
        else if (!revised && !_draftHintGiven)
        {
            _draftHintGiven = true;
            said += " Beğenmezsen \"daha kibar yap\", \"kısalt\" ya da \"şunu da ekle: …\" de.";
        }

        return said;
    }
}
