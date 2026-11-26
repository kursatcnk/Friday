namespace Friday.Infrastructure;

/// <summary>Bir sohbete bağlı yarım kalmış durumlar.</summary>
public sealed partial class AssistantTools
{
    /// <summary>
    /// Sohbet kapatılınca, yeni sohbet açılınca ya da başka bir sohbete geçilince yarım kalan her şey bırakılır:
    /// cevap beklenen soru (günlük, toplantı notu), sesli kontrol listesi, onay bekleyen işlem ve önündeki belge.
    /// </summary>
    public void EndConversation()
    {
        _awaitingAnswer = null;
        _checklist = null;
        _pendingAction = null;
        _front = null;
        _draft = null;
        // Okunan içerik yeni sohbete taşınmaz.
        _untrustedAt = DateTime.MinValue;
    }
}
