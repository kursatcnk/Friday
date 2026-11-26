using Friday.Core;

namespace Friday.UnitTests;

/// <summary>
/// Son cevaba dönük kısa sorular: günün durumu eklenmez, yoksa model "ilki"ni durum satırındaki işlere bağlıyordu
/// (ölçümde "İzmir'de gezilecek üç yer → ilki nerede" sorusuna "Kirayı ödemen gerekiyor" dendi).
/// </summary>
public class BackReferenceTests
{
    [Theory]
    [InlineData("ilki nerede", true)]
    [InlineData("İkincisi ne kadar?", true)]
    [InlineData("orası uzak mı", true)]
    [InlineData("bunlardan hangisi daha ucuz", true)]
    [InlineData("sonuncusunun adresi ne", true)]
    [InlineData("İzmir'de gezilecek üç yer söyle", false)]
    [InlineData("yarın dişçim var mı", false)]
    [InlineData("ilk randevum ne zaman", false)]
    [InlineData("hangisi daha iyi sence telefon olarak iPhone mu Samsung mu", false)]
    public void RecognizesQuestionsAboutTheLastAnswer(string question, bool expected) =>
        Assert.Equal(expected, ConversationStyle.RefersToLastAnswer(question));
}
