using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class PartyMatchCodecTests
{
    [Fact]
    public void Opcodes_use_assigned_net_ids()
    {
        Assert.Equal((ushort)0x03F2, NetOpcode.RequestPartyMatchConfig);
        Assert.Equal((ushort)0x03F3, NetOpcode.RequestPartyMatchList);
        Assert.Equal((ushort)0x03F4, NetOpcode.RequestPartyMatchDetail);
        Assert.Equal((ushort)0x03F5, NetOpcode.PartyMatchDetail);
        Assert.Equal((ushort)0x03F6, NetOpcode.PartyMatchRoomList);
        Assert.Equal(unchecked((ushort)(NetOpcodeMobius.ExClientBase | 0x01)), NetOpcode.RequestPartyRoomOust);
        Assert.Equal(unchecked((ushort)(NetOpcodeMobius.ExServerBase | 0x0E)), NetOpcode.PartyRoomMemberList);
    }

    [Fact]
    public void Detail_and_room_list_round_trip()
    {
        var detail = NetCodec.DecodePartyMatchDetail(NetCodec.EncodePartyMatchDetail(new PartyMatchDetailNetMsg
        {
            RoomId = 3,
            MaxMembers = 7,
            MinLevel = 10,
            MaxLevel = 40,
            Loot = 1,
            Location = 2,
            Title = "Giran"
        }));
        Assert.Equal(3, detail.RoomId);
        Assert.Equal("Giran", detail.Title);

        var list = NetCodec.DecodePartyMatchRoomList(NetCodec.EncodePartyMatchRoomList(new PartyMatchRoomListNetMsg
        {
            HasRooms = true,
            Rooms =
            [
                new PartyMatchRoomListEntryNet
                {
                    RoomId = 3,
                    Title = "Giran",
                    Location = 2,
                    MinLevel = 10,
                    MaxLevel = 40,
                    MaxMembers = 7,
                    Members = 2,
                    OwnerName = "Lead"
                }
            ]
        }));
        Assert.True(list.HasRooms);
        Assert.Equal("Lead", Assert.Single(list.Rooms).OwnerName);
    }
}
