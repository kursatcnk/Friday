using System.Diagnostics;
using Friday.Infrastructure;

namespace Friday.UnitTests;

public sealed class ChildProcessJobTests
{
    [Fact]
    public void ClosingTheJobEndsItsProcessesAndTheirChildren()
    {
        // cmd, ping bitince waitfor açar; waitfor işe eklendikten sonra doğar, tıpkı Ollama'nın llama-server'ı gibi.
        using var parent = StartSleeper("ping -n 2 127.0.0.1 >nul & waitfor /t 30 FridayJobTest");
        var before = WaitforIds();
        var job = new ChildProcessJob();
        try
        {
            Assert.True(job.TryAdd(parent));
            var child = WaitForNewWaitfor(before);

            job.Dispose();

            Assert.True(parent.WaitForExit(TimeSpan.FromSeconds(5)));
            Assert.True(child.WaitForExit(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            job.Dispose();
            if (!parent.HasExited)
            {
                parent.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public void AnExitedProcessIsNotAdded()
    {
        using var process = StartSleeper("exit 0");
        process.WaitForExit();
        using var job = new ChildProcessJob();

        Assert.False(job.TryAdd(process));
    }

    private static Process StartSleeper(string command) =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("cmd başlatılamadı.");

    private static HashSet<int> WaitforIds() => Process.GetProcessesByName("waitfor").Select(p => p.Id).ToHashSet();

    private static Process WaitForNewWaitfor(HashSet<int> before)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            // ping yaklaşık bir saniye sürer; waitfor ondan sonra açılır.
            Thread.Sleep(250);
            var fresh = Process.GetProcessesByName("waitfor").Where(p => !before.Contains(p.Id)).ToList();
            if (fresh.Count > 0)
            {
                return fresh[^1];
            }
        }

        throw new TimeoutException("waitfor açılmadı.");
    }
}
