using Fineage.Protocol;
using Fineage.Zone.Party;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly PartyMatchRoomActor _partyMatch = new();

    internal PartyMatchRoomActor PartyMatch => _partyMatch;

    public Task<string?> TryPartyMatchConfigAsync(ZoneSession session, PartyMatchConfigMsg request, CancellationToken ct) =>
        OnSimulationAsync(session, "party-match-config", () => TryPartyMatchConfig(session, request), ct);

    public Task<string?> TryPartyMatchListAsync(ZoneSession session, PartyMatchListMsg request, CancellationToken ct) =>
        OnSimulationAsync(session, "party-match-list", () => TryPartyMatchList(session, request), ct);

    public Task<string?> TryPartyMatchDetailAsync(ZoneSession session, PartyMatchDetailRequestMsg request, CancellationToken ct) =>
        OnSimulationAsync(session, "party-match-detail", () => TryPartyMatchDetail(session, request), ct);

    public Task<string?> TryPartyRoomDismissAsync(ZoneSession session, int roomId, CancellationToken ct) =>
        OnSimulationAsync(session, "party-room-dismiss", () => TryPartyRoomDismiss(session, roomId), ct);

    public Task<string?> TryPartyRoomWithdrawAsync(ZoneSession session, int roomId, CancellationToken ct) =>
        OnSimulationAsync(session, "party-room-withdraw", () => TryPartyRoomWithdraw(session, roomId), ct);

    public Task<string?> TryPartyRoomOustAsync(ZoneSession session, int targetObjectId, CancellationToken ct) =>
        OnSimulationAsync(session, "party-room-oust", () => TryPartyRoomOust(session, targetObjectId), ct);

    public Task<string?> TryPartyRoomAskJoinAsync(ZoneSession session, string name, CancellationToken ct) =>
        OnSimulationAsync(session, "party-room-ask", () => TryPartyRoomAskJoin(session, name), ct);

    public Task<string?> TryPartyRoomAnswerJoinAsync(ZoneSession session, int answer, CancellationToken ct) =>
        OnSimulationAsync(session, "party-room-answer", () => TryPartyRoomAnswerJoin(session, answer), ct);

    public Task<string?> TryPartyMatchWaitingListAsync(ZoneSession session, PartyMatchWaitingListRequestMsg request, CancellationToken ct) =>
        OnSimulationAsync(session, "party-match-waiting", () => TryPartyMatchWaitingList(session, request), ct);

    public Task<string?> TryPartyMatchWaitingExitAsync(ZoneSession session, CancellationToken ct) =>
        OnSimulationAsync(session, "party-match-waiting-exit", () => TryPartyMatchWaitingExit(session), ct);

    string? TryPartyMatchConfig(ZoneSession session, PartyMatchConfigMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return "world";

        if (!_partyMatch.InRoom(player.ObjectId)
            && _party.Of(player.ObjectId) is { } party
            && party.LeaderObjectId != player.ObjectId)
        {
            SelfLine(session, PartyMatchProtocol.ListOnlyNonParty, PartyMatchProtocol.ListOnlyNonPartyText);
            return null;
        }

        if (_partyMatch.RoomOf(player.ObjectId) is { } room)
        {
            FanPartyMatchDetail([player.ObjectId], DetailOf(room));
            FanPartyRoomMemberList([player.ObjectId], MemberListOf(room, mode: 2));
            return null;
        }

        _partyMatch.AddWaiting(player.ObjectId);
        FanPartyMatchRoomList([player.ObjectId], BuildRoomList(player, request.Location, request.Level));
        return null;
    }

    string? TryPartyMatchList(ZoneSession session, PartyMatchListMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return "world";

        if (request.RoomId > 0)
        {
            var room = _partyMatch.GetRoom(request.RoomId);
            if (room == null || room.OwnerObjectId != player.ObjectId)
                return null;
            room.MaxMembers = Math.Max(1, request.MaxMembers);
            room.MinLevel = request.MinLevel;
            room.MaxLevel = request.MaxLevel;
            room.Loot = request.Loot;
            room.Title = request.Title ?? "";
            var detail = DetailOf(room);
            foreach (int memberId in room.MemberObjectIds)
            {
                FanPartyMatchDetail([memberId], detail);
                SelfLineTo(memberId, PartyMatchProtocol.Revised, PartyMatchProtocol.RevisedText);
            }

            return null;
        }

        var created = _partyMatch.Create(
            request.Title ?? "", request.Loot, request.MinLevel, request.MaxLevel,
            request.MaxMembers, player.ObjectId, location: 0);
        if (_party.Of(player.ObjectId) is { } ownerParty)
        {
            foreach (int mateId in ownerParty.MemberIds)
            {
                if (mateId == player.ObjectId)
                    continue;
                if (!_byId.ContainsKey(mateId))
                    continue;
                _partyMatch.AddMember(created, mateId);
            }
        }

        FanPartyMatchDetail([player.ObjectId], DetailOf(created));
        FanPartyRoomMemberList([player.ObjectId], MemberListOf(created, mode: 1));
        SelfLine(session, PartyMatchProtocol.Created, PartyMatchProtocol.CreatedText);
        return null;
    }

    string? TryPartyMatchDetail(ZoneSession session, PartyMatchDetailRequestMsg request) =>
        JoinRoom(session, request.RoomId);

    string? JoinRoom(ZoneSession session, int roomId)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return "world";

        var room = _partyMatch.GetRoom(roomId);
        if (room == null)
            return null;

        if (player.Level < room.MinLevel || player.Level > room.MaxLevel)
        {
            SelfLine(session, PartyMatchProtocol.LevelRequirements, PartyMatchProtocol.LevelRequirementsText);
            return null;
        }

        if (room.MemberCount >= room.MaxMembers)
            return null;

        EnterRoom(player, room);
        return null;
    }

    void EnterRoom(ZoneEntity player, ZonePartyMatchRoom room)
    {
        var entry = MemberEntryOf(player, room);
        foreach (int memberId in room.MemberObjectIds.ToArray())
        {
            FanPartyRoomMemberUpdate([memberId], new PartyRoomMemberUpdateMsg { Mode = 0, Member = entry });
            SelfLineTo(memberId, PartyMatchProtocol.MemberEntered,
                string.Format(PartyMatchProtocol.MemberEnteredText, player.Name), player.Name);
        }

        _partyMatch.AddMember(room, player.ObjectId);
        FanPartyMatchDetail([player.ObjectId], DetailOf(room));
        FanPartyRoomMemberList([player.ObjectId], MemberListOf(room, mode: 0));
    }

    string? TryPartyRoomDismiss(ZoneSession session, int roomId)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return "world";

        var room = _partyMatch.GetRoom(roomId);
        if (room == null || room.OwnerObjectId != player.ObjectId)
            return null;
        DisbandRoom(room);
        return null;
    }

    string? TryPartyRoomWithdraw(ZoneSession session, int roomId)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return "world";

        var room = _partyMatch.GetRoom(roomId);
        if (room == null || !_partyMatch.InRoom(player.ObjectId) || _partyMatch.RoomIdOf(player.ObjectId) != roomId)
            return null;

        // Mobius: same party as owner is not removed.
        if (SamePartyAsOwner(player.ObjectId, room))
            return null;

        LeaveRoom(player, room, notifyExit: true, closeUi: true, lineId: PartyMatchProtocol.Exited,
            lineText: PartyMatchProtocol.ExitedText);
        return null;
    }

    string? TryPartyRoomOust(ZoneSession session, int targetObjectId)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var owner)
            || owner.Kind != ZoneEntityKind.Player)
            return "world";

        if (!_byId.TryGetValue(targetObjectId, out var member) || member.Kind != ZoneEntityKind.Player)
            return null;

        var room = _partyMatch.RoomOf(member.ObjectId);
        if (room == null || room.OwnerObjectId != owner.ObjectId)
            return null;

        if (SamePartyAsOwner(member.ObjectId, room))
        {
            SelfLine(session, PartyMatchProtocol.CannotDismissPartyMember,
                PartyMatchProtocol.CannotDismissPartyMemberText);
            return null;
        }

        LeaveRoom(member, room, notifyExit: true, closeUi: true, lineId: PartyMatchProtocol.Ousted,
            lineText: PartyMatchProtocol.OustedText);
        _partyMatch.AddWaiting(member.ObjectId);
        FanPartyMatchRoomList([member.ObjectId], BuildRoomList(member, location: 0, levelFilter: member.Level));
        return null;
    }

    string? TryPartyRoomAskJoin(ZoneSession session, string name)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return "world";

        var target = FindOnlinePlayerByName(name);
        if (target == null)
        {
            SelfLine(session, ChatProtocol.PlayerNotOnline, "That player is not online.");
            return null;
        }

        if (_partyMatch.RoomOf(player.ObjectId) == null)
            return null;

        _partyMatch.Ask(target.ObjectId, player.ObjectId);
        NetBridge?.FanoutPartyRoomAskJoin([target.ObjectId], new PartyRoomAskJoinMsg { CharName = player.Name });
        return null;
    }

    string? TryPartyRoomAnswerJoin(ZoneSession session, int answer)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return "world";

        if (!_partyMatch.TryTakeAsk(player.ObjectId, out int requestorId))
        {
            SelfLine(session, ChatProtocol.PlayerNotOnline, "That player is not online.");
            return null;
        }

        if (!_byId.TryGetValue(requestorId, out var requestor) || requestor.Kind != ZoneEntityKind.Player)
        {
            SelfLine(session, ChatProtocol.PlayerNotOnline, "That player is not online.");
            return null;
        }

        if (answer != 1)
        {
            SelfLineTo(requestorId, PartyMatchProtocol.InviteDeclined, PartyMatchProtocol.InviteDeclinedText);
            return null;
        }

        var room = _partyMatch.RoomOf(requestor.ObjectId);
        if (room == null)
            return null;
        if (player.Level < room.MinLevel || player.Level > room.MaxLevel)
        {
            SelfLine(session, PartyMatchProtocol.LevelRequirements, PartyMatchProtocol.LevelRequirementsText);
            return null;
        }

        EnterRoom(player, room);
        return null;
    }

    string? TryPartyMatchWaitingList(ZoneSession session, PartyMatchWaitingListRequestMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return "world";

        if (request.Mode == 0)
        {
            FanPartyMatchWaitingRoom([player.ObjectId], new PartyMatchWaitingRoomMsg { Page = 0, Players = [] });
            return null;
        }

        var rows = new List<PartyMatchWaitingEntryMsg>();
        foreach (int id in _partyMatch.Waiting)
        {
            if (id == player.ObjectId)
                continue;
            if (!_byId.TryGetValue(id, out var other) || other.Kind != ZoneEntityKind.Player)
                continue;
            if (other.Level < request.MinLevel || other.Level > request.MaxLevel)
                continue;
            rows.Add(new PartyMatchWaitingEntryMsg
            {
                Name = other.Name,
                ClassId = other.ClassId,
                Level = other.Level
            });
        }

        FanPartyMatchWaitingRoom([player.ObjectId], new PartyMatchWaitingRoomMsg
        {
            Page = 1,
            Players = rows.ToArray()
        });
        return null;
    }

    string? TryPartyMatchWaitingExit(ZoneSession session)
    {
        if (!IsLive(session))
            return "world";
        _partyMatch.RemoveWaiting(session.ObjectId);
        return null;
    }

    void LeaveRoom(
        ZoneEntity player,
        ZonePartyMatchRoom room,
        bool notifyExit,
        bool closeUi,
        int lineId,
        string lineText)
    {
        bool wasOwner = room.OwnerObjectId == player.ObjectId;
        if (wasOwner)
        {
            if (room.MemberCount <= 1)
            {
                DisbandRoom(room);
                return;
            }

            // Mobius changeLeader: next member becomes owner (index 0).
            int next = room.MemberObjectIds[1];
            room.MemberObjectIds.RemoveAt(0);
            _partyMatch.Unbind(player.ObjectId);
            if (_byId.TryGetValue(next, out var leader))
            {
                var leaderEntry = MemberEntryOf(leader, room);
                var oldEntry = MemberEntryOf(player, room);
                foreach (int memberId in room.MemberObjectIds)
                {
                    FanPartyRoomMemberUpdate([memberId], new PartyRoomMemberUpdateMsg { Mode = 1, Member = leaderEntry });
                    FanPartyRoomMemberUpdate([memberId], new PartyRoomMemberUpdateMsg { Mode = 1, Member = oldEntry });
                    SelfLineTo(memberId, PartyMatchProtocol.LeaderChanged, PartyMatchProtocol.LeaderChangedText);
                }
            }

            if (closeUi)
                FanPartyRoomClose([player.ObjectId]);
            if (notifyExit)
                SelfLineTo(player.ObjectId, lineId, lineText);
            return;
        }

        var exitEntry = MemberEntryOf(player, room);
        room.MemberObjectIds.Remove(player.ObjectId);
        _partyMatch.Unbind(player.ObjectId);
        if (notifyExit)
        {
            foreach (int memberId in room.MemberObjectIds)
            {
                FanPartyRoomMemberUpdate([memberId], new PartyRoomMemberUpdateMsg { Mode = 2, Member = exitEntry });
                SelfLineTo(memberId, PartyMatchProtocol.MemberLeft,
                    string.Format(PartyMatchProtocol.MemberLeftText, player.Name), player.Name);
            }
        }

        if (closeUi)
            FanPartyRoomClose([player.ObjectId]);
        if (notifyExit)
            SelfLineTo(player.ObjectId, lineId, lineText);
    }

    void DisbandRoom(ZonePartyMatchRoom room)
    {
        var members = room.MemberObjectIds.ToArray();
        _partyMatch.DeleteRoom(room.Id);
        foreach (int memberId in members)
        {
            FanPartyRoomClose([memberId]);
            SelfLineTo(memberId, PartyMatchProtocol.Disbanded, PartyMatchProtocol.DisbandedText);
        }
    }

    void ClearPartyMatchOnLeave(int objectId)
    {
        _partyMatch.ClearPlayer(objectId);
    }

    bool SamePartyAsOwner(int memberObjectId, ZonePartyMatchRoom room)
    {
        var ownerParty = _party.Of(room.OwnerObjectId);
        var memberParty = _party.Of(memberObjectId);
        return ownerParty != null && memberParty != null && ownerParty.PartyId == memberParty.PartyId
            && ownerParty.LeaderObjectId == memberParty.LeaderObjectId;
    }

    PartyMatchDetailMsg DetailOf(ZonePartyMatchRoom room) => new()
    {
        RoomId = room.Id,
        MaxMembers = room.MaxMembers,
        MinLevel = room.MinLevel,
        MaxLevel = room.MaxLevel,
        Loot = room.Loot,
        Location = room.Location,
        Title = room.Title
    };

    PartyMatchRoomListMsg BuildRoomList(ZoneEntity viewer, int location, int levelFilter)
    {
        var rows = new List<PartyMatchRoomListEntryMsg>();
        foreach (var room in _partyMatch.Rooms.ToArray())
        {
            if (room.MemberCount < 1 || !_byId.TryGetValue(room.OwnerObjectId, out var owner)
                || owner.Kind != ZoneEntityKind.Player
                || _partyMatch.RoomIdOf(owner.ObjectId) != room.Id)
            {
                DisbandRoom(room);
                continue;
            }

            if (location > 0 && location != room.Location)
                continue;
            if (levelFilter == 0
                && (viewer.Level < room.MinLevel || viewer.Level > room.MaxLevel))
                continue;

            rows.Add(new PartyMatchRoomListEntryMsg
            {
                RoomId = room.Id,
                Title = room.Title,
                Location = room.Location,
                MinLevel = room.MinLevel,
                MaxLevel = room.MaxLevel,
                MaxMembers = room.MaxMembers,
                Members = room.MemberCount,
                OwnerName = owner.Name
            });
        }

        return new PartyMatchRoomListMsg { HasRooms = rows.Count > 0, Rooms = rows.ToArray() };
    }

    PartyRoomMemberListMsg MemberListOf(ZonePartyMatchRoom room, int mode)
    {
        var rows = new List<PartyRoomMemberEntryMsg>(room.MemberCount);
        foreach (int id in room.MemberObjectIds)
        {
            if (!_byId.TryGetValue(id, out var e) || e.Kind != ZoneEntityKind.Player)
                continue;
            rows.Add(MemberEntryOf(e, room));
        }

        return new PartyRoomMemberListMsg { Mode = mode, Members = rows.ToArray() };
    }

    PartyRoomMemberEntryMsg MemberEntryOf(ZoneEntity member, ZonePartyMatchRoom room)
    {
        int type = 0;
        if (room.OwnerObjectId == member.ObjectId)
            type = 1;
        else if (SamePartyAsOwner(member.ObjectId, room))
            type = 2;
        return new PartyRoomMemberEntryMsg
        {
            ObjectId = member.ObjectId,
            Name = member.Name,
            ClassId = member.ClassId,
            Level = member.Level,
            Location = room.Location,
            MemberType = type
        };
    }

    void SelfLineTo(int objectId, int id, string text, params string[] args) =>
        NetBridge?.FanoutSystemMessage([objectId], new SystemMessageMsg { Id = id, Text = text, Params = args });

    void FanPartyMatchDetail(IReadOnlyList<int> watchers, PartyMatchDetailMsg msg) =>
        NetBridge?.FanoutPartyMatchDetail(watchers, msg);

    void FanPartyMatchRoomList(IReadOnlyList<int> watchers, PartyMatchRoomListMsg msg) =>
        NetBridge?.FanoutPartyMatchRoomList(watchers, msg);

    void FanPartyRoomMemberList(IReadOnlyList<int> watchers, PartyRoomMemberListMsg msg) =>
        NetBridge?.FanoutPartyRoomMemberList(watchers, msg);

    void FanPartyRoomMemberUpdate(IReadOnlyList<int> watchers, PartyRoomMemberUpdateMsg msg) =>
        NetBridge?.FanoutPartyRoomMemberUpdate(watchers, msg);

    void FanPartyRoomClose(IReadOnlyList<int> watchers) =>
        NetBridge?.FanoutPartyRoomClose(watchers);

    void FanPartyMatchWaitingRoom(IReadOnlyList<int> watchers, PartyMatchWaitingRoomMsg msg) =>
        NetBridge?.FanoutPartyMatchWaitingRoom(watchers, msg);

    /// <summary>Online members of the speaker's party-match room (Mobius ChatPartyMatchRoom).</summary>
    internal IReadOnlyList<int>? PartyMatchRoomOnlineMembers(int objectId)
    {
        var room = _partyMatch.RoomOf(objectId);
        if (room == null)
            return null;
        var ids = new List<int>(room.MemberCount);
        foreach (int id in room.MemberObjectIds)
        {
            if (_byId.ContainsKey(id))
                ids.Add(id);
        }

        return ids;
    }
}
