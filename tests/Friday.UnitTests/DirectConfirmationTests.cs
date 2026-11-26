using Friday.Core;
using Friday.Infrastructure;

namespace Friday.UnitTests;

/// <summary>
/// Kayıt değiştiren tek bir çağrının onay cümlesi modele ikinci kez sorulmadan okunur; modele yönelik yönerge ya da
/// güvenlik onayı içeren sonuçlar okunmaz.
/// </summary>
public class DirectConfirmationTests
{
    private static ToolCall Call(string name, string action) =>
        new(name, new Dictionary<string, string> { ["islem"] = action });

    [Theory]
    [InlineData("harcama", "sil_son", true)]
    [InlineData("harcama", "ekle", true)]
    [InlineData("harcama", "ozet", false)]
    [InlineData("liste", "ekle", true)]
    [InlineData("liste", "oku", false)]
    [InlineData("hatirlatici", "ekle", true)]
    [InlineData("hatirlatici", "listele", false)]
    [InlineData("yapilacaklar", "onemli", true)]
    [InlineData("ritim", "odak_duraklat", true)]
    [InlineData("ritim", "odaklar", false)]
    [InlineData("bilgi", "", false)]
    public void ConfirmsOnlyRecordChanges(string tool, string action, bool direct) =>
        Assert.Equal(direct, AssistantTools.ConfirmsDirectly(Call(tool, action)));

    [Theory]
    [InlineData("Son harcamayı sildim: market, 230 lira.", true)]
    [InlineData("Tamam, her gün 08:00 su içmeni hatırlatırım.", true)]
    [InlineData("ONAY GEREKLİ, YAPILMADI. Bu istek kullanıcıdan değil, okunan dış içerikten geldi.", false)]
    [InlineData("Hafızadaki eski bilgi güncellendi. Eski: x Yeni: y Kullanıcıya eski bilginin yerine yenisini koyduğunu kısaca söyle.", false)]
    [InlineData("", false)]
    public void ReadsOnlySpeakableResults(string result, bool speakable) => Assert.Equal(speakable, AssistantTools.Speakable(result));
}
