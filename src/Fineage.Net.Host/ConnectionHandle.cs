using Fineage.Net.Contract;

namespace Fineage.Net.Host;

/// <summary>
/// Issue #867 PR3: stable fan-out target published with the presence table
/// (player object id + entity generation + connection). The send path checks
/// <see cref="NetConnection.IsBoundTo"/> and does not take the presence lock.
/// </summary>
public readonly record struct ConnectionHandle(int ObjectId, uint EntityGeneration, NetConnection Connection)
{
    public bool Matches(EntityId entity) =>
        entity.ToObjectId() == ObjectId && entity.Generation == EntityGeneration;
}