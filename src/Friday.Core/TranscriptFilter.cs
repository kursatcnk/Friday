namespace Friday.Core;

/// <summary>
/// Whisper sessiz ya da çok kısa kayıtlarda eğitim verisindeki altyazı kalıplarını "duyabilir". Bunlar
/// kullanıcının söylediği bir şey sayılmaz.
/// </summary>
public static class TranscriptFilter
{
    private static readonly string[] Hallucinations =
    [
        "altyazı m.k.",
        "altyazı m.k",
        "izlediğiniz için teşekkür ederim",
        "izlediğiniz için teşekkürler",
        "abone olmayı unutmayın",
        "thanks for watching",
        "thank you for watching",
        "[müzik]",
        "[music]",
    ];

    public static string Normalize(string transcript)
    {
        var text = transcript.Trim();
        // ToLowerInvariant "İ"yi "i̇"ye çevirir; Türkçe kurallarıyla küçültülür.
        var key = text.Trim('.', '!', ' ').ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        if (key.Length == 0 || Hallucinations.Any(item => key == item.Trim('.')))
        {
            return "";
        }

        return text;
    }
}
