using System.Diagnostics;
using Fineage.Zone.World;
using Xunit;
using Xunit.Abstractions;

namespace Fineage.Zone.Tests;

/// <summary>
/// The knownlist reconcile path runs once per moving entity per movement stage, so its per-call allocation is the
/// thing that matters, not its asymptotics: every structure it touches is already a hash set. This measures the
/// per-refresh cost at a realistic watcher count so a claim about it can be checked against a number.
/// </summary>
[Trait("Category", "Slow")]
public sealed class KnownlistReconcileBenchTests(ITestOutputHelper output)
{
    const int Peers = 40;
    const int Refreshes = 20_000;

    [Fact]
    public async Task Refresh_allocates_no_per_call_set_when_the_reconcile_scratch_already_holds_the_before_list()
    {
        const int x0 = 46045, y0 = 41251, z0 = -3440;
        var bench = new WorldBench();
        var self = await bench.EnterAsync(x: x0, y: y0, z: z0);
        for (int i = 0; i < Peers; i++)
            await bench.EnterAsync(x: x0 + 20 + (i % 10) * 20, y: y0 + (i / 10) * 20, z: z0);

        // Warm every lazily-built structure so the measurement is the steady-state path, not first-call setup.
        for (int i = 0; i < 200; i++)
            bench.World.RefreshMovingKnownlistMidRunForTests(self.Player);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < Refreshes; i++)
            bench.World.RefreshMovingKnownlistMidRunForTests(self.Player);
        clock.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"peers={self.Player.Knownlist.Count} refreshes={Refreshes} " +
                         $"allocated={allocated} B total, {allocated / (double)Refreshes:F1} B/refresh, " +
                         $"{clock.Elapsed.TotalMilliseconds / Refreshes:F4} ms/refresh");

        // A refresh must not build copies of the knownlist: the before set comes from the reconcile scratch and the
        // set diffs come from one thread-static bucket. What is left is the turn struct and the lazy added/removed
        // lists the diff actually needs (none when nothing entered or left).
        Assert.True(allocated / (double)Refreshes < 1000,
            $"knownlist refresh allocated {allocated / (double)Refreshes:F1} B per call for {self.Player.Knownlist.Count} known ids");
    }
}
