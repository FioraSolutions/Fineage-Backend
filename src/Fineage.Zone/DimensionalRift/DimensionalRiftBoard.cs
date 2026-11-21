namespace Fineage.Zone.DimensionalRift;

/// <summary>Room occupancy and active instances (Mobius <c>DimensionalRiftManager</c> room flags).</summary>
public sealed class DimensionalRiftBoard
{
    readonly DimensionalRiftCatalog _catalog;
    readonly Dictionary<int, DimensionalRiftInstance> _byParty = [];
    readonly Dictionary<(int Type, int Room), int> _occupied = [];

    public DimensionalRiftBoard(DimensionalRiftCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public DimensionalRiftCatalog Catalog => _catalog;

    public DimensionalRiftInstance? OfParty(int partyId)
    {
        _byParty.TryGetValue(partyId, out var instance);
        return instance;
    }

    public bool PartyInRift(int partyId) => _byParty.ContainsKey(partyId);

    public IReadOnlyCollection<int> ActivePartyIds => _byParty.Keys;

    public bool IsRoomFree(int type, int room) => !_occupied.ContainsKey((type, room));

    public void Occupy(int type, int room, int partyId) => _occupied[(type, room)] = partyId;

    public void Release(int type, int room) => _occupied.Remove((type, room));

    public void Register(DimensionalRiftInstance instance)
    {
        _byParty[instance.PartyId] = instance;
        Occupy(instance.Type, instance.CurrentRoom, instance.PartyId);
    }

    public void Remove(int partyId)
    {
        if (!_byParty.TryGetValue(partyId, out var instance))
            return;
        Release(instance.Type, instance.CurrentRoom);
        _byParty.Remove(partyId);
    }

    public bool IsAllowedEnter(int type)
    {
        int occupied = 0;
        int total = 0;
        foreach (var room in _catalog.RoomsOfType(type))
        {
            if (room.Room == 0)
                continue;
            total++;
            if (_occupied.ContainsKey((type, room.Room)))
                occupied++;
        }

        return occupied < total - 1;
    }

    public List<int> FreeRooms(int type)
    {
        var list = new List<int>();
        foreach (var room in _catalog.RoomsOfType(type))
        {
            if (room.Room == 0)
                continue;
            if (!_occupied.ContainsKey((type, room.Room)))
                list.Add(room.Room);
        }

        return list;
    }
}