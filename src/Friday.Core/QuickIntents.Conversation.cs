using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>Yarım kalmış bir konuşmada (kelime çalışması, kontrol listesi, günlük sorusu) gelen sözün ona ait olup olmadığı.</summary>
public static partial class QuickIntents
{
    /// <summary>
    /// Bekleyen soruya cevap değil, araya giren ayrı bir istek mi? "100 dolar ne kadar", "yarın hava nasıl olacak?" ya da
    /// uzun bir cümle kelime çalışmasının ya da kontrol listesinin cevabı sanılmaz. <paramref name="shortAnswer"/>: beklenen
    /// cevap kısadır (kelimenin anlamı, "tamam", "yok"); günlük sorusu gibi serbest cevaplarda yalnızca açık sorular ayrılır.
    /// Kısa, tereddütlü cevaplar ("başarmak mı") cevap sayılmaya devam eder.
    /// </summary>
    public static bool IsAside(string text, bool shortAnswer)
    {
        var sentence = Normalize(text);
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var question = text.TrimEnd().EndsWith('?') || AsideQuestion().IsMatch(sentence);
        if (!shortAnswer)
        {
            // "Ne kadar yorucu bir gündü": soru sözcüğüyle başlayan bir günlük cevabı soru sayılmaz.
            return words >= 3 && question;
        }

        question |= QuestionStart().IsMatch(sentence);
        return (words >= 2 && sentence.Any(char.IsAsciiDigit)) || (words >= 3 && question) || words >= 6;
    }

    // "... ne kadar", "... kaç lira", "... nedir", "... ne demek", "... var mı", "... olacak mı", "... nasıl yapılır".
    [GeneratedRegex(@"(?:^| )(?:ne kadar|kaç(?: lira| tl| para| oldu| tane)?|nedir|ne demek|kimdir|nerede|nereye|ne zaman|nasıl(?: olacak| yapılır| olur)?|neden|niye)$|(?:^| )(?:mı|mi|mu|mü)(?:dır|dir|dur|dür|sın|sin|sun|sün|yım|yim|yum|yüm)?$")]
    private static partial Regex AsideQuestion();

    [GeneratedRegex(@"^(?:ne|nasıl|neden|niye|kaç|hangi|kim|nerede|ne zaman) ")]
    private static partial Regex QuestionStart();
}
