using Fineage.Accounts;
using Fineage.Zone.Persistence;

namespace Fineage.Zone.Tests;

/// <summary>
/// The world-state path a zone uses (#996), without a zone: subsystems save into <see cref="State"/>, <see cref="Commit"/>
/// writes the dirty documents the way the economy commit does, and <see cref="Restart"/> opens a fresh store over what
/// was committed. Anything saved and not committed is gone after a restart, as after a kill.
/// </summary>
internal sealed class WorldStateRig
{
    long _committedBatches;

    public WorldStateRig(MemoryAccountStore? store = null)
    {
        Store = store ?? MemoryAccountStore.InMemory();
        State = WorldStateStore.Open(Store);
    }

    public MemoryAccountStore Store { get; }

    public WorldStateStore State { get; private set; }

    public long CommittedBatches => _committedBatches;

    public void Commit()
    {
        var stolen = State.Steal();
        if (stolen.Count == 0)
            return;
        try
        {
            Store.CommitEconomy([], [], stolen);
        }
        catch
        {
            State.Restore(stolen);
            throw;
        }

        State.Committed(stolen);
        _committedBatches++;
    }

    /// <summary>Commits what is dirty, then starts over from the store.</summary>
    public WorldStateStore Restart()
    {
        Commit();
        return Crash();
    }

    /// <summary>Starts over from the store; whatever was not committed is lost.</summary>
    public WorldStateStore Crash()
    {
        State = WorldStateStore.Open(Store);
        return State;
    }
}
