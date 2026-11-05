using Fineage.Protocol;
using Xunit;

namespace Fineage.Login.Tests;

public class ProtocolCompatTests
{
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
        Assert.Equal(95, (ushort)MsgType.BypassRequest);
        Assert.Equal(96, (ushort)MsgType.RequestBuyItem);
        Assert.Equal(97, (ushort)MsgType.RequestSellItem);
        Assert.Equal(98, (ushort)MsgType.RequestMultiSellChoose);
        Assert.Equal(99, (ushort)MsgType.RequestPreviewItem);
        Assert.Equal(ShopProtocol.BuyRequest, (ushort)MsgType.RequestBuyItem);
        Assert.Equal(ShopProtocol.Balance, (ushort)MsgType.ShopBalance);
        Assert.Equal(105, (ushort)MsgType.TeleportSelectRequest);
        Assert.Equal(106, (ushort)MsgType.WarehouseDepositRequest);
        Assert.Equal(107, (ushort)MsgType.WarehouseWithdrawRequest);
        Assert.Equal(108, (ushort)MsgType.SupportMagicRequest);
        Assert.Equal(109, (ushort)MsgType.GiveBlessingRequest);
        Assert.Equal(NpcServiceProtocol.TeleportSelect, (ushort)MsgType.TeleportSelectRequest);
        Assert.Equal(NpcHtmlProtocol.Bypass, (ushort)MsgType.BypassRequest);
        Assert.Equal(NpcHtmlProtocol.Html, (ushort)MsgType.NpcHtml);
        Assert.Equal(NpcHtmlProtocol.InteractionDistance, QuestProtocol.TalkRange);
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
        Assert.Equal(ActionProtocol.SitStand, 0);
        Assert.Equal(ActionProtocol.WalkRun, 1);
        Assert.Equal(1070, (ushort)MsgType.OlympiadStatus);
        Assert.Equal(1071, (ushort)MsgType.OlympiadMatch);
        Assert.Equal(1072, (ushort)MsgType.OlympiadResult);
        Assert.Equal(1073, (ushort)MsgType.OlympiadCountdown);
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
        Assert.Equal(QuestProtocol.Acquired, 30);
        Assert.Equal(QuestProtocol.NotAbortable, 38);
        Assert.Equal(QuestProtocol.MaxActive, 25);
        Assert.Equal(ChatProtocol.ChatBandFirst, (ushort)MsgType.SystemMessage);
        Assert.Equal(TradeProtocol.Distance, QuestProtocol.TalkRange);
        Assert.Equal(90, (ushort)MsgType.SevenSignsStatusRequest);
        Assert.Equal(91, (ushort)MsgType.SevenSignsActionRequest);
        Assert.Equal(92, (ushort)MsgType.DungeonEnterRequest);
        Assert.Equal(1090, (ushort)MsgType.SevenSignsStatus);
        Assert.Equal(1091, (ushort)MsgType.SevenSignsSky);
        Assert.Equal(1092, (ushort)MsgType.DungeonEntered);
        Assert.Equal(DungeonProtocol.DawnOnlyMessage, 1301);
        Assert.Equal(DungeonProtocol.DuskOnlyMessage, 1302);
        Assert.Equal(DungeonProtocol.GateRefusedMessage, 137);
        Assert.Equal(1099, (ushort)MsgType.Error);
        AssertUnique<MsgType>();
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
        Assert.Equal(lobby, lobby.Distinct().ToArray());
        Assert.All(lobby, op => Assert.DoesNotContain(op, LobbyProtocol.ZoneAndInventory));
    }
}
