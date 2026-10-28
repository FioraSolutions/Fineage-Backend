using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Login.Tests;

public class ProtocolCompatTests
{
    [Fact]
    public void AuthMsg_MatchesZoneServerBytes()
    {
        var msg = new AuthMsg { Account = "hero", Password = "tok" };
        Assert.Equal("92A46865726FA3746F6B", Convert.ToHexString(MessagePackSerializer.Serialize(msg)));
    }

    [Fact]
    public void HelloMsg_MatchesZoneServerBytes()
    {
        var msg = new HelloMsg { ProtocolVersion = 1, ClientBuild = "selftest" };
        Assert.Equal("9201A873656C6674657374", Convert.ToHexString(MessagePackSerializer.Serialize(msg)));
    }

    [Fact]
    public void ErrorMsg_MatchesZoneServerBytes()
    {
        var msg = new ErrorMsg { Code = "auth", Message = "Auth required" };
        Assert.Equal("92A461757468AD41757468207265717569726564", Convert.ToHexString(MessagePackSerializer.Serialize(msg)));
    }

    [Fact]
    public void AuthOkMsg_MatchesZoneServerBytes()
    {
        var msg = new AuthOkMsg { SessionId = 7, CharacterName = "Test" };
        Assert.Equal("9207A454657374", Convert.ToHexString(MessagePackSerializer.Serialize(msg)));
    }

