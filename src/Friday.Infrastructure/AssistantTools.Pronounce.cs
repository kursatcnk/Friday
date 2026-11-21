using Friday.Core;

namespace Friday.Infrastructure;

/// <summary>"Claude'u Klod diye oku": Friday'in sesi yabancı adları kullanıcının öğrettiği gibi okur (bkz. <see cref="Pronunciation"/>).</summary>
public sealed partial class AssistantTools
{
    private QuickReply PronounceCommand(string action, string word, string spoken)
    {
        var written = TurkishGrammar.Capitalize(word.Trim().Trim('"', '\'', '.', ','));

        // Ayar dosyasından okunan sözlük büyük/küçük harf ayırabilir; aynı kelimenin eski kaydı bulunup değiştirilir.
        var known = Settings.Pronunciations.Keys.FirstOrDefault(key => string.Equals(key, written, StringComparison.OrdinalIgnoreCase));
        if (written.Length < 2)
        {
            return new QuickReply("Hangi kelimeyi nasıl okuyacağımı anlayamadım; ör. \"Claude'u Klod diye oku\" de.");
        }

        if (action == "unut")
        {
            var removed = known is not null && Settings.Pronunciations.Remove(known);
            if (removed)
            {
                SaveSettings?.Invoke(Settings);
            }

            return new QuickReply(removed ? $"Tamam, {written} kelimesini yine kendi bildiğim gibi okuyacağım." : $"{written} için bana öğretilmiş bir okunuş yok.");
        }

        var sound = spoken.Trim().Trim('"', '\'', '.', ',');
        if (sound.Length < 2 || string.Equals(sound, written, StringComparison.OrdinalIgnoreCase))
        {
            return new QuickReply("Nasıl okumam gerektiğini anlayamadım; ör. \"Claude'u Klod diye oku\" de.");
        }

        if (known is not null)
        {
            Settings.Pronunciations.Remove(known);
        }

        Settings.Pronunciations[written] = sound;
        SaveSettings?.Invoke(Settings);
        ledger.Record(DataFlow.Local, "Okunuş", $"{written} → {sound}");
        // Cevaptaki kelime yeni okunuşla seslendirilir; kullanıcı doğru olup olmadığını hemen duyar.
        return new QuickReply($"Tamam, bundan sonra {written} kelimesini böyle okuyacağım: {written}.");
    }
}
