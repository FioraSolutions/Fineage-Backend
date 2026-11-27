using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutPartyMatchDetail(IReadOnlyList<int> watcherObjectIds, PartyMatchDetailMsg detail)
    {
        var payload = NetCodec.EncodePartyMatchDetail(ToNet(detail));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyMatchDetail, payload);
    }

    void IWorldEventPublisher.FanoutPartyMatchRoomList(IReadOnlyList<int> watcherObjectIds, PartyMatchRoomListMsg list)
    {
        var payload = NetCodec.EncodePartyMatchRoomList(ToNet(list));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyMatchRoomList, payload);
    }

    void IWorldEventPublisher.FanoutPartyRoomMemberList(IReadOnlyList<int> watcherObjectIds, PartyRoomMemberListMsg list)
    {
        var payload = NetCodec.EncodePartyRoomMemberList(ToNet(list));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyRoomMemberList, payload);
    }

    void IWorldEventPublisher.FanoutPartyRoomMemberUpdate(IReadOnlyList<int> watcherObjectIds, PartyRoomMemberUpdateMsg update)
    {
        var payload = NetCodec.EncodePartyRoomMemberUpdate(ToNet(update));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyRoomMemberUpdate, payload);
    }

    void IWorldEventPublisher.FanoutPartyRoomClose(IReadOnlyList<int> watcherObjectIds) =>
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyRoomClose, NetCodec.EncodePartyRoomClose());

    void IWorldEventPublisher.FanoutPartyRoomAskJoin(IReadOnlyList<int> watcherObjectIds, PartyRoomAskJoinMsg ask)
    {
        var payload = NetCodec.EncodePartyRoomAskJoin(new PartyRoomAskJoinNetMsg { CharName = ask.CharName ?? "" });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyRoomAskJoin, payload);
    }

    void IWorldEventPublisher.FanoutPartyMatchWaitingRoom(IReadOnlyList<int> watcherObjectIds, PartyMatchWaitingRoomMsg list)
    {
        var payload = NetCodec.EncodePartyMatchWaitingRoom(ToNet(list));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyMatchWaitingRoom, payload);
    }

    static PartyMatchDetailNetMsg ToNet(PartyMatchDetailMsg m) => new()
    {
        RoomId = m.RoomId,
        MaxMembers = m.MaxMembers,
        MinLevel = m.MinLevel,
        MaxLevel = m.MaxLevel,
        Loot = m.Loot,
        Location = m.Location,
        Title = m.Title ?? ""
    };

    static PartyMatchRoomListNetMsg ToNet(PartyMatchRoomListMsg m)
    {
        var src = m.Rooms ?? [];
        var rows = new PartyMatchRoomListEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new PartyMatchRoomListEntryNet
            {
                RoomId = src[i].RoomId,
                Title = src[i].Title ?? "",
                Location = src[i].Location,
                MinLevel = src[i].MinLevel,
                MaxLevel = src[i].MaxLevel,
                MaxMembers = src[i].MaxMembers,
                Members = src[i].Members,
                OwnerName = src[i].OwnerName ?? ""
            };
        }

        return new PartyMatchRoomListNetMsg { HasRooms = m.HasRooms, Rooms = rows };
    }

    static PartyRoomMemberListNetMsg ToNet(PartyRoomMemberListMsg m)
    {
        var src = m.Members ?? [];
        var rows = new PartyRoomMemberEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
            rows[i] = ToNet(src[i]);
        return new PartyRoomMemberListNetMsg { Mode = m.Mode, Members = rows };
    }

    static PartyRoomMemberUpdateNetMsg ToNet(PartyRoomMemberUpdateMsg m) =>
        new() { Mode = m.Mode, Member = ToNet(m.Member) };

    static PartyRoomMemberEntryNet ToNet(PartyRoomMemberEntryMsg m) => new()
    {
        ObjectId = m.ObjectId,
        Name = m.Name ?? "",
        ClassId = m.ClassId,
        Level = m.Level,
        Location = m.Location,
        MemberType = m.MemberType
    };

    static PartyMatchWaitingRoomNetMsg ToNet(PartyMatchWaitingRoomMsg m)
    {
        var src = m.Players ?? [];
        var rows = new PartyMatchWaitingEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new PartyMatchWaitingEntryNet
            {
                Name = src[i].Name ?? "",
                ClassId = src[i].ClassId,
                Level = src[i].Level
            };
        }

        return new PartyMatchWaitingRoomNetMsg { Page = m.Page, Players = rows };
    }
}
