using System.Diagnostics.CodeAnalysis;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// The simulation step as a <see cref="RegionTickScheduler"/> pipeline. Regen and abnormals run per region; the
/// remaining systems are world stages in the order the loop always ran them. See
/// <c>docs/architecture/zone-simulation.md</c> for the migration plan.
/// </summary>
public sealed partial class ZoneWorld
{
    RegionTickScheduler _simulationTicks;
    IRegionStage _regenPlayersStage;
    IRegionStage _regenNpcsStage;
    IRegionStage _abnormalsStage;

    internal RegionTickScheduler SimulationTicks => _simulationTicks;

    /// <summary>One full simulation step on demand (tests and admin diagnostics), same body the loop runs.</summary>
    internal void StepSimulation()
    {
        lock (_gate)
        {
            BeginTickOutbox();
            try
            {
                _simulationTicks.Step(_clock().ToUnixTimeMilliseconds());
            }
            finally
            {
                EndTickOutboxRecording();
            }
        }

        FlushTickOutbox();
    }

    [MemberNotNull(nameof(_simulationTicks), nameof(_regenPlayersStage), nameof(_regenNpcsStage), nameof(_abnormalsStage))]
    void InitSimulation(IRegionPartition partition, ISimulationObserver observer)
    {
        _regenPlayersStage = new RegionStage("regen.players", CollectRegenPlayers, (_, id) => RegenPlayer(id));
        _regenNpcsStage = new RegionStage("regen.npcs", CollectRegenNpcs, (_, id) => RegenNpc(id));
        _abnormalsStage = new RegionStage("abnormals", CollectAbnormals, (tick, id) => TickEntityAbnormals(id, tick.NowUnixMs));

        // Region bodies still fan out through world-wide Players() scans and party state, so regions run one after
        // another under _gate until that state is region-owned.
        var ticks = new RegionTickScheduler(
            ZoneSimulationSettings.TickMilliseconds, partition, SequentialRegionExecutor.Instance, observer);
        ticks.AddRegionStage(_regenPlayersStage);
        ticks.AddRegionStage(_regenNpcsStage);
        ticks.AddWorldStage("spawns", _ => TickSpawns());
        ticks.AddRegionStage(_abnormalsStage);
        ticks.AddWorldStage("bosses", _ => TickBosses());
        ticks.AddWorldStage("clan.dissolutions", tick => CompleteDueDissolutions(tick.NowUnixMs));
        ticks.AddWorldStage("clan.hall-auctions", tick => TickHallAuctions(tick.NowUnixMs));
        ticks.AddWorldStage("sieges", tick => TickSieges(tick.NowUnixMs));
        ticks.AddWorldStage("manor", tick => TickManor(tick.NowUnixMs));
        ticks.AddWorldStage("fishing", tick => TickFishing(tick.NowUnixMs));
        ticks.AddWorldStage("lottery", tick => TickLottery(tick.NowUnixMs));
        ticks.AddWorldStage("seven-signs", _ => TickSevenSigns());
        ticks.AddWorldStage("mammon", tick => TickMammon(tick.NowUnixMs));
        ticks.AddWorldStage("olympiad", _ => TickOlympiad());
        _simulationTicks = ticks;
    }
}
