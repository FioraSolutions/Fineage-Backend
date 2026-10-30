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
        Assert.Equal(28, (ushort)MsgType.TradeRequest);
        Assert.Equal(29, (ushort)MsgType.TradeAnswerRequest);
        Assert.Equal(32, (ushort)MsgType.ClanCreateRequest);
        Assert.Equal(33, (ushort)MsgType.ClanInviteRequest);
        Assert.Equal(34, (ushort)MsgType.ClanAnswerJoinRequest);
        Assert.Equal(35, (ushort)MsgType.ClanLeaveRequest);
        Assert.Equal(36, (ushort)MsgType.ClanKickRequest);
        Assert.Equal(37, (ushort)MsgType.ClanDissolveRequest);
        Assert.Equal(38, (ushort)MsgType.ClanCancelDissolveRequest);
        Assert.Equal(39, (ushort)MsgType.EnchantRequest);
        Assert.Equal(40, (ushort)MsgType.EnchantConfirm);
        Assert.Equal(41, (ushort)MsgType.EnchantCancel);
        Assert.Equal(42, (ushort)MsgType.RequestQuestList);
        Assert.Equal(43, (ushort)MsgType.RequestQuestAbort);
        Assert.Equal(44, (ushort)MsgType.NpcTalk);
        Assert.Equal(45, (ushort)MsgType.QuestEvent);
        Assert.Equal(46, (ushort)MsgType.SiegeRegisterRequest);
        Assert.Equal(47, (ushort)MsgType.SiegeDefenderAnswerRequest);
        Assert.Equal(48, (ushort)MsgType.SiegeListRequest);
        Assert.Equal(49, (ushort)MsgType.AdminCaptureCastle);
        Assert.Equal(50, (ushort)MsgType.SkillUseRequest);
        Assert.Equal(51, (ushort)MsgType.CharacterActionRequest);
        Assert.Equal(60, (ushort)MsgType.OlympiadStatusRequest);
        Assert.Equal(61, (ushort)MsgType.OlympiadRegisterRequest);
        Assert.Equal(62, (ushort)MsgType.OlympiadUnregisterRequest);
        Assert.Equal(63, (ushort)MsgType.OlympiadLeaveRequest);
        Assert.Equal(64, (ushort)MsgType.AdminReportOlympiadResult);
        Assert.Equal((ushort)MsgType.OlympiadStatusRequest, (ushort)Fineage.Protocol.V2.MsgType.OlympiadStatusRequest);
        Assert.Equal((ushort)MsgType.AdminReportOlympiadResult, (ushort)Fineage.Protocol.V2.MsgType.AdminReportOlympiadResult);
        Assert.Equal((ushort)MsgType.SiegeRegisterRequest, (ushort)Fineage.Protocol.V2.MsgType.SiegeRegisterRequest);
        Assert.Equal((ushort)MsgType.AdminCaptureCastle, (ushort)Fineage.Protocol.V2.MsgType.AdminCaptureCastle);
        Assert.Equal((ushort)MsgType.SkillUseRequest, (ushort)Fineage.Protocol.V2.MsgType.SkillUseRequest);
        Assert.Equal(95, (ushort)MsgType.BypassRequest);
        Assert.Equal(96, (ushort)MsgType.RequestBuyItem);
        Assert.Equal(97, (ushort)MsgType.RequestSellItem);
        Assert.Equal(98, (ushort)MsgType.RequestMultiSellChoose);
        Assert.Equal(99, (ushort)MsgType.RequestPreviewItem);
        Assert.Equal(ShopProtocol.BuyRequest, (ushort)MsgType.RequestBuyItem);
        Assert.Equal(ShopProtocol.Balance, (ushort)MsgType.ShopBalance);
        Assert.Equal((ushort)MsgType.RequestBuyItem, (ushort)Fineage.Protocol.V2.MsgType.RequestBuyItem);
        Assert.Equal((ushort)MsgType.RequestSellItem, (ushort)Fineage.Protocol.V2.MsgType.RequestSellItem);
        Assert.Equal((ushort)MsgType.RequestMultiSellChoose, (ushort)Fineage.Protocol.V2.MsgType.RequestMultiSellChoose);
        Assert.Equal((ushort)MsgType.RequestPreviewItem, (ushort)Fineage.Protocol.V2.MsgType.RequestPreviewItem);
        Assert.Equal((ushort)MsgType.BuyList, (ushort)Fineage.Protocol.V2.MsgType.BuyList);
        Assert.Equal((ushort)MsgType.ShopBalance, (ushort)Fineage.Protocol.V2.MsgType.ShopBalance);
        Assert.Equal(105, (ushort)MsgType.TeleportSelectRequest);
        Assert.Equal(106, (ushort)MsgType.WarehouseDepositRequest);
        Assert.Equal(107, (ushort)MsgType.WarehouseWithdrawRequest);
        Assert.Equal(108, (ushort)MsgType.SupportMagicRequest);
        Assert.Equal(109, (ushort)MsgType.GiveBlessingRequest);
        Assert.Equal(NpcServiceProtocol.TeleportSelect, (ushort)MsgType.TeleportSelectRequest);
        Assert.Equal((ushort)MsgType.TeleportSelectRequest, (ushort)Fineage.Protocol.V2.MsgType.TeleportSelectRequest);
        Assert.Equal((ushort)MsgType.WarehouseDepositRequest, (ushort)Fineage.Protocol.V2.MsgType.WarehouseDepositRequest);
        Assert.Equal((ushort)MsgType.GiveBlessingRequest, (ushort)Fineage.Protocol.V2.MsgType.GiveBlessingRequest);
        Assert.Equal(NpcHtmlProtocol.Bypass, (ushort)MsgType.BypassRequest);
        Assert.Equal(NpcHtmlProtocol.Html, (ushort)MsgType.NpcHtml);
        Assert.Equal(NpcHtmlProtocol.InteractionDistance, QuestProtocol.TalkRange);
        Assert.Equal((ushort)MsgType.RequestQuestList, (ushort)Fineage.Protocol.V2.MsgType.RequestQuestList);
        Assert.Equal((ushort)MsgType.BypassRequest, (ushort)Fineage.Protocol.V2.MsgType.BypassRequest);
        Assert.Equal((ushort)MsgType.NpcHtml, (ushort)Fineage.Protocol.V2.MsgType.NpcHtml);
        Assert.Equal((ushort)MsgType.QuestEvent, (ushort)Fineage.Protocol.V2.MsgType.QuestEvent);
        Assert.Equal((ushort)MsgType.EnchantRequest, (ushort)Fineage.Protocol.V2.MsgType.EnchantRequest);
        Assert.Equal((ushort)MsgType.EnchantConfirm, (ushort)Fineage.Protocol.V2.MsgType.EnchantConfirm);
        Assert.Equal((ushort)MsgType.EnchantCancel, (ushort)Fineage.Protocol.V2.MsgType.EnchantCancel);
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
        Assert.Equal(1019, (ushort)MsgType.DropItem);
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
        Assert.Equal(1060, (ushort)MsgType.EnchantResult);
        Assert.Equal(1061, (ushort)MsgType.QuestList);
        Assert.Equal(1062, (ushort)MsgType.QuestDialog);
        Assert.Equal(1063, (ushort)MsgType.QuestSound);
        Assert.Equal(1064, (ushort)MsgType.SiegeInfo);
        Assert.Equal(1065, (ushort)MsgType.SkillList);
        Assert.Equal(1066, (ushort)MsgType.MagicSkillUse);
        Assert.Equal(1067, (ushort)MsgType.ChangeWaitType);
        Assert.Equal(1068, (ushort)MsgType.SocialAction);
        Assert.Equal(1069, (ushort)MsgType.ChangeMoveType);
        Assert.Equal(1074, (ushort)MsgType.Die);
        Assert.Equal(1075, (ushort)MsgType.Revive);
        Assert.Equal(1076, (ushort)MsgType.TeleportToLocation);
        Assert.Equal(1077, (ushort)MsgType.TradeAsk);
        Assert.Equal(1078, (ushort)MsgType.TradeRequestResult);
        Assert.Equal(1079, (ushort)MsgType.TradeStart);
        Assert.Equal(1080, (ushort)MsgType.TradeOfferUpdate);
        Assert.Equal(1081, (ushort)MsgType.TradeConfirmState);
        Assert.Equal(1082, (ushort)MsgType.TradeFinished);
        Assert.Equal(52, (ushort)MsgType.ReviveRequest);
        Assert.Equal(53, (ushort)MsgType.AdminKillPlayer);
        Assert.Equal(54, (ushort)MsgType.TradeAddItemRequest);
        Assert.Equal(55, (ushort)MsgType.TradeDoneRequest);
        Assert.Equal(56, (ushort)MsgType.DropItemRequest);
        Assert.Equal((ushort)MsgType.CharacterActionRequest, (ushort)Fineage.Protocol.V2.MsgType.CharacterActionRequest);
        Assert.Equal((ushort)MsgType.ReviveRequest, (ushort)Fineage.Protocol.V2.MsgType.ReviveRequest);
        Assert.Equal((ushort)MsgType.Die, (ushort)Fineage.Protocol.V2.MsgType.Die);
        Assert.Equal((ushort)MsgType.Revive, (ushort)Fineage.Protocol.V2.MsgType.Revive);
        Assert.Equal((ushort)MsgType.TeleportToLocation, (ushort)Fineage.Protocol.V2.MsgType.TeleportToLocation);
        Assert.Equal((ushort)MsgType.ChangeWaitType, (ushort)Fineage.Protocol.V2.MsgType.ChangeWaitType);
        Assert.Equal((ushort)MsgType.SocialAction, (ushort)Fineage.Protocol.V2.MsgType.SocialAction);
        Assert.Equal((ushort)MsgType.ChangeMoveType, (ushort)Fineage.Protocol.V2.MsgType.ChangeMoveType);
        Assert.Equal(ActionProtocol.SitStand, 0);
        Assert.Equal(ActionProtocol.WalkRun, 1);
        Assert.Equal((ushort)MsgType.SiegeInfo, (ushort)Fineage.Protocol.V2.MsgType.SiegeInfo);
        Assert.Equal((ushort)MsgType.SkillList, (ushort)Fineage.Protocol.V2.MsgType.SkillList);
        Assert.Equal(1070, (ushort)MsgType.OlympiadStatus);
        Assert.Equal(1071, (ushort)MsgType.OlympiadMatch);
        Assert.Equal(1072, (ushort)MsgType.OlympiadResult);
        Assert.Equal(1073, (ushort)MsgType.OlympiadCountdown);
        Assert.Equal((ushort)MsgType.OlympiadStatus, (ushort)Fineage.Protocol.V2.MsgType.OlympiadStatus);
        Assert.Equal((ushort)MsgType.OlympiadResult, (ushort)Fineage.Protocol.V2.MsgType.OlympiadResult);
        Assert.Equal((ushort)MsgType.SiegeInfo, (ushort)Fineage.Protocol.V2.MsgType.SiegeInfo);
        Assert.Equal(OlympiadProtocol.StartHour, 18);
        Assert.Equal(OlympiadProtocol.ClassedMin, 5);
        Assert.Equal(OlympiadProtocol.NonClassedMin, 9);
        Assert.Equal(OlympiadProtocol.StartPoints, 18);
        Assert.Equal(OlympiadProtocol.NotInProgress, 160);
        Assert.Equal(OlympiadProtocol.MatchDecided, 179);
        Assert.Equal(SiegeProtocol.MinClanLevel, 4);
        Assert.Equal(SiegeProtocol.CycleWeeks, 2);
        Assert.Equal(SiegeProtocol.RegisteredAttacker, 140);
        Assert.Equal(SiegeProtocol.CastleCaptured, 149);
        Assert.Equal(1094, (ushort)MsgType.NpcHtml);
        Assert.Equal(1095, (ushort)MsgType.BuyList);
        Assert.Equal(1096, (ushort)MsgType.SellList);
        Assert.Equal(1097, (ushort)MsgType.MultiSellList);
        Assert.Equal(1098, (ushort)MsgType.ShopBalance);
        Assert.Equal(1105, (ushort)MsgType.TeleportList);
        Assert.Equal(1106, (ushort)MsgType.WarehouseDepositList);
        Assert.Equal(1107, (ushort)MsgType.WarehouseWithdrawList);
        Assert.Equal(1108, (ushort)MsgType.NewbieBuffResult);
        Assert.Equal(1109, (ushort)MsgType.BlessingResult);
        Assert.Equal(NpcServiceProtocol.TeleportList, (ushort)MsgType.TeleportList);
        Assert.Equal((ushort)MsgType.TeleportList, (ushort)Fineage.Protocol.V2.MsgType.TeleportList);
        Assert.Equal((ushort)MsgType.BlessingResult, (ushort)Fineage.Protocol.V2.MsgType.BlessingResult);
        Assert.Equal((ushort)MsgType.QuestList, (ushort)Fineage.Protocol.V2.MsgType.QuestList);
        Assert.Equal(QuestProtocol.Acquired, 30);
        Assert.Equal(QuestProtocol.NotAbortable, 38);
        Assert.Equal(QuestProtocol.MaxActive, 25);
        Assert.Equal((ushort)MsgType.EnchantResult, (ushort)Fineage.Protocol.V2.MsgType.EnchantResult);
        Assert.Equal(ChatProtocol.ChatBandFirst, (ushort)MsgType.SystemMessage);
        Assert.Equal((ushort)MsgType.SystemMessage, (ushort)Fineage.Protocol.V2.MsgType.SystemMessage);
        Assert.Equal((ushort)MsgType.ItemList, (ushort)Fineage.Protocol.V2.MsgType.ItemList);
        Assert.Equal((ushort)MsgType.InventoryUpdate, (ushort)Fineage.Protocol.V2.MsgType.InventoryUpdate);
        Assert.Equal((ushort)MsgType.UserInfo, (ushort)Fineage.Protocol.V2.MsgType.UserInfo);
        Assert.Equal((ushort)MsgType.AbnormalStatusUpdate, (ushort)Fineage.Protocol.V2.MsgType.AbnormalStatusUpdate);
        Assert.Equal((ushort)MsgType.SpawnItem, (ushort)Fineage.Protocol.V2.MsgType.SpawnItem);
        Assert.Equal((ushort)MsgType.DropItem, (ushort)Fineage.Protocol.V2.MsgType.DropItem);
        Assert.Equal((ushort)MsgType.DropItemRequest, (ushort)Fineage.Protocol.V2.MsgType.DropItemRequest);
        Assert.Equal((ushort)MsgType.PartyInviteRequest, (ushort)Fineage.Protocol.V2.MsgType.PartyInviteRequest);
        Assert.Equal((ushort)MsgType.PartyAskJoin, (ushort)Fineage.Protocol.V2.MsgType.PartyAskJoin);
        Assert.Equal((ushort)MsgType.TradeRequest, (ushort)Fineage.Protocol.V2.MsgType.TradeRequest);
        Assert.Equal((ushort)MsgType.TradeFinished, (ushort)Fineage.Protocol.V2.MsgType.TradeFinished);
        Assert.Equal(TradeProtocol.Distance, QuestProtocol.TalkRange);
        Assert.Equal(90, (ushort)MsgType.SevenSignsStatusRequest);
        Assert.Equal(91, (ushort)MsgType.SevenSignsActionRequest);
        Assert.Equal(92, (ushort)MsgType.DungeonEnterRequest);
        Assert.Equal(1090, (ushort)MsgType.SevenSignsStatus);
        Assert.Equal(1091, (ushort)MsgType.SevenSignsSky);
        Assert.Equal(1092, (ushort)MsgType.DungeonEntered);
        Assert.Equal((ushort)MsgType.DungeonEnterRequest, (ushort)Fineage.Protocol.V2.MsgType.DungeonEnterRequest);
        Assert.Equal((ushort)MsgType.DungeonEntered, (ushort)Fineage.Protocol.V2.MsgType.DungeonEntered);
        Assert.Equal(DungeonProtocol.DawnOnlyMessage, 1301);
        Assert.Equal(DungeonProtocol.DuskOnlyMessage, 1302);
        Assert.Equal(DungeonProtocol.GateRefusedMessage, 137);
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
    public void Die_MatchesProtocolV2Layout()
    {
        Assert.Equal((ushort)MsgType.Die, (ushort)Fineage.Protocol.V2.MsgType.Die);
        byte[] legacy = MessagePackSerializer.Serialize(new DieMsg { ObjectId = 42, KillerObjectId = 7 });
        byte[] v2 = MessagePackSerializer.Serialize(new Fineage.Protocol.V2.DieMsg { ObjectId = 42, KillerObjectId = 7 });
        Assert.Equal(legacy, v2);
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