    [Fact]
    public void ZoneOpcodes_MatchFineageZoneServer()
    {
        Assert.Equal(1, (ushort)MsgType.Hello);
        Assert.Equal(2, (ushort)MsgType.Auth);
        Assert.Equal(3, (ushort)MsgType.EnterWorld);
        Assert.Equal(4, (ushort)MsgType.MoveTo);
        Assert.Equal(5, (ushort)MsgType.SelectTarget);
        Assert.Equal(6, (ushort)MsgType.AttackRequest);
        Assert.Equal(12, (ushort)MsgType.BuffRequest);
        Assert.Equal(13, (ushort)MsgType.GrantItemRequest);
        Assert.Equal(14, (ushort)MsgType.PickupRequest);
        Assert.Equal(15, (ushort)MsgType.UseItem);
        Assert.Equal(16, (ushort)MsgType.UnequipItem);
        Assert.Equal(17, (ushort)MsgType.GiveItem);
        Assert.Equal(18, (ushort)MsgType.PartyInviteRequest);
        Assert.Equal(19, (ushort)MsgType.PartyAnswerJoinRequest);
        Assert.Equal(26, (ushort)MsgType.PartyLeaveRequest);
        Assert.Equal(27, (ushort)MsgType.PartyKickRequest);
        Assert.Equal(32, (ushort)MsgType.ClanCreateRequest);
        Assert.Equal(33, (ushort)MsgType.ClanInviteRequest);
        Assert.Equal(34, (ushort)MsgType.ClanAnswerJoinRequest);
        Assert.Equal(35, (ushort)MsgType.ClanLeaveRequest);
        Assert.Equal(36, (ushort)MsgType.ClanKickRequest);
        Assert.Equal(37, (ushort)MsgType.ClanDissolveRequest);
        Assert.Equal(38, (ushort)MsgType.ClanCancelDissolveRequest);
        Assert.Equal((ushort)MsgType.UseItem, (ushort)Fineage.Protocol.V2.MsgType.UseItem);
        Assert.Equal((ushort)MsgType.UnequipItem, (ushort)Fineage.Protocol.V2.MsgType.UnequipItem);
        Assert.Equal((ushort)MsgType.GiveItem, (ushort)Fineage.Protocol.V2.MsgType.GiveItem);
        Assert.Equal(InventoryProtocol.NotOwned, "not_owned");
        Assert.Equal(InventoryProtocol.NotEquippable, "not_equippable");
        Assert.Equal(InventoryProtocol.Dead, "dead");
        Assert.Equal(InventoryProtocol.Blocked, "blocked");
        Assert.Equal(InventoryProtocol.BadSlot, "bad_slot");
        Assert.Equal(InventoryProtocol.Empty, "empty");
        Assert.Equal(InventoryProtocol.Forbidden, "forbidden");
        Assert.Equal(9, (ushort)MsgType.Ping);
        Assert.Equal(1001, (ushort)MsgType.HelloAck);
        Assert.Equal(1002, (ushort)MsgType.AuthOk);
        Assert.Equal(1003, (ushort)MsgType.PlayerSpawned);
        Assert.Equal(1004, (ushort)MsgType.MoveToAck);
        Assert.Equal(1005, (ushort)MsgType.CharInfo);
        Assert.Equal(1006, (ushort)MsgType.NpcInfo);
        Assert.Equal(1007, (ushort)MsgType.DeleteObject);
        Assert.Equal(1008, (ushort)MsgType.MyTargetSelected);
        Assert.Equal(1009, (ushort)MsgType.Attack);
        Assert.Equal(1010, (ushort)MsgType.StatusUpdate);
        Assert.Equal(1013, (ushort)MsgType.Pong);
        Assert.Equal(1014, (ushort)MsgType.ItemList);
        Assert.Equal(1015, (ushort)MsgType.InventoryUpdate);
        Assert.Equal(1016, (ushort)MsgType.UserInfo);
        Assert.Equal(1017, (ushort)MsgType.AbnormalStatusUpdate);
        Assert.Equal(1018, (ushort)MsgType.SpawnItem);
        Assert.Equal(1027, (ushort)MsgType.PartyAskJoin);
        Assert.Equal(1028, (ushort)MsgType.PartyJoinResult);
        Assert.Equal(1029, (ushort)MsgType.PartyMemberList);
        Assert.Equal(1040, (ushort)MsgType.SystemMessage);
        Assert.Equal(1050, (ushort)MsgType.ClanAskJoin);
        Assert.Equal(1051, (ushort)MsgType.PledgeStatusChanged);
        Assert.Equal(1052, (ushort)MsgType.PledgeMemberList);
        Assert.Equal(1053, (ushort)MsgType.ClanJoinResult);
        Assert.Equal(ClanProtocol.BandFirst, (ushort)MsgType.ClanAskJoin);
        Assert.Equal((ushort)MsgType.ClanAskJoin, (ushort)Fineage.Protocol.V2.MsgType.ClanAskJoin);
        Assert.Equal((ushort)MsgType.PledgeMemberList, (ushort)Fineage.Protocol.V2.MsgType.PledgeMemberList);
        Assert.Equal(ChatProtocol.ChatBandFirst, (ushort)MsgType.SystemMessage);
        Assert.Equal((ushort)MsgType.SystemMessage, (ushort)Fineage.Protocol.V2.MsgType.SystemMessage);
        Assert.Equal((ushort)MsgType.ItemList, (ushort)Fineage.Protocol.V2.MsgType.ItemList);
        Assert.Equal((ushort)MsgType.InventoryUpdate, (ushort)Fineage.Protocol.V2.MsgType.InventoryUpdate);
        Assert.Equal((ushort)MsgType.UserInfo, (ushort)Fineage.Protocol.V2.MsgType.UserInfo);
        Assert.Equal((ushort)MsgType.AbnormalStatusUpdate, (ushort)Fineage.Protocol.V2.MsgType.AbnormalStatusUpdate);
        Assert.Equal((ushort)MsgType.SpawnItem, (ushort)Fineage.Protocol.V2.MsgType.SpawnItem);
        Assert.Equal((ushort)MsgType.PartyInviteRequest, (ushort)Fineage.Protocol.V2.MsgType.PartyInviteRequest);
        Assert.Equal((ushort)MsgType.PartyAskJoin, (ushort)Fineage.Protocol.V2.MsgType.PartyAskJoin);
        Assert.Equal(1099, (ushort)MsgType.Error);
        AssertUnique<MsgType>();
        AssertUnique<Fineage.Protocol.V2.MsgType>();
    }

