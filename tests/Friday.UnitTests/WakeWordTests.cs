using Friday.Infrastructure;

namespace Friday.UnitTests;

/// <summary>Modeller yalnızca setup.ps1 çalıştırılmış bilgisayarda vardır; CI'da bu testler kendini atlar.</summary>
public class WakeWordTests
{
    private const string Folder = @"D:\Friday\models\wakeword";

    [Fact]
    public void SilenceAndNoiseNeverWake()
    {
        if (!WakeWordEngine.IsInstalled(Folder, "hey_friday"))
        {
            return;
        }

        using var engine = new WakeWordEngine(Folder, "hey_friday");
        var tracker = engine.CreateStream();
        var random = new Random(3);
        var chunk = new short[WakeWordEngine.ChunkSamples];
        var highest = 0f;
        for (var step = 0; step < 100; step++)
        {
            // Yarısı tam sessizlik (sanal mikrofon), yarısı oda gürültüsü.
            for (var index = 0; index < chunk.Length; index++)
            {
                chunk[index] = step % 2 == 0 ? (short)0 : (short)random.Next(-300, 300);
            }

            highest = Math.Max(highest, tracker.Process(chunk));
        }

        Assert.True(highest < 0.2f, $"En yüksek puan {highest}");
    }

    [Fact]
    public void FindsTheVersionedModelFile()
    {
        if (!WakeWordEngine.IsInstalled(Folder, "hey_friday"))
        {
            return;
        }

        using var engine = new WakeWordEngine(Folder, "hey_friday");
        Assert.Equal(16, engine.FeatureFrames);
        Assert.False(WakeWordEngine.IsInstalled(Folder, "ok_nabu"));
    }

    [Fact]
    public void RejectsWrongChunkSize()
    {
        if (!WakeWordEngine.IsInstalled(Folder, "hey_friday"))
        {
            return;
        }

        using var engine = new WakeWordEngine(Folder, "hey_friday");
        Assert.Throws<ArgumentException>(() => engine.CreateStream().Process(new short[100]));
    }
}

public class NoiseFloorTests
{
    [Fact]
    public void FanNoiseBecomesQuietButSpeechNeverDoes()
    {
        var floor = new WakeWordTracker.NoiseFloor();
        var noise = new Random(3);
        var quiet = 0;
        for (var chunk = 0; chunk < 400; chunk++)
        {
            // Fanı duyan bir kulaklık: tepe 300-450, sabit eşiğin (200) hep üstünde.
            quiet += floor.IsQuiet(noise.Next(300, 450)) ? 1 : 0;
        }

        Assert.True(quiet > 300, $"{quiet}");
        Assert.False(floor.IsQuiet(4000));
        Assert.True(floor.QuietShare > 0.7);
    }

    [Fact]
    public void LoudBackgroundNeverHidesTheWakeWord()
    {
        var floor = new WakeWordTracker.NoiseFloor();
        for (var chunk = 0; chunk < 5000; chunk++)
        {
            floor.IsQuiet(2500);
        }

        // Müzik açıkken taban yükselse de eşik ~-29 dBFS'i geçmez: üstüne söylenen kelime tam işlenir.
        Assert.False(floor.IsQuiet(1300));
    }
}
