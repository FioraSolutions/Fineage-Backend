using Fineage.Net.Contract;

namespace Fineage.Net.Host;

/// <summary>
/// Issue #867 PR3: stable fan-out target (player object id + entity generation + connection).
/// </summary>
public readonly record struct ConnectionHandle(int ObjectId, uint EntityGeneration, NetConnection Connection)
{
    public bool Matches(EntityId entity) =>
        entity.ToObjectId() == ObjectId && entity.Generation == EntityGeneration;
}