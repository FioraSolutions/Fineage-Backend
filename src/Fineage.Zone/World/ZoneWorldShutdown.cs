using Fineage.Zone;

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
            _sevenSigns.Engine.Save();
            _bosses.Schedule.Save();
        });
    }

    internal void CloseCommandIngress(string reason) => _ingress.Close(reason);
}