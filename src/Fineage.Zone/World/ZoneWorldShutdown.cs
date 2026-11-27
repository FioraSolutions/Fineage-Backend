using Fineage.Zone;
using Fineage.Zone.Persistence;

namespace Fineage.Zone.World;

/// <summary>P1-10: detach and persist every online player, then refuse further commands.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>
    /// One simulation step that detaches and persists every online player. Safe to call when the loop already saved on
    /// cancel (no-op when nobody is online).
    /// </summary>
    public void ShutdownGracefully()
    {
        ShutdownSaveOnlinePlayers();
        CloseCommandIngress("zone shutdown");
        _fanoutDispatcher.WaitUntilIdle(TimeSpan.FromSeconds(30));
        _fanoutDispatcher.Dispose();
    }

    /// <summary>Waits for the background simulation loop to finish (including its shutdown save).</summary>
    public Task WaitSimulationAsync() => _simulation ?? Task.CompletedTask;

    internal void ShutdownSaveOnlinePlayers()
    {
        RunStep(_ =>
        {
            foreach (var entity in Players().ToList())
            {
                if (entity.Session is not { } session)
                    continue;
                if (!TryDetachPlayer(session, capturePersist: true, out ZoneEntity? detached, out var snapshot) ||
                    snapshot == null)
                    continue;

                PersistUnlinkSnapshot(snapshot.Value);
                ZoneLog.LeaveWorld(session);
            }

            CountOnlinePlayers();
            _stateFlushes.FlushAll();
        });
    }

    /// <summary>
    /// The closed shutdown list from issue #984. Startup calls <see cref="ShutdownFlushRegistry.AssertComplete"/> so a
    /// new sidecar cannot be added without joining this registry.
    /// </summary>
    void RegisterStateShutdownFlushes()
    {
        _stateFlushes.Register(WorldStateSystems.BossSchedule, () => _bosses.Schedule.Save());
        _stateFlushes.Register(WorldStateSystems.SevenSigns, () => _sevenSigns.Engine.Save());
        _stateFlushes.Register(WorldStateSystems.Olympiad, () => _olympiad.Board.Save());
        _stateFlushes.Register(WorldStateSystems.Lottery, () => _lottery.Board.Save());
        _stateFlushes.Register(WorldStateSystems.RaidBossPoints, () => _raidBossPoints.Save());
        _stateFlushes.Register(WorldStateSystems.Punishment, () => _punishments.Save());
        _stateFlushes.Register(WorldStateSystems.MonsterRace, () => _monsterRace.Board.Save());
        _stateFlushes.Register(WorldStateSystems.FishingChampionship, () => _fishingChampionship.Board.Save());
        _stateFlushes.AssertComplete();
    }

    internal IReadOnlyList<string> StateFlushSystems => _stateFlushes.RegisteredSystems;

    internal IReadOnlyList<string> LastStateFlushFailures => _stateFlushes.LastFailures;

    internal void FlushWorldStateForTests() => _stateFlushes.FlushAll();

    internal void CloseCommandIngress(string reason) => _ingress.Close(reason);
}
