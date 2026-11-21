using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Step 12 of <c>docs/architecture/zone-simulation.md</c>: with <c>_gate</c> gone, a world that a loop or pump drives
/// is touched only by the thread running a step. Everything else submits commands or requests, which the next step
/// applies; requests are not logged, and a command applied around a projection is logged once.
/// </summary>
public sealed class WorldAffinityTests
{
    [Fact]
    public async Task A_driven_world_refuses_touches_off_the_simulation_thread_and_serves_requests_on_it()
    {
        var kit = new NetTestKit(simulationSeed: 1);
        var (session, entity) = await kit.EnterAsync();
        int id = entity.ObjectId;

        var inline = kit.World.OnSimulationAsync(session, "probe", () => Lookup(kit.World, id), CancellationToken.None);
        Assert.True(inline.IsCompletedSuccessfully, "a world nothing drives runs the body on the caller");
        Assert.Equal(id, await inline);

        using var cts = new CancellationTokenSource();
        var pump = kit.World.PumpCommandsAsync(cts.Token);
        try
        {
            var refused = Assert.Throws<InvalidOperationException>(() => Lookup(kit.World, id));
            Assert.Contains("off the simulation thread", refused.Message, StringComparison.Ordinal);

            var log = kit.World.RecordSimulation();
            Assert.Equal(id, await kit.World.OnSimulationAsync(session, "probe", () => Lookup(kit.World, id), CancellationToken.None));
            Assert.Equal(id, kit.World.OnStepForTest(() => Lookup(kit.World, id)));
            Assert.DoesNotContain(log.Entries, e => e is CommandLogEntry);
        }
        finally
        {
            await cts.CancelAsync();
            await pump;
        }
    }

    [Fact]
    public async Task A_command_applied_around_a_projection_is_logged_once_and_seen_before_and_after()
    {
        var kit = new NetTestKit(simulationSeed: 1);
        var (session, _) = await kit.EnterAsync();
        var (_, peer) = await kit.EnterAsync(x: NetTestKit.ElfX + 100);

        using var cts = new CancellationTokenSource();
        var pump = kit.World.PumpCommandsAsync(cts.Token);
        try
        {
            var log = kit.World.RecordSimulation();
            var (before, error, after) = await kit.World.ExecuteAroundAsync(
                session,
                new SelectTargetCommand(peer.ObjectId),
                apply =>
                {
                    int was = session.SelectedTargetId;
                    string? rejected = apply();
                    return (was, rejected, session.SelectedTargetId);
                },
                CancellationToken.None);

            Assert.Equal(0, before);
            Assert.Null(error);
            Assert.Equal(peer.ObjectId, after);
            var logged = Assert.Single(log.Entries.OfType<CommandLogEntry>());
            Assert.IsType<SelectTargetCommand>(logged.Command);
        }
        finally
        {
            await cts.CancelAsync();
            await pump;
        }
    }

    static int Lookup(ZoneWorld world, int objectId) =>
        world.TryGetEntityById(objectId, out var found) ? found.ObjectId : 0;
}
