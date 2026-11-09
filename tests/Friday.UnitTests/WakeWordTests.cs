using Friday.Infrastructure;

namespace Friday.UnitTests;

/// <summary>Modeller yalnızca setup.ps1 çalıştırılmış bilgisayarda vardır; CI'da bu testler kendini atlar.</summary>
public class WakeWordTests
{
    private const string Folder = @"D:\Friday\models\wakeword";

    [Fact]
    public void SilenceAndNoiseNeverWake()
    {
        if (!WakeWordEngine.IsInstalled(Folder, "hey_jarvis"))
        {
            return;
        }

        using var engine = new WakeWordEngine(Folder, "hey_jarvis");
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
        if (!WakeWordEngine.IsInstalled(Folder, "hey_jarvis"))
        {
            return;
        }

        using var engine = new WakeWordEngine(Folder, "hey_jarvis");
        Assert.Equal(16, engine.FeatureFrames);
        Assert.False(WakeWordEngine.IsInstalled(Folder, "ok_nabu"));
    }

    [Fact]
    public void RejectsWrongChunkSize()
    {
        if (!WakeWordEngine.IsInstalled(Folder, "hey_jarvis"))
        {
            return;
        }

        using var engine = new WakeWordEngine(Folder, "hey_jarvis");
        Assert.Throws<ArgumentException>(() => engine.CreateStream().Process(new short[100]));
    }
}
