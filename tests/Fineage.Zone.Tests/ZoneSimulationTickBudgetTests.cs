using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Idle full-world steps keep combat registries empty (no wall-clock timing in the default suite).</summary>
public sealed class ZoneSimulationTickBudgetTests
{
    [Fact]
    public void Idle_full_world_step_keeps_thinking_registry_empty()
    {
        var bench = new WorldBench(includeEnabledMaps: true, simulationSeed: 42, allowAmbientBoats: false);
        Assert.Equal(0, bench.World.ActiveSetCounts.Thinking);

        var clock = new MutableClock { Now = ManualSimulationDriver.FixedStartUtc };
        var driver = new ManualSimulationDriver(bench.World, clock);
        driver.Step(120);

        Assert.Equal(0, bench.World.ActiveSetCounts.Thinking);
    }
}