    static void AssertUnique<T>() where T : struct, Enum
    {
        var seen = new HashSet<ushort>();
        foreach (var value in Enum.GetValues<T>())
        {
            ushort id = Convert.ToUInt16(value);
            Assert.True(seen.Add(id), $"{typeof(T).Name} reuses opcode {id} ({value}).");
        }
    }

    [Fact]
    public void GiveItem_omitted_count_defaults_to_one()
    {
        byte[] oneKey = [0x91, 0x39];
        var msg = MessagePackSerializer.Deserialize<GiveItemMsg>(oneKey);
        Assert.Equal(57, msg.ItemId);
        Assert.Equal(1, msg.Count);

        var round = MessagePackSerializer.Deserialize<GiveItemMsg>(
            MessagePackSerializer.Serialize(new GiveItemMsg { ItemId = 2369, Count = 2 }));
        Assert.Equal(2369, round.ItemId);
        Assert.Equal(2, round.Count);

        var use = MessagePackSerializer.Deserialize<UseItemMsg>(MessagePackSerializer.Serialize(new UseItemMsg { ObjectId = 100_000 }));
        Assert.Equal(100_000, use.ObjectId);
        var slot = MessagePackSerializer.Deserialize<UnequipItemMsg>(MessagePackSerializer.Serialize(new UnequipItemMsg { PaperdollSlot = 7 }));
        Assert.Equal(7, slot.PaperdollSlot);
    }

    [Fact]
    public void CombatMsgs_RoundTrip()
    {
        var status = new StatusUpdateMsg
        {
            ObjectId = 50_001,
            AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp],
            Values = [64, 96]
        };
        var attack = new AttackMsg { AttackerObjectId = 1, TargetObjectId = 50_001, Damage = 32 };
        Assert.Equal(64,
            MessagePackSerializer.Deserialize<StatusUpdateMsg>(MessagePackSerializer.Serialize(status)).Values[0]);
        Assert.Equal(32,
            MessagePackSerializer.Deserialize<AttackMsg>(MessagePackSerializer.Serialize(attack)).Damage);
    }

    [Fact]
    public void Say_MatchesProtocolV2Layout()
    {
        Assert.Equal((ushort)MsgType.Say, (ushort)Fineage.Protocol.V2.MsgType.Say);
        Assert.Equal((ushort)MsgType.CreatureSay, (ushort)Fineage.Protocol.V2.MsgType.CreatureSay);

        byte[] say = MessagePackSerializer.Serialize(new SayMsg { Text = "hi", ChatType = 0 });
        byte[] sayV2 = MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SayMsg { Text = "hi", ChatType = 0 });
        Assert.Equal(say, sayV2);

        byte[] heard = MessagePackSerializer.Serialize(new CreatureSayMsg
        {
            ObjectId = 4,
            Name = "Aria",
            Text = "hi",
            ChatType = 0
        });
        byte[] heardV2 = MessagePackSerializer.Serialize(new Fineage.Protocol.V2.CreatureSayMsg
        {
            ObjectId = 4,
            Name = "Aria",
            Text = "hi",
            ChatType = 0
        });
        Assert.Equal(heard, heardV2);
    }

    [Fact]
    public void SystemMessage_MatchesProtocolV2Layout()
    {
        Assert.Equal(1040, (ushort)MsgType.SystemMessage);
        Assert.Equal((ushort)MsgType.SystemMessage, (ushort)Fineage.Protocol.V2.MsgType.SystemMessage);
        Assert.Equal(8, (ushort)MsgType.Say);
        Assert.Equal(1012, (ushort)MsgType.CreatureSay);

        var msg = new SystemMessageMsg
        {
            Id = ChatProtocol.Welcome,
            Params = ["Aria"],
            Text = "Welcome, Aria."
        };
        byte[] legacy = MessagePackSerializer.Serialize(msg);
        byte[] v2 = MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SystemMessageMsg
        {
            Id = ChatProtocol.Welcome,
            Params = ["Aria"],
            Text = "Welcome, Aria."
        });
        Assert.Equal(legacy, v2);
        var round = MessagePackSerializer.Deserialize<SystemMessageMsg>(legacy);
        Assert.Equal(ChatProtocol.Welcome, round.Id);
        Assert.Equal("Aria", Assert.Single(round.Params));
        Assert.Equal("Welcome, Aria.", round.Text);
    }

    [Fact]
    public void DeleteObject_MatchesProtocolV2Layout()
    {
        Assert.Equal((ushort)MsgType.DeleteObject, (ushort)Fineage.Protocol.V2.MsgType.DeleteObject);
        Assert.Equal((ushort)MsgType.CharInfo, (ushort)Fineage.Protocol.V2.MsgType.CharInfo);
        Assert.Equal((ushort)MsgType.NpcInfo, (ushort)Fineage.Protocol.V2.MsgType.NpcInfo);

        byte[] legacy = MessagePackSerializer.Serialize(new DeleteObjectMsg { ObjectId = 9001 });
        byte[] v2 = MessagePackSerializer.Serialize(new Fineage.Protocol.V2.DeleteObjectMsg { ObjectId = 9001 });
        Assert.Equal(legacy, v2);

        byte[] entityLeft = MessagePackSerializer.Serialize(new Fineage.Protocol.V2.EntityLeftMsg { ObjectId = 9001 });
        Assert.Equal(legacy, entityLeft);
    }

    [Fact]
    public void SpawnItem_MatchesProtocolV2Layout()
    {
        var msg = new SpawnItemMsg
        {
            ObjectId = 100_001,
            ItemId = 57,
            X = 1,
            Y = 2,
            Z = 3,
            Stackable = true,
            Count = 25
        };
        byte[] legacy = MessagePackSerializer.Serialize(msg);
        byte[] v2 = MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SpawnItemMsg
        {
            ObjectId = 100_001,
            ItemId = 57,
            X = 1,
            Y = 2,
            Z = 3,
            Stackable = true,
            Count = 25
        });
        Assert.Equal(legacy, v2);
    }

    [Fact]
    public void LoginOpcodes_DoNotReuseZoneOpcodes()
    {
        ushort[] zone =
        [
            1, 2, 3, 4, 5, 6, 8, 9, 12, 13, 14,
            1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008, 1009, 1010, 1012, 1013,
            1018,
            1099
        ];
        ushort[] login =
        [
            (ushort)MsgType.Register,
            (ushort)MsgType.CreateCharacter,
            (ushort)MsgType.ListCharacters,
            (ushort)MsgType.SelectCharacter,
            (ushort)MsgType.DeleteCharacter,
            (ushort)MsgType.RestoreCharacter,
            (ushort)MsgType.RegisterOk,
            (ushort)MsgType.CharacterCreated,
            (ushort)MsgType.CharacterList,
            (ushort)MsgType.Handoff,
            (ushort)MsgType.LoginOk,
            (ushort)MsgType.CharacterDeleted,
            (ushort)MsgType.CharacterDeleteFailed
        ];
        Assert.All(login, op => Assert.DoesNotContain(op, zone));
    }

    [Fact]
    public void LobbyOpcodes_DoNotCollideWithZoneOrInventory()
    {
        var lobby = LobbyProtocol.ClientToServer.Concat(LobbyProtocol.ServerToClient).ToArray();
        Assert.Equal(lobby, LobbyProtocol.ClientToServer.Concat(LobbyProtocol.ServerToClient).Distinct().ToArray());
        Assert.All(lobby, op => Assert.DoesNotContain(op, LobbyProtocol.ZoneAndInventory));

        Assert.Equal(1005, (ushort)Fineage.Protocol.V2.MsgType.CharInfo);
        Assert.Equal(1006, (ushort)Fineage.Protocol.V2.MsgType.NpcInfo);
        Assert.Equal(1007, (ushort)Fineage.Protocol.V2.MsgType.DeleteObject);
        Assert.Equal(1030, (ushort)Fineage.Protocol.V2.MsgType.CharacterRoster);
        Assert.Equal(1031, (ushort)Fineage.Protocol.V2.MsgType.CreateCharacterResult);
        Assert.Equal(1032, (ushort)Fineage.Protocol.V2.MsgType.EntityLeft);
        Assert.Equal(1033, (ushort)Fineage.Protocol.V2.MsgType.ServerList);
        Assert.Equal(1011, (ushort)Fineage.Protocol.V2.MsgType.PlayToken);
        Assert.DoesNotContain((ushort)Fineage.Protocol.V2.MsgType.CharacterRoster, LobbyProtocol.ZoneAndInventory);
        Assert.DoesNotContain((ushort)Fineage.Protocol.V2.MsgType.CreateCharacterResult, LobbyProtocol.ZoneAndInventory);
        Assert.DoesNotContain((ushort)Fineage.Protocol.V2.MsgType.EntityLeft, LobbyProtocol.ZoneAndInventory);
        Assert.DoesNotContain((ushort)Fineage.Protocol.V2.MsgType.ServerList, LobbyProtocol.ZoneAndInventory);
        Assert.NotEqual((ushort)Fineage.Protocol.V2.MsgType.CharacterRoster, (ushort)Fineage.Protocol.V2.MsgType.CharInfo);
        Assert.NotEqual((ushort)Fineage.Protocol.V2.MsgType.ServerList, (ushort)MsgType.StatusUpdate);
    }

    [Fact]
    public async Task Frame_RoundTripsLengthPrefixedBody()
    {
        using var stream = new MemoryStream();
        byte[] body = [1, 2, 3, 4];
        await FrameIO.SendAsync(stream, (ushort)MsgType.Auth, body);
        stream.Position = 0;
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.Auth, frame!.Value.Type);
        Assert.Equal(body, frame.Value.Body);
        Assert.Equal(8, stream.Length);
    }

    [Fact]
    public void ClanMessages_MatchProtocolV2Layout()
    {
        byte[] ask = MessagePackSerializer.Serialize(new ClanAskJoinMsg
        {
            RequestorObjectId = 4,
            RequestorName = "Aria",
            ClanId = 2,
            ClanName = "Fine",
            PledgeType = 0
        });
        byte[] askV2 = MessagePackSerializer.Serialize(new Fineage.Protocol.V2.ClanAskJoinMsg
        {
            RequestorObjectId = 4,
            RequestorName = "Aria",
            ClanId = 2,
            ClanName = "Fine",
            PledgeType = 0
        });
        Assert.Equal(ask, askV2);

        var info = new CharInfoMsg
        {
            ObjectId = 4,
            Name = "Aria",
            X = 1,
            Y = 2,
            Z = 3,
            ClassId = 0,
            Paperdoll = new int[17],
            ClanId = 9,
            ClanCrestId = 0
        };
        var round = MessagePackSerializer.Deserialize<CharInfoMsg>(MessagePackSerializer.Serialize(info));
        Assert.Equal(9, round.ClanId);
        Assert.Equal(0, round.ClanCrestId);
        Assert.Equal(17, round.Paperdoll.Length);

        var user = MessagePackSerializer.Deserialize<UserInfoMsg>(MessagePackSerializer.Serialize(new UserInfoMsg
        {
            ObjectId = 4,
            Name = "Aria",
            ClanId = 9,
            ClanCrestId = 0
        }));
        Assert.Equal(9, user.ClanId);
        Assert.Equal(0, user.ClanCrestId);
    }
}
