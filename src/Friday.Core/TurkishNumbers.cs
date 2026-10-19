using System.Text;

namespace Friday.Core;

/// <summary>
/// Rakamları Türkçe okunuşuna çevirir. Dil modeli sayıları yazıya dökerken hata yapabiliyor ("11:05" → "on altı
/// beş"); bu yüzden model rakam yazar, çeviriyi bu sınıf yapar.
/// </summary>
public static class TurkishNumbers
{
    private static readonly string[] Ones = ["", "bir", "iki", "üç", "dört", "beş", "altı", "yedi", "sekiz", "dokuz"];
    private static readonly string[] Tens = ["", "on", "yirmi", "otuz", "kırk", "elli", "altmış", "yetmiş", "seksen", "doksan"];
    private static readonly (long Value, string Name)[] Scales = [(1_000_000_000_000, "trilyon"), (1_000_000_000, "milyar"), (1_000_000, "milyon"), (1_000, "bin")];

    public static string ToWords(long number)
    {
        if (number == 0)
        {
            return "sıfır";
        }

        if (number < 0)
        {
            return "eksi " + ToWords(-number);
        }

        var words = new List<string>();
        foreach (var (value, name) in Scales)
        {
            var count = number / value;
            if (count == 0)
            {
                continue;
            }

            // "bir bin" denmez, "bin" denir; "bir milyon" ise doğrudur.
            if (!(value == 1_000 && count == 1))
            {
                words.Add(ToWords(count));
            }

            words.Add(name);
            number %= value;
        }

        if (number >= 100)
        {
            var hundreds = number / 100;
            if (hundreds > 1)
            {
                words.Add(Ones[hundreds]);
            }

            words.Add("yüz");
            number %= 100;
        }

        if (number >= 10)
        {
            words.Add(Tens[number / 10]);
        }

        if (number % 10 > 0)
        {
            words.Add(Ones[number % 10]);
        }

        return string.Join(' ', words);
    }

    /// <summary>"14:05" → "on dört sıfır beş", "09:30" → "dokuz otuz", "16:00" → "on altı".</summary>
    public static string TimeToWords(int hour, int minute)
    {
        var builder = new StringBuilder(ToWords(hour));
        if (minute > 0)
        {
            builder.Append(' ');
            if (minute < 10)
            {
                builder.Append("sıfır ");
            }

            builder.Append(ToWords(minute));
        }

        return builder.ToString();
    }
}
