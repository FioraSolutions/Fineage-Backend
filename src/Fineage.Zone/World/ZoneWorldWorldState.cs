using Fineage.Zone.Persistence;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// The world subsystems' saved state (#996). Their saves leave documents dirty here; the economy commit takes them
    /// with the bags and ledger rows (<see cref="StealDirtyEconomy"/>) and writes them in the same transaction. A world
    /// built without one (tests, tools) gets an empty store that nothing outside the world reads.
    /// </summary>
    WorldStateStore _worldState = WorldStateStore.FromDocuments();

    internal WorldStateStore WorldState => _worldState;

    /// <summary>
    /// Production wiring: the store the subsystems were opened on. Call before anything enters the world. A save the
    /// previous store still holds would be lost, so swapping one with pending saves is refused.
    /// </summary>
    public void UseWorldState(WorldStateStore state)
    {
        ArgumentNullException.ThrowIfNull(state);
        using (_affinity.Step())
        {
            if (!ReferenceEquals(state, _worldState) && _worldState.HasDirty)
                throw new InvalidOperationException(
                    "world state store replaced while the previous one has saves that are not committed: "
                    + string.Join(", ", _worldState.Current().Where(d => _worldState.IsDirty(d.System)).Select(d => d.System)));
            _worldState = state;
        }
    }
}
