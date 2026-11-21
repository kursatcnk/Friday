using Friday.Core;

namespace Friday.UnitTests;

public class PrivacyShieldTests
{
    [Theory]
    [InlineData("10000000146 numaralı kişinin vergi borcu", "numaralı kişinin vergi borcu", "TC kimlik numarası")]
    [InlineData("TR33 0006 1005 1978 6457 8413 26 hangi bankanın", "hangi bankanın", "IBAN")]
    [InlineData("4111 1111 1111 1111 kartı geçerli mi", "kartı geçerli mi", "kart numarası")]
    [InlineData("0532 123 45 67 kimin numarası", "kimin numarası", "telefon numarası")]
    [InlineData("+90 532 123 45 67 kimin", "kimin", "telefon numarası")]
    [InlineData("ali.veli@ornek.com adresine kayıtlı hesap", "adresine kayıtlı hesap", "e-posta adresi")]
    public void KeepsIdentifiersOffTheInternet(string query, string clean, string kind)
    {
        var (text, removed) = PrivacyShield.Redact(query);
        Assert.Equal(clean, text);
        Assert.Equal([kind], removed);
    }

    [Theory]
    [InlineData("2026 bütçesi ne kadar")]
    [InlineData("21.11.2026 tarihinde ne oldu")]
    [InlineData("1 500 000 TL kaç dolar")]
    [InlineData("12345678901 sayısı asal mı")]
    [InlineData("İstanbul hava durumu")]
    public void LeavesOrdinaryNumbersAlone(string query)
    {
        var (text, removed) = PrivacyShield.Redact(query);
        Assert.Equal(query, text);
        Assert.Empty(removed);
    }

    [Theory]
    [InlineData("şifrem Kedi1234", "şifre")]
    [InlineData("Wi-Fi şifresi: Ev_2024!", "şifre")]
    [InlineData("kart numaram 4111 1111 1111 1111", "kart numarası")]
    [InlineData("Wi-Fi şifresi nasıl değiştirilir", null)]
    [InlineData("Netflix şifresini annem biliyor", null)]
    [InlineData("TC kimlik numaram 10000000146", null)]
    [InlineData("Kızımın adı Elif", null)]
    public void RecognizesSecretsThatMustNotBeStored(string fact, string? kind) =>
        Assert.Equal(kind, PrivacyShield.Secret(fact));

    [Theory]
    [InlineData("10000000146", true)]
    [InlineData("10000000147", false)]
    [InlineData("00000000146", false)]
    [InlineData("1000000014", false)]
    public void ChecksTurkishIdentityNumbers(string digits, bool valid) =>
        Assert.Equal(valid, PrivacyShield.IsTurkishId(digits));
}
