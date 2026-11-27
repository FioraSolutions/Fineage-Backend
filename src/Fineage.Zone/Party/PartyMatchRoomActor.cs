namespace Fineage.Zone.Party;

/// <summary>Mobius <c>PartyMatchRoom</c> fields kept on the simulation thread.</summary>
internal sealed class ZonePartyMatchRoom
{
    public required int Id { get; init; }
    public string Title { get; set; } = "";
    public int Loot { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int MaxMembers { get; set; }
    /// <summary>Map-region BBS id (Mobius <c>getLocation</c>); 0 = unset / all.</summary>
    public int Location { get; set; }
    /// <summary>Member object ids; index 0 is the room owner.</summary>
    public List<int> MemberObjectIds { get; } = [];

    public int OwnerObjectId => MemberObjectIds.Count > 0 ? MemberObjectIds[0] : 0;
    public int MemberCount => MemberObjectIds.Count;
}

/// <summary>
/// Mobius <c>PartyMatchRoomList</c> + <c>PartyMatchWaitingList</c>: in-memory rooms and LFP waiters.
/// </summary>
internal sealed class PartyMatchRoomActor
{
    readonly SortedDictionary<int, ZonePartyMatchRoom> _rooms = [];
    readonly Dictionary<int, int> _playerRoom = [];
    readonly HashSet<int> _waiting = [];
    readonly Dictionary<int, int> _pendingInvite = []; // invitee → requestor
    int _nextId = 1;

    public int MaxId => _nextId;
    public IReadOnlyCollection<ZonePartyMatchRoom> Rooms => _rooms.Values;
    public IReadOnlyCollection<int> Waiting => _waiting;

    public ZonePartyMatchRoom? GetRoom(int id) =>
        _rooms.TryGetValue(id, out var room) ? room : null;

    public ZonePartyMatchRoom? RoomOf(int objectId) =>
        _playerRoom.TryGetValue(objectId, out int roomId) ? GetRoom(roomId) : null;

    public int RoomIdOf(int objectId) =>
        _playerRoom.TryGetValue(objectId, out int roomId) ? roomId : 0;

    public bool InRoom(int objectId) => _playerRoom.ContainsKey(objectId);

    public bool IsWaiting(int objectId) => _waiting.Contains(objectId);

    public void AddWaiting(int objectId) => _waiting.Add(objectId);

    public void RemoveWaiting(int objectId) => _waiting.Remove(objectId);

    public ZonePartyMatchRoom Create(
        string title, int loot, int minLevel, int maxLevel, int maxMembers, int ownerObjectId, int location)
    {
        int id = _nextId++;
        var room = new ZonePartyMatchRoom
        {
            Id = id,
            Title = title ?? "",
            Loot = loot,
            MinLevel = minLevel,
            MaxLevel = maxLevel,
            MaxMembers = Math.Max(1, maxMembers),
            Location = location
        };
        room.MemberObjectIds.Add(ownerObjectId);
        _rooms[id] = room;
        _waiting.Remove(ownerObjectId);
        _playerRoom[ownerObjectId] = id;
        return room;
    }

    public void Bind(int objectId, int roomId)
    {
        _waiting.Remove(objectId);
        _playerRoom[objectId] = roomId;
    }

    public void AddMember(ZonePartyMatchRoom room, int objectId)
    {
        if (room.MemberObjectIds.Contains(objectId))
            return;
        room.MemberObjectIds.Add(objectId);
        Bind(objectId, room.Id);
    }

    public void Unbind(int objectId)
    {
        _playerRoom.Remove(objectId);
    }

    public void DeleteRoom(int id)
    {
        if (!_rooms.TryGetValue(id, out var room))
            return;
        foreach (int memberId in room.MemberObjectIds.ToList())
            _playerRoom.Remove(memberId);
        room.MemberObjectIds.Clear();
        _rooms.Remove(id);
    }

    public void Ask(int inviteeObjectId, int requestorObjectId) =>
        _pendingInvite[inviteeObjectId] = requestorObjectId;

    public bool TryTakeAsk(int inviteeObjectId, out int requestorObjectId)
    {
        if (_pendingInvite.TryGetValue(inviteeObjectId, out requestorObjectId))
        {
            _pendingInvite.Remove(inviteeObjectId);
            return true;
        }

        requestorObjectId = 0;
        return false;
    }

    public void DropAsksOf(int objectId)
    {
        _pendingInvite.Remove(objectId);
        foreach (var (invitee, requestor) in _pendingInvite.ToList())
        {
            if (requestor == objectId)
                _pendingInvite.Remove(invitee);
        }
    }

    public void ClearPlayer(int objectId)
    {
        RemoveWaiting(objectId);
        DropAsksOf(objectId);
        var room = RoomOf(objectId);
        if (room == null)
            return;
        if (room.OwnerObjectId == objectId)
        {
            if (room.MemberCount <= 1)
            {
                DeleteRoom(room.Id);
                return;
            }

            // Promote next member then drop the leaving owner.
            int next = room.MemberObjectIds[1];
            room.MemberObjectIds.RemoveAt(0);
            room.MemberObjectIds.Remove(next);
            room.MemberObjectIds.Insert(0, next);
            Unbind(objectId);
            return;
        }

        room.MemberObjectIds.Remove(objectId);
        Unbind(objectId);
    }
}
