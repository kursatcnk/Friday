using Friday.Core;

namespace Friday.UnitTests;

public class SpeechEndpointDetectorTests
{
    private const int Rate = 16_000;

    private static SpeechEndpointDetector Create() => new(
        Rate, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));

    private static float[] Tone(double seconds, float amplitude) =>
        Enumerable.Range(0, (int)(seconds * Rate))
            .Select(index => (float)(Math.Sin(2 * Math.PI * 220 * index / Rate) * amplitude))
            .ToArray();

    private static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    [Fact]
    public void SpeechFollowedBySilenceFinishes()
    {
        var detector = Create();
        detector.Process(Silence(0.3));
        Assert.Equal(EndpointState.Speaking, detector.Process(Tone(1.0, 0.3f)));
        Assert.Equal(EndpointState.Speaking, detector.Process(Silence(0.5)));
        Assert.Equal(EndpointState.Finished, detector.Process(Silence(0.4)));
    }

    [Fact]
    public void ShortPausesInsideASentenceDoNotFinish()
    {
        var detector = Create();
        detector.Process(Tone(0.6, 0.3f));
        detector.Process(Silence(0.4));
        Assert.Equal(EndpointState.Speaking, detector.Process(Tone(0.6, 0.3f)));
    }

    [Fact]
    public void QuietRoomTimesOutWithoutSpeech()
    {
        var detector = Create();
        Assert.Equal(EndpointState.NoSpeech, detector.Process(Silence(3.1)));
    }

    [Fact]
    public void SteadyBackgroundNoiseIsNotSpeech()
    {
        var detector = Create();
        var random = new Random(3);
        var noise = Enumerable.Range(0, Rate * 4).Select(_ => (float)((random.NextDouble() - 0.5) * 0.02)).ToArray();
        Assert.Equal(EndpointState.NoSpeech, detector.Process(noise));
    }

    [Fact]
    public void ClickShorterThanSixtyMillisecondsIsIgnored()
    {
        var detector = Create();
        detector.Process(Tone(0.03, 0.5f));
        Assert.Equal(EndpointState.Waiting, detector.Process(Silence(0.5)));
    }

    [Fact]
    public void NonStopTalkingStopsAtMaximumDuration()
    {
        var detector = Create();
        Assert.Equal(EndpointState.Finished, detector.Process(Tone(10.1, 0.3f)));
    }
}
