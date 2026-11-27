using Fineage.Accounts;
using Fineage.Zone.World;
using Xunit;
using Rig = Fineage.Zone.Tests.EconomyReconcileTests.Rig;

namespace Fineage.Zone.Tests;

/// <summary>Blocks every thread-pool thread, so it runs alone.</summary>
[CollectionDefinition(nameof(ThreadPoolStarvationCollection), DisableParallelization = true)]
public sealed class ThreadPoolStarvationCollection;

/// <summary>
/// #1110: a reconnect storm parked the thread pool in synchronous logout writes. The simulation loop resumed through
/// the pool after every wait, and the economy persist worker woke through it, so the tick stopped for 30 s, until
/// those writes timed out. The loop, its watchdog and the worker now block on their own threads; these tests starve
/// the pool on purpose and require the world to keep stepping and committing.
/// </summary>
[Collection(nameof(ThreadPoolStarvationCollection))]
public sealed class SimulationPoolStarvationTests
{
    const int Adena = 57;
    const int Scroll = 736;

    /// <summary>
    /// Parks far more work items than the pool can grow threads for in the test window, each blocked until disposed:
    /// anything that needs a pool thread to resume waits behind them.
    /// </summary>
    sealed class StarvedPool : IDisposable
    {
        readonly ManualResetEventSlim _release = new(false);
        int _parked;

        public StarvedPool()
        {
            ThreadPool.GetMinThreads(out int minWorkers, out _);
            int items = Math.Max(minWorkers, Environment.ProcessorCount) * 64;
            for (int i = 0; i < items; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    Interlocked.Increment(ref _parked);
                    _release.Wait();
                }, null);
            }

            // Every thread the pool has now is parked on the gate.
            SpinWait.SpinUntil(() => Volatile.Read(ref _parked) >= Math.Min(ThreadPool.ThreadCount, items), TimeSpan.FromSeconds(5));
        }

        public void Dispose() => _release.Set();
    }

    static void WaitUntilTicking(ZoneWorld world)
    {
        long first = world.SimulationTicks.Tick;
        Assert.True(SpinWait.SpinUntil(() => world.SimulationTicks.Tick > first + 2, TimeSpan.FromSeconds(10)),
            "the simulation loop did not start ticking");
    }

    static void Stop(ZoneWorld world, CancellationTokenSource stop)
    {
        stop.Cancel();
        try
        {
            world.WaitSimulationAsync().Wait(TimeSpan.FromSeconds(60));
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
        }
    }

    [Fact]
    public void The_simulation_keeps_its_tick_while_every_pool_thread_is_blocked()
    {
        var world = new WorldBench().World;
        using var stop = new CancellationTokenSource();
        world.StartSimulation(stop.Token);
        WaitUntilTicking(world);

        const int windowMs = 3_000;
        long before, after;
        using (new StarvedPool())
        {
            before = world.SimulationTicks.Tick;
            Thread.Sleep(windowMs);
            after = world.SimulationTicks.Tick;
        }

        Stop(world, stop);
        long expected = windowMs / ZoneSimulationSettings.StepMilliseconds;
        Assert.True(after - before >= expected / 2,
            $"the loop ran {after - before} steps in {windowMs} ms with the pool starved; a free loop runs about {expected}");
    }

    [Fact]
    public void An_economy_commit_lands_while_every_pool_thread_is_blocked()
    {
        var rig = new Rig();
        var bench = rig.Boot();
        var player = bench.EnterAsync("pse", "PsE", items: [new StoredItem(Adena, 100, -1), new StoredItem(Scroll, 5, -1)],
            x: 0, y: 0, z: 0).GetAwaiter().GetResult();
        int scrolls = player.Slot(Scroll);
        var world = bench.World;
        using var stop = new CancellationTokenSource();
        world.StartSimulation(stop.Token);
        WaitUntilTicking(world);

        Task<string?> drop;
        bool answered;
        using (new StarvedPool())
        {
            // The reply is held until the persist worker has written the drop and a step has drained its completion.
            drop = world.ExecuteAsync(player.Session, new DropItemCommand(scrolls, 2, 10, 0, 0), CancellationToken.None);
            answered = drop.Wait(TimeSpan.FromSeconds(5));
        }

        Stop(world, stop);
        Assert.True(answered, "the economy commit for the drop did not land within 5 s while the pool was starved");
        Assert.Null(drop.Result);
        Assert.Equal(3, player.Inventory.CountOf(Scroll));
    }
}
