namespace Friday.Infrastructure;

/// <summary>Bir sohbete bağlı yarım kalmış durumlar.</summary>
public sealed partial class AssistantTools
{
    /// <summary>
    /// Sohbet kapatılınca, yeni sohbet açılınca ya da başka bir sohbete geçilince yarım kalan her şey bırakılır: kelime
    /// çalışması, cevap beklenen soru (günlük, toplantı notu), sesli kontrol listesi, onay bekleyen işlem ve önündeki belge.
    /// Eskiden kelime çalışması sohbet kapatıldıktan sonra da sürüyor, "100 dolar ne kadar" kelime cevabı sanılıyordu.
    /// </summary>
    public void EndConversation()
    {
        _awaitingAnswer = null;
        _quiz.Clear();
        _quizIndex = 0;
        _checklist = null;
        _pendingAction = null;
        _front = null;
    }
}
