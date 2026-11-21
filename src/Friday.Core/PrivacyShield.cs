using System.Text.RegularExpressions;

namespace Friday.Core;

/// <summary>
/// İnternete giden her sorgudan kişisel kimlik bilgileri çıkarılır: dil modeli bir aramaya yanlışlıkla TC kimlik
/// numarası, IBAN, kart ya da telefon numarası, e-posta adresi koyarsa bunlar bilgisayardan çıkmaz. Şifre ve kart
/// numarası gibi sırlar hafızaya da düz metin olarak yazılmaz (hafıza diskte açık durur). Sayılar sağlama
/// algoritmalarıyla (TC kimlik, Luhn) doğrulanır; sıradan sayılara ("2026 bütçesi", "15 dakika") dokunulmaz.
/// </summary>
public static partial class PrivacyShield
{
    /// <summary>Metnin internete gidebilecek hali ve çıkarılan bilgi türleri ("TC kimlik numarası", "IBAN"…).</summary>
    public static (string Clean, IReadOnlyList<string> Removed) Redact(string text)
    {
        var removed = new List<string>();
        var clean = Iban().Replace(text, _ => Note(removed, "IBAN"));
        clean = Email().Replace(clean, _ => Note(removed, "e-posta adresi"));
        clean = DigitRun().Replace(clean, match =>
        {
            var digits = string.Concat(match.Value.Where(char.IsAsciiDigit));
            if (IsTurkishId(digits))
            {
                return Note(removed, "TC kimlik numarası");
            }

            if (digits.Length is >= 13 and <= 19 && Luhn(digits))
            {
                return Note(removed, "kart numarası");
            }

            if (IsPhone(digits))
            {
                return Note(removed, "telefon numarası");
            }

            return match.Value;
        });
        clean = Spaces().Replace(clean, " ").Trim();
        return (clean, [.. removed.Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>Hafızaya düz metin yazılmaması gereken bir sır içeriyorsa türü ("şifre", "kart numarası"); yoksa null.</summary>
    public static string? Secret(string text)
    {
        if (Password().IsMatch(text))
        {
            return "şifre";
        }

        return DigitRun().Matches(text).Select(match => string.Concat(match.Value.Where(char.IsAsciiDigit)))
            .Any(digits => digits.Length is >= 13 and <= 19 && Luhn(digits) && !IsTurkishId(digits))
            ? "kart numarası"
            : null;
    }

    /// <summary>TC kimlik numarası: 11 hane, ilk hane sıfır değil, 10. ve 11. haneler sağlama.</summary>
    public static bool IsTurkishId(string digits)
    {
        if (digits.Length != 11 || digits[0] == '0' || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        var d = digits.Select(digit => digit - '0').ToArray();
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        var tenth = ((odd * 7) - even) % 10;
        if (tenth < 0)
        {
            tenth += 10;
        }

        return tenth == d[9] && (d.Take(10).Sum() % 10) == d[10];
    }

    private static bool Luhn(string digits)
    {
        var sum = 0;
        var doubled = false;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var value = digits[index] - '0';
            if (doubled)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            doubled = !doubled;
        }

        return sum % 10 == 0;
    }

    /// <summary>Türkiye cep ve sabit hatları: 05xx…, 5xx… (10 hane), +90/90 ile başlayanlar.</summary>
    private static bool IsPhone(string digits) =>
        (digits.Length == 10 && digits[0] == '5')
        || (digits.Length == 11 && digits[0] == '0' && digits[1] is '5' or '2' or '3' or '4')
        || (digits.Length == 12 && digits.StartsWith("90", StringComparison.Ordinal));

    private static string Note(List<string> removed, string kind)
    {
        removed.Add(kind);
        return "";
    }

    [GeneratedRegex(@"\bTR\s?\d{2}(?:\s?\d{4}){5}\s?\d{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex Iban();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")]
    private static partial Regex Email();

    // Boşluk, tire ya da noktayla gruplanmış uzun sayılar ("0532 123 45 67", "4111-1111-1111-1111"); +90 dahil.
    [GeneratedRegex(@"\+?\d(?:[\d]|[ .-](?=\d)){9,22}\d?")]
    private static partial Regex DigitRun();

    // Değer bir rakam ya da işaret taşımalı: "şifrem Kedi1234" sırdır, "Wi-Fi şifresi nasıl değişir" değildir.
    [GeneratedRegex(@"\b(?:şifre(?:m|si|miz)?|parola(?:m|sı)?|pin(?: kodu?m?)?|password)\b\s*(?:[:=]|şu|şudur|bu)?\s*(?=\S*[\d!@#$%^&*_.+-])\S{4,}", RegexOptions.IgnoreCase)]
    private static partial Regex Password();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}
