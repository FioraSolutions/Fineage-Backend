namespace Fineage.Zone.World;

/// <summary>
/// Turns a descriptor back into an <see cref="IWorldEventPublisher"/> call. One static switch for every kind;
/// the batch does not store a delegate per event.
/// </summary>
internal static class FanoutDispatch
{
    public static void Invoke(IWorldEventPublisher? publisher, FanoutKind kind, AudienceSnapshotId audience, in FanoutArgs args)
    {
        if (kind == FanoutKind.Prepared)
        {
            ((Action)args.Payload!)();
            return;
        }

        if (publisher == null)
            throw new InvalidOperationException($"fan-out {kind} has no publisher");

        var watchers = AudienceSnapshots.Watchers(audience);
        switch (kind)
        {
            case FanoutKind.MoveState:
                publisher.FanoutMoveState(watchers, args.A, args.B, args.C, args.D, args.E, args.F, args.G, args.Flag);
                return;
            case FanoutKind.ChangeMoveType:
                publisher.FanoutChangeMoveType(watchers, args.A, args.Flag, args.Fa, args.Fb, args.Flag2);
                return;
            case FanoutKind.PositionCorrection:
                publisher.FanoutPositionCorrection(watchers, args.A, args.B, args.C, args.D, args.E);
                return;
            case FanoutKind.EtcStatusUpdate:
                publisher.FanoutEtcStatusUpdate(watchers, ((Fineage.Protocol.EtcStatusUpdateMsg)args.Payload!));
                return;
            case FanoutKind.AttackStance:
                publisher.FanoutAttackStance(watchers, args.A, args.Flag);
                return;
            case FanoutKind.AutoSoulShot:
                publisher.FanoutAutoSoulShot(watchers, args.A, args.Flag);
                return;
            case FanoutKind.UserInfo:
                publisher.FanoutUserInfo(watchers, ((Fineage.Protocol.UserInfoMsg)args.Payload!));
                return;
            case FanoutKind.CharInfo:
                ((Action)args.Payload!)();
                return;
            case FanoutKind.AttackState:
                publisher.FanoutAttackState(watchers, ((Fineage.Protocol.AttackMsg)args.Payload!));
                return;
            case FanoutKind.StatusUpdate:
                publisher.FanoutStatusUpdate(watchers, ((Fineage.Protocol.StatusUpdateMsg)args.Payload!));
                return;
            case FanoutKind.Die:
                publisher.FanoutDie(watchers, args.A, args.B, args.C);
                return;
            case FanoutKind.DropItem:
                publisher.FanoutDropItem(watchers, ((Fineage.Protocol.DropItemMsg)args.Payload!));
                return;
            case FanoutKind.EntityDespawn:
                publisher.FanoutEntityDespawn(watchers, args.A);
                return;
            case FanoutKind.InventoryUpdate:
                publisher.FanoutInventoryUpdate(watchers, ((Fineage.Protocol.InventoryUpdateMsg)args.Payload!));
                return;
            case FanoutKind.ItemList:
                publisher.FanoutItemList(watchers, ((Fineage.Protocol.ItemListMsg)args.Payload!));
                return;
            case FanoutKind.CreatureSay:
                publisher.FanoutCreatureSay(watchers, ((Fineage.Protocol.CreatureSayMsg)args.Payload!));
                return;
            case FanoutKind.SystemMessage:
                publisher.FanoutSystemMessage(watchers, ((Fineage.Protocol.SystemMessageMsg)args.Payload!));
                return;
            case FanoutKind.MagicSkillUse:
                publisher.FanoutMagicSkillUse(watchers, ((Fineage.Protocol.MagicSkillUseMsg)args.Payload!));
                return;
            case FanoutKind.Revive:
                publisher.FanoutRevive(watchers, args.A);
                return;
            case FanoutKind.TeleportToLocation:
                publisher.FanoutTeleportToLocation(watchers, args.A, args.B, args.C, args.D, args.E);
                return;
            case FanoutKind.PlayerStats:
                publisher.FanoutPlayerStats(watchers, ((Fineage.Protocol.PlayerStatsMsg)args.Payload!));
                return;
            case FanoutKind.AbnormalStatusUpdate:
                publisher.FanoutAbnormalStatusUpdate(watchers, ((Fineage.Protocol.AbnormalStatusUpdateMsg)args.Payload!));
                return;
            case FanoutKind.NpcHtml:
                publisher.FanoutNpcHtml(watchers, ((Fineage.Protocol.NpcHtmlMsg)args.Payload!));
                return;
            case FanoutKind.BuyList:
                publisher.FanoutBuyList(watchers, ((Fineage.Protocol.BuyListMsg)args.Payload!));
                return;
            case FanoutKind.SellList:
                publisher.FanoutSellList(watchers, ((Fineage.Protocol.SellListMsg)args.Payload!));
                return;
            case FanoutKind.MultiSellList:
                publisher.FanoutMultiSellList(watchers, ((Fineage.Protocol.MultiSellListMsg)args.Payload!));
                return;
            case FanoutKind.ShopPreviewList:
                publisher.FanoutShopPreviewList(watchers, ((Fineage.Protocol.ShopPreviewListMsg)args.Payload!));
                return;
            case FanoutKind.ShopPreviewInfo:
                publisher.FanoutShopPreviewInfo(watchers, ((Fineage.Protocol.ShopPreviewInfoMsg)args.Payload!));
                return;
            case FanoutKind.Dice:
                publisher.FanoutDice(watchers, ((Fineage.Protocol.DiceMsg)args.Payload!));
                return;
            case FanoutKind.ShowMiniMap:
                publisher.FanoutShowMiniMap(watchers, ((Fineage.Protocol.ShowMiniMapMsg)args.Payload!));
                return;
            case FanoutKind.ShowCalculator:
                publisher.FanoutShowCalculator(watchers, ((Fineage.Protocol.ShowCalculatorMsg)args.Payload!));
                return;
            case FanoutKind.ShowXMasSeal:
                publisher.FanoutShowXMasSeal(watchers, ((Fineage.Protocol.ShowXMasSealMsg)args.Payload!));
                return;
            case FanoutKind.ShopBalance:
                publisher.FanoutShopBalance(watchers, ((Fineage.Protocol.ShopBalanceMsg)args.Payload!));
                return;
            case FanoutKind.WarehouseDepositList:
                publisher.FanoutWarehouseDepositList(watchers, ((Fineage.Protocol.WarehouseDepositListMsg)args.Payload!));
                return;
            case FanoutKind.WarehouseWithdrawList:
                publisher.FanoutWarehouseWithdrawList(watchers, ((Fineage.Protocol.WarehouseWithdrawListMsg)args.Payload!));
                return;
            case FanoutKind.PackageToList:
                publisher.FanoutPackageToList(watchers, ((Fineage.Protocol.PackageToListMsg)args.Payload!));
                return;
            case FanoutKind.PackageSendableList:
                publisher.FanoutPackageSendableList(watchers, ((Fineage.Protocol.PackageSendableListMsg)args.Payload!));
                return;
            case FanoutKind.QuestList:
                publisher.FanoutQuestList(watchers, ((Fineage.Protocol.QuestListMsg)args.Payload!));
                return;
            case FanoutKind.QuestDirection:
                publisher.FanoutQuestDirection(watchers, ((Fineage.Net.Contract.QuestDirectionNetMsg)args.Payload!));
                return;
            case FanoutKind.QuestNpcMarks:
                publisher.FanoutQuestNpcMarks(watchers, ((Fineage.Net.Contract.QuestNpcMarksNetMsg)args.Payload!));
                return;
            case FanoutKind.TutorialUi:
                publisher.FanoutTutorialUi(watchers, ((Fineage.Net.Contract.TutorialUiNetMsg)args.Payload!));
                return;
            case FanoutKind.SkillList:
                publisher.FanoutSkillList(watchers, ((Fineage.Protocol.SkillListMsg)args.Payload!));
                return;
            case FanoutKind.ShortcutInit:
                publisher.FanoutShortcutInit(watchers, ((Fineage.Protocol.ShortCutInitMsg)args.Payload!));
                return;
            case FanoutKind.MacroList:
                publisher.FanoutMacroList(watchers, ((Fineage.Protocol.MacroListMsg)args.Payload!));
                return;
            case FanoutKind.ChangeWaitType:
                publisher.FanoutChangeWaitType(watchers, args.A, args.B, args.C, args.D, args.E);
                return;
            case FanoutKind.SocialAction:
                publisher.FanoutSocialAction(watchers, args.A, args.B);
                return;
            case FanoutKind.TargetSelected:
                publisher.FanoutTargetSelected(watchers, args.A, args.B, args.C, args.D, args.E, args.F);
                return;
            case FanoutKind.TargetUnselected:
                publisher.FanoutTargetUnselected(watchers, args.A, args.B);
                return;
            case FanoutKind.AcquireSkillList:
                publisher.FanoutAcquireSkillList(watchers, ((Fineage.Protocol.AcquireSkillListMsg)args.Payload!));
                return;
            case FanoutKind.AcquireSkillInfo:
                publisher.FanoutAcquireSkillInfo(watchers, ((Fineage.Protocol.AcquireSkillInfoMsg)args.Payload!));
                return;
            case FanoutKind.EnchantSkillList:
                publisher.FanoutEnchantSkillList(watchers, ((Fineage.Protocol.EnchantSkillListMsg)args.Payload!));
                return;
            case FanoutKind.EnchantSkillInfo:
                publisher.FanoutEnchantSkillInfo(watchers, ((Fineage.Protocol.EnchantSkillInfoMsg)args.Payload!));
                return;
            case FanoutKind.EnchantResult:
                publisher.FanoutEnchantResult(watchers, ((Fineage.Protocol.EnchantResultMsg)args.Payload!));
                return;
            case FanoutKind.AugmentShowMakeWindow:
                publisher.FanoutAugmentShowMakeWindow(watchers);
                return;
            case FanoutKind.AugmentShowCancelWindow:
                publisher.FanoutAugmentShowCancelWindow(watchers);
                return;
            case FanoutKind.AugmentPutItemResult:
                publisher.FanoutAugmentPutItemResult(watchers, ((Fineage.Protocol.VariationPutItemMsg)args.Payload!));
                return;
            case FanoutKind.AugmentPutRefinerResult:
                publisher.FanoutAugmentPutRefinerResult(watchers, ((Fineage.Protocol.VariationPutRefinerMsg)args.Payload!));
                return;
            case FanoutKind.AugmentPutGemResult:
                publisher.FanoutAugmentPutGemResult(watchers, ((Fineage.Protocol.VariationPutGemMsg)args.Payload!));
                return;
            case FanoutKind.AugmentVariationResult:
                publisher.FanoutAugmentVariationResult(watchers, ((Fineage.Protocol.VariationResultMsg)args.Payload!));
                return;
            case FanoutKind.AugmentPutCancelItemResult:
                publisher.FanoutAugmentPutCancelItemResult(watchers, ((Fineage.Protocol.VariationCancelItemMsg)args.Payload!));
                return;
            case FanoutKind.AugmentCancelResult:
                publisher.FanoutAugmentCancelResult(watchers, ((Fineage.Protocol.VariationCancelResultMsg)args.Payload!));
                return;
            case FanoutKind.PledgeStatus:
                publisher.FanoutPledgeStatus(watchers, ((Fineage.Protocol.PledgeStatusChangedMsg)args.Payload!));
                return;
            case FanoutKind.PledgeMemberList:
                publisher.FanoutPledgeMemberList(watchers, ((Fineage.Protocol.PledgeMemberListMsg)args.Payload!));
                return;
            case FanoutKind.CrestData:
                publisher.FanoutCrestData(watchers, ((Fineage.Net.Contract.CrestDataNetMsg)args.Payload!));
                return;
            case FanoutKind.PledgeWarList:
                publisher.FanoutPledgeWarList(watchers, ((Fineage.Net.Contract.PledgeWarListNetMsg)args.Payload!));
                return;
            case FanoutKind.RelationChanged:
                publisher.FanoutRelationChanged(watchers, ((Fineage.Net.Contract.RelationChangedNetMsg)args.Payload!));
                return;
            case FanoutKind.PledgeInfo:
                publisher.FanoutPledgeInfo(watchers, ((Fineage.Net.Contract.PledgeInfoNetMsg)args.Payload!));
                return;
            case FanoutKind.AllyInfo:
                publisher.FanoutAllyInfo(watchers, ((Fineage.Net.Contract.AllyInfoNetMsg)args.Payload!));
                return;
            case FanoutKind.AllyAskJoin:
                publisher.FanoutAllyAskJoin(watchers, ((Fineage.Net.Contract.AllyAskJoinNetMsg)args.Payload!));
                return;
            case FanoutKind.ClanHallList:
                publisher.FanoutClanHallList(watchers, ((Fineage.Net.Contract.ClanHallListNetMsg)args.Payload!));
                return;
            case FanoutKind.AgitDecoInfo:
                publisher.FanoutAgitDecoInfo(watchers, ((Fineage.Net.Contract.AgitDecoInfoNetMsg)args.Payload!));
                return;
            case FanoutKind.ClientSetTime:
                publisher.FanoutClientSetTime(watchers, ((Fineage.Net.Contract.ClientSetTimeNetMsg)args.Payload!));
                return;
            case FanoutKind.DoorStatus:
                publisher.FanoutDoorStatus(watchers, ((Fineage.Net.Contract.DoorStatusNetMsg)args.Payload!));
                return;
            case FanoutKind.CastleInfo:
                publisher.FanoutCastleInfo(watchers, ((Fineage.Net.Contract.CastleInfoNetMsg)args.Payload!));
                return;
            case FanoutKind.ManorInfo:
                publisher.FanoutManorInfo(watchers, ((Fineage.Net.Contract.ManorInfoNetMsg)args.Payload!));
                return;
            case FanoutKind.Fishing:
                publisher.FanoutFishing(watchers, ((Fineage.Net.Contract.FishingEventNetMsg)args.Payload!));
                return;
            case FanoutKind.SummonInfo:
                publisher.FanoutSummonInfo(watchers, ((Fineage.Net.Contract.SummonInfoNetMsg)args.Payload!));
                return;
            case FanoutKind.SummonGone:
                publisher.FanoutSummonGone(watchers, ((Fineage.Net.Contract.SummonGoneNetMsg)args.Payload!));
                return;
            case FanoutKind.PetInventory:
                publisher.FanoutPetInventory(watchers, ((Fineage.Net.Contract.PetInventoryNetMsg)args.Payload!));
                return;
            case FanoutKind.Ride:
                publisher.FanoutRide(watchers, ((Fineage.Net.Contract.RideNetMsg)args.Payload!));
                return;
            case FanoutKind.PlayerActionState:
                publisher.FanoutPlayerActionState(watchers, ((Fineage.Net.Contract.PlayerActionStateNetMsg)args.Payload!));
                return;
            case FanoutKind.DuelEvent:
                publisher.FanoutDuelEvent(watchers, ((Fineage.Net.Contract.DuelEventNetMsg)args.Payload!));
                return;
            case FanoutKind.DuelUserInfo:
                publisher.FanoutDuelUserInfo(watchers, ((Fineage.Net.Contract.DuelUserInfoNetMsg)args.Payload!));
                return;
            case FanoutKind.ClanAskJoin:
                publisher.FanoutClanAskJoin(watchers, ((Fineage.Protocol.ClanAskJoinMsg)args.Payload!));
                return;
            case FanoutKind.ClanJoinResult:
                publisher.FanoutClanJoinResult(watchers, ((Fineage.Protocol.ClanJoinResultMsg)args.Payload!));
                return;
            case FanoutKind.PartyAskJoin:
                publisher.FanoutPartyAskJoin(watchers, ((Fineage.Protocol.PartyAskJoinMsg)args.Payload!));
                return;
            case FanoutKind.PartyJoinResult:
                publisher.FanoutPartyJoinResult(watchers, ((Fineage.Protocol.PartyJoinResultMsg)args.Payload!));
                return;
            case FanoutKind.PartyMemberList:
                publisher.FanoutPartyMemberList(watchers, ((Fineage.Protocol.PartyMemberListMsg)args.Payload!));
                return;
            case FanoutKind.PartyMemberUpdate:
                publisher.FanoutPartyMemberUpdate(watchers, ((Fineage.Protocol.PartyMemberUpdateMsg)args.Payload!));
                return;
            case FanoutKind.PartySpelled:
                publisher.FanoutPartySpelled(watchers, ((Fineage.Protocol.AbnormalStatusUpdateMsg)args.Payload!));
                return;
            case FanoutKind.PartyMemberPosition:
                publisher.FanoutPartyMemberPosition(watchers, ((Fineage.Protocol.PartyMemberPositionMsg)args.Payload!));
                return;
            case FanoutKind.CommandChannelAskJoin:
                publisher.FanoutCommandChannelAskJoin(watchers, ((Fineage.Protocol.CommandChannelAskJoinMsg)args.Payload!));
                return;
            case FanoutKind.CommandChannelOpen:
                publisher.FanoutCommandChannelOpen(watchers);
                return;
            case FanoutKind.CommandChannelClose:
                publisher.FanoutCommandChannelClose(watchers);
                return;
            case FanoutKind.CommandChannelInfo:
                publisher.FanoutCommandChannelInfo(watchers, ((Fineage.Protocol.CommandChannelInfoMsg)args.Payload!));
                return;
            case FanoutKind.PartyMatchDetail:
                publisher.FanoutPartyMatchDetail(watchers, ((Fineage.Protocol.PartyMatchDetailMsg)args.Payload!));
                return;
            case FanoutKind.PartyMatchRoomList:
                publisher.FanoutPartyMatchRoomList(watchers, ((Fineage.Protocol.PartyMatchRoomListMsg)args.Payload!));
                return;
            case FanoutKind.PartyRoomMemberList:
                publisher.FanoutPartyRoomMemberList(watchers, ((Fineage.Protocol.PartyRoomMemberListMsg)args.Payload!));
                return;
            case FanoutKind.PartyRoomMemberUpdate:
                publisher.FanoutPartyRoomMemberUpdate(watchers, ((Fineage.Protocol.PartyRoomMemberUpdateMsg)args.Payload!));
                return;
            case FanoutKind.PartyRoomClose:
                publisher.FanoutPartyRoomClose(watchers);
                return;
            case FanoutKind.PartyRoomAskJoin:
                publisher.FanoutPartyRoomAskJoin(watchers, ((Fineage.Protocol.PartyRoomAskJoinMsg)args.Payload!));
                return;
            case FanoutKind.PartyMatchWaitingRoom:
                publisher.FanoutPartyMatchWaitingRoom(watchers, ((Fineage.Protocol.PartyMatchWaitingRoomMsg)args.Payload!));
                return;
            case FanoutKind.ShowBoard:
                publisher.FanoutShowBoard(watchers, ((Fineage.Protocol.ShowBoardMsg)args.Payload!));
                return;
            case FanoutKind.MagicSkillLaunched:
                publisher.FanoutMagicSkillLaunched(watchers, args.A, args.B, args.C, ((int[])args.Payload!));
                return;
            case FanoutKind.MagicSkillCanceled:
                publisher.FanoutMagicSkillCanceled(watchers, args.A);
                return;
            case FanoutKind.PlaySound:
                publisher.FanoutPlaySound(watchers, args.A, ((string)args.Payload!), args.B, args.C, args.D, args.E, args.F);
                return;
            case FanoutKind.MonRaceInfo:
                publisher.FanoutMonRaceInfo(watchers, ((Fineage.Net.Contract.MonRaceInfoNetMsg)args.Payload!));
                return;
            case FanoutKind.VehicleInfo:
                publisher.FanoutVehicleInfo(watchers, ((Fineage.Net.Contract.VehicleInfoNetMsg)args.Payload!));
                return;
            case FanoutKind.VehicleDeparture:
                publisher.FanoutVehicleDeparture(watchers, ((Fineage.Net.Contract.VehicleDepartureNetMsg)args.Payload!));
                return;
            case FanoutKind.TradeAsk:
                publisher.FanoutTradeAsk(watchers, ((Fineage.Protocol.TradeAskMsg)args.Payload!));
                return;
            case FanoutKind.TradeRequestResult:
                publisher.FanoutTradeRequestResult(watchers, ((Fineage.Protocol.TradeRequestResultMsg)args.Payload!));
                return;
            case FanoutKind.TradeStart:
                publisher.FanoutTradeStart(watchers, ((Fineage.Protocol.TradeStartMsg)args.Payload!));
                return;
            case FanoutKind.TradeOfferUpdate:
                publisher.FanoutTradeOfferUpdate(watchers, ((Fineage.Protocol.TradeOfferUpdateMsg)args.Payload!));
                return;
            case FanoutKind.TradeConfirmState:
                publisher.FanoutTradeConfirmState(watchers, ((Fineage.Protocol.TradeConfirmStateMsg)args.Payload!));
                return;
            case FanoutKind.TradeFinished:
                publisher.FanoutTradeFinished(watchers, ((Fineage.Protocol.TradeFinishedMsg)args.Payload!));
                return;
            case FanoutKind.PrivateStoreInfo:
                publisher.FanoutPrivateStoreInfo(watchers, ((Fineage.Protocol.PrivateStoreInfoMsg)args.Payload!));
                return;
            case FanoutKind.PrivateStoreListSell:
                publisher.FanoutPrivateStoreListSell(watchers, ((Fineage.Protocol.PrivateStoreListSellMsg)args.Payload!));
                return;
            case FanoutKind.PrivateStoreListBuy:
                publisher.FanoutPrivateStoreListBuy(watchers, ((Fineage.Protocol.PrivateStoreListBuyMsg)args.Payload!));
                return;
            case FanoutKind.RecipeBookList:
                publisher.FanoutRecipeBookList(watchers, ((Fineage.Protocol.RecipeBookListMsg)args.Payload!));
                return;
            case FanoutKind.RecipeItemMakeInfo:
                publisher.FanoutRecipeItemMakeInfo(watchers, ((Fineage.Protocol.RecipeItemMakeInfoMsg)args.Payload!));
                return;
            case FanoutKind.RecipeShopSellList:
                publisher.FanoutRecipeShopSellList(watchers, ((Fineage.Protocol.RecipeShopSellListMsg)args.Payload!));
                return;
            case FanoutKind.RecipeShopItemInfo:
                publisher.FanoutRecipeShopItemInfo(watchers, ((Fineage.Protocol.RecipeShopItemInfoMsg)args.Payload!));
                return;
            case FanoutKind.FriendInviteAsk:
                publisher.FanoutFriendInviteAsk(watchers, ((Fineage.Protocol.FriendInviteAskMsg)args.Payload!));
                return;
            case FanoutKind.WeddingEngageAsk:
                publisher.FanoutWeddingEngageAsk(watchers, ((Fineage.Protocol.WeddingEngageAskMsg)args.Payload!));
                return;
            case FanoutKind.FriendList:
                publisher.FanoutFriendList(watchers, ((Fineage.Protocol.FriendListMsg)args.Payload!));
                return;
            case FanoutKind.FriendUpdate:
                publisher.FanoutFriendUpdate(watchers, ((Fineage.Protocol.FriendUpdateMsg)args.Payload!));
                return;
            case FanoutKind.BlockList:
                publisher.FanoutBlockList(watchers, ((Fineage.Protocol.BlockListMsg)args.Payload!));
                return;
            case FanoutKind.BossStatus:
                publisher.FanoutBossStatus(watchers, ((Fineage.Protocol.BossStatusMsg)args.Payload!));
                return;
            case FanoutKind.SiegeInfo:
                publisher.FanoutSiegeInfo(watchers, ((Fineage.Protocol.SiegeInfoMsg)args.Payload!));
                return;
            case FanoutKind.OlympiadStatus:
                publisher.FanoutOlympiadStatus(watchers, ((Fineage.Protocol.OlympiadStatusMsg)args.Payload!));
                return;
            case FanoutKind.OlympiadMatch:
                publisher.FanoutOlympiadMatch(watchers, ((Fineage.Protocol.OlympiadMatchMsg)args.Payload!));
                return;
            case FanoutKind.OlympiadCountdown:
                publisher.FanoutOlympiadCountdown(watchers, ((Fineage.Protocol.OlympiadCountdownMsg)args.Payload!));
                return;
            case FanoutKind.OlympiadResult:
                publisher.FanoutOlympiadResult(watchers, ((Fineage.Protocol.OlympiadResultMsg)args.Payload!));
                return;
            case FanoutKind.SevenSignsStatus:
                publisher.FanoutSevenSignsStatus(watchers, ((Fineage.Protocol.SevenSignsStatusMsg)args.Payload!));
                return;
            case FanoutKind.SevenSignsSky:
                publisher.FanoutSevenSignsSky(watchers, ((Fineage.Protocol.SevenSignsSkyMsg)args.Payload!));
                return;
            case FanoutKind.DungeonEntered:
                publisher.FanoutDungeonEntered(watchers, ((Fineage.Protocol.DungeonEnteredMsg)args.Payload!));
                return;
            case FanoutKind.CursedWeaponList:
                publisher.FanoutCursedWeaponList(watchers, ((Fineage.Protocol.CursedWeaponListMsg)args.Payload!));
                return;
            case FanoutKind.CursedWeaponLocation:
                publisher.FanoutCursedWeaponLocation(watchers, ((Fineage.Protocol.CursedWeaponLocationMsg)args.Payload!));
                return;
            default:
                throw new InvalidOperationException($"fan-out kind {kind} is not dispatched");
        }
    }
}
