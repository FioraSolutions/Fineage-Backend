using Fineage.Net.Contract;

namespace Fineage.Zone.Net;

/// <summary>Bridge ZoneWorld int object ids ↔ Net EntityId (not a gameplay adapter).</summary>
public sealed class EntityIdMap
{
    readonly Dictionary<int, uint> _generation = new();

    public EntityId AllocateOrGet(int objectId)
    {
        if (!_generation.TryGetValue(objectId, out uint gen))
        {
            gen = 1;
            _generation[objectId] = gen;
        }

        return EntityId.FromObjectId(objectId, gen);
    }

    public void Bump(int objectId)
    {
        _generation.TryGetValue(objectId, out uint gen);
        _generation[objectId] = gen + 1;
    }
}
