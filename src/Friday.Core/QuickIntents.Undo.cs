using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>"Geri al", "geri getir", "son değişikliklerin neler" (bkz. <see cref="UndoLog"/>).</summary>
public static partial class QuickIntents
{
    private static QuickCommand? Undo(string sentence) =>
        UndoRequest().IsMatch(sentence) ? Command("geri_al", ("islem", "geri_al"))
        : RedoRequest().IsMatch(sentence) ? Command("geri_al", ("islem", "geri_getir"))
        : ChangesQuestion().IsMatch(sentence) ? Command("geri_al", ("islem", "liste"))
        : CorrectionsReset().IsMatch(sentence) ? Command("duzeltme", ("islem", "sifirla"))
        : CorrectionsList().IsMatch(sentence) ? Command("duzeltme", ("islem", "listele"))
        : null;

    [GeneratedRegex(@"^(?:öğrendiğin |bütün )?düzeltmeleri (?:unut|sıfırla|sil|temizle)$")]
    private static partial Regex CorrectionsReset();

    [GeneratedRegex(@"^(?:öğrendiğin )?düzeltmeler(?:in)? (?:neler|ne)$|^(?:öğrendiğin )?düzeltmeleri (?:say|söyle|göster)$|^hangi sözleri komut sanmıyorsun$")]
    private static partial Regex CorrectionsList();

    [GeneratedRegex(@"^(?:(?:hayır|yanlış oldu|yanlışlıkla oldu|vazgeçtim|dur),? )?(?:(?:son |az önce |demin |az önceki |deminki )?(?:yaptığını|yaptığın şeyi|yaptığın değişikliği|son işlemi|son değişikliği|eklediğini|kaydettiğini|değiştirdiğini|sildiğini|onu|bunu|şunu|hepsini) )?geri (?:al|alır mısın|alsana|alabilir misin)$")]
    private static partial Regex UndoRequest();

    [GeneratedRegex(@"^(?:geri aldığını|onu|bunu)? ?(?:geri getir|geri getirir misin|yeniden yap|tekrar uygula)$|^geri almayı geri al$|^geri aldığını geri al$")]
    private static partial Regex RedoRequest();

    [GeneratedRegex(@"^(?:son )?(?:yaptığın )?(?:değişiklikler(?:in)?|değişiklikleri) (?:neler|ne|say|söyle|göster)$|^(?:son olarak |bugün |az önce )?(?:kayıtlarımda )?neleri değiştirdin$|^son yaptıkların(?:ı)? (?:neler|say|söyle)$|^ne(?:leri)? (?:değiştirdin|kaydettin)$")]
    private static partial Regex ChangesQuestion();
}
