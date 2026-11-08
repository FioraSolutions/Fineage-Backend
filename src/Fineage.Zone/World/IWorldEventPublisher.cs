using Fineage.Net.Contract;

namespace Fineage.Zone.World;

/// <summary>
/// World sim → presence/domain event fanout for sim ticks (NPC chase, attacks, vitals, die/drop).
/// Lives in World so the sim does not take a Net-namespace type (#353 domain events).
/// Player enter/move knownlist diffs stay in <c>NetZoneServer</c>; encoding stays on the Net implementer.
/// </summary>
interface IWorldEventPublisher
{
    void FanoutMoveState(
        IReadOnlyList<int> watcherObjectIds,
        int moverObjectId,
        int originX,
        int originY,
        int originZ,
        int destX,
        int destY,
        int destZ,
        bool running);

    /// <summary>
    /// Knownlist changed for <paramref name="selfObjectId"/> during a sim tick (timed move AOI refresh):
    /// EntitySpawn/DropItem for <paramref name="added"/>, EntityDespawn for <paramref name="removed"/>, both directions for players,
    /// plus MoveState for whichever side is mid-run so the newcomer is seen moving, not standing.
    /// Called under the ZoneWorld gate.
    /// </summary>
    void FanoutKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed);

    /// <summary>Mobius ChangeMoveType (gait + effective speeds) for <paramref name="moverObjectId"/>. Called under the ZoneWorld gate.</summary>
    void FanoutChangeMoveType(IReadOnlyList<int> watcherObjectIds, int moverObjectId, bool running, float runSpeed, float walkSpeed, bool inWater);

    /// <summary>
    /// Mobius EtcStatusUpdate (weight penalty level, grade-penalty flag, force charges) to the owner.
    /// Called under the ZoneWorld gate.
    /// </summary>
    void FanoutEtcStatusUpdate(IReadOnlyList<int> watcherObjectIds, EtcStatusUpdateNetMsg status);

    /// <summary>Mobius AutoAttackStart (<paramref name="inStance"/>) / AutoAttackStop for <paramref name="objectId"/>. Called under the ZoneWorld gate.</summary>
    void FanoutAttackStance(IReadOnlyList<int> watcherObjectIds, int objectId, bool inStance);

    /// <summary>Mobius ExAutoSoulShot: auto use of shot <paramref name="itemId"/> is now on/off. Called under the ZoneWorld gate.</summary>
    void FanoutAutoSoulShot(IReadOnlyList<int> watcherObjectIds, int itemId, bool enabled);

    /// <summary>Mobius UserInfo to the owner (status window sheet + own paperdoll). Called under the ZoneWorld gate.</summary>
    void FanoutUserInfo(IReadOnlyList<int> watcherObjectIds, UserInfoNetMsg info);

    /// <summary>Mobius CharInfo refresh (paperdoll changed) of player <paramref name="objectId"/> to knownlist players. Called under the ZoneWorld gate.</summary>
    void FanoutCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId);

    void FanoutAttackState(IReadOnlyList<int> watcherObjectIds, AttackStateMsg attack);

    /// <summary>Mobius StatusUpdate to watchers. Called under the ZoneWorld gate.</summary>
    void FanoutStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.StatusUpdateMsg status);

    /// <summary>Mobius Die packet for <paramref name="objectId"/> (killer may be 0). Called under the ZoneWorld gate.</summary>
    void FanoutDie(IReadOnlyList<int> watcherObjectIds, int objectId, int killerObjectId);

    /// <summary>Mobius DropItem (ground spawn with dropper FX). Called under the ZoneWorld gate.</summary>
    void FanoutDropItem(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DropItemMsg drop);

    /// <summary>Mobius DeleteObject for <paramref name="objectId"/>. Called under the ZoneWorld gate.</summary>
    void FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, int objectId);

    /// <summary>Mobius InventoryUpdate to watchers. Called under the ZoneWorld gate.</summary>
    void FanoutInventoryUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.InventoryUpdateMsg update);

    /// <summary>Mobius CreatureSay to watchers. Called under the ZoneWorld gate.</summary>
    void FanoutCreatureSay(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CreatureSayMsg say);

    /// <summary>Mobius SystemMessage to watchers. Called under the ZoneWorld gate.</summary>
    void FanoutSystemMessage(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SystemMessageMsg message);

    /// <summary>Mobius MagicSkillUse to watchers. Called under the ZoneWorld gate.</summary>
    void FanoutMagicSkillUse(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MagicSkillUseMsg cast);

    /// <summary>Mobius Revive for <paramref name="objectId"/>. Called under the ZoneWorld gate.</summary>
    void FanoutRevive(IReadOnlyList<int> watcherObjectIds, int objectId);

    /// <summary>Mobius TeleportToLocation for <paramref name="objectId"/>. Called under the ZoneWorld gate.</summary>
    void FanoutTeleportToLocation(IReadOnlyList<int> watcherObjectIds, int objectId, int x, int y, int z, int heading);

    void FanoutPlayerStats(IReadOnlyList<int> watcherObjectIds, PlayerStatsMsg stats);

    /// <summary>Mobius AbnormalStatusUpdate to watchers. Called under the ZoneWorld gate.</summary>
    void FanoutAbnormalStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AbnormalStatusUpdateMsg update);

    /// <summary>Mobius NpcHtmlMessage to watchers. Called under the ZoneWorld gate.</summary>
    void FanoutNpcHtml(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.NpcHtmlMsg html);

    void FanoutBuyList(IReadOnlyList<int> watcherObjectIds, BuyListNetMsg list);

    void FanoutSellList(IReadOnlyList<int> watcherObjectIds, SellListNetMsg list);

    void FanoutMultiSellList(IReadOnlyList<int> watcherObjectIds, MultiSellListNetMsg list);

    /// <summary>Shop window adena/weight/slots snapshot. Called under the ZoneWorld gate.</summary>
    void FanoutShopBalance(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShopBalanceMsg balance);

    void FanoutWarehouseDepositList(IReadOnlyList<int> watcherObjectIds, WarehouseDepositListNetMsg list);

    void FanoutWarehouseWithdrawList(IReadOnlyList<int> watcherObjectIds, WarehouseWithdrawListNetMsg list);

    void FanoutQuestList(IReadOnlyList<int> watcherObjectIds, QuestListNetMsg list);

    /// <summary>Full SkillList re-send after a trainer learn (Mobius sends SkillList after RequestAcquireSkill).</summary>
    void FanoutSkillList(IReadOnlyList<int> watcherObjectIds, SkillListNetMsg list);

    /// <summary>Mobius ChangeWaitType (sit/stand) for <paramref name="objectId"/>. Called under the ZoneWorld gate.</summary>
    void FanoutChangeWaitType(IReadOnlyList<int> watcherObjectIds, int objectId, int waitType, int x, int y, int z);

    /// <summary>Mobius SocialAction (gesture) for <paramref name="objectId"/>. Called under the ZoneWorld gate.</summary>
    void FanoutSocialAction(IReadOnlyList<int> watcherObjectIds, int objectId, int actionId);

    /// <summary>
    /// Mobius <c>MyTargetSelected</c>: the owner's own click answer (nameplate colour for the target window).
    /// Called under the ZoneWorld gate.
    /// </summary>
    void FanoutTargetSelected(IReadOnlyList<int> watcherObjectIds, int targetObjectId, int colorHint);

    /// <summary>
    /// Mobius <c>TargetUnselected</c>: the selection was dropped. Called under the ZoneWorld gate.
    /// </summary>
    void FanoutTargetUnselected(IReadOnlyList<int> watcherObjectIds, int targetObjectId);

    /// <summary>Mobius <c>AcquireSkillList</c>: the trainer's learnable rows. Called under the ZoneWorld gate.</summary>
    void FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, AcquireSkillListNetMsg list);

    /// <summary>Mobius <c>AcquireSkillInfo</c>: the confirmation page for one trainer offer. Called under the ZoneWorld gate.</summary>
    void FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, AcquireSkillInfoNetMsg info);

    /// <summary>Mobius EnchantResult to the enchanter. Called under the ZoneWorld gate.</summary>
    void FanoutEnchantResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantResultMsg result);

    void FanoutPledgeStatus(IReadOnlyList<int> watcherObjectIds, PledgeStatusNetMsg status);

    void FanoutPledgeMemberList(IReadOnlyList<int> watcherObjectIds, PledgeMemberListNetMsg list);

    void FanoutClanAskJoin(IReadOnlyList<int> watcherObjectIds, ClanAskJoinNetMsg ask);

    void FanoutClanJoinResult(IReadOnlyList<int> watcherObjectIds, ClanJoinResultNetMsg result);

    void FanoutPartyAskJoin(IReadOnlyList<int> watcherObjectIds, PartyAskJoinNetMsg ask);

    void FanoutPartyJoinResult(IReadOnlyList<int> watcherObjectIds, PartyJoinResultNetMsg result);

    void FanoutPartyMemberList(IReadOnlyList<int> watcherObjectIds, PartyMemberListNetMsg list);

    void FanoutTradeAsk(IReadOnlyList<int> watcherObjectIds, TradeAskNetMsg ask);

    void FanoutTradeRequestResult(IReadOnlyList<int> watcherObjectIds, TradeRequestResultNetMsg result);

    void FanoutTradeStart(IReadOnlyList<int> watcherObjectIds, TradeStartNetMsg start);

    void FanoutTradeOfferUpdate(IReadOnlyList<int> watcherObjectIds, TradeOfferUpdateNetMsg update);

    void FanoutTradeConfirmState(IReadOnlyList<int> watcherObjectIds, TradeConfirmStateNetMsg state);

    void FanoutTradeFinished(IReadOnlyList<int> watcherObjectIds, TradeFinishedNetMsg finished);

    /// <summary>Private store type + title for a pawn (self + knownlist).</summary>
    void FanoutPrivateStoreInfo(IReadOnlyList<int> watcherObjectIds, PrivateStoreInfoNetMsg info);

    /// <summary>Sell list window for a buyer who clicked a store pawn.</summary>
    void FanoutPrivateStoreListSell(IReadOnlyList<int> watcherObjectIds, PrivateStoreListSellNetMsg list);

    /// <summary>Buy list window for a visitor who clicked a buy-store pawn.</summary>
    void FanoutPrivateStoreListBuy(IReadOnlyList<int> watcherObjectIds, PrivateStoreListBuyNetMsg list);

    /// <summary>Known recipe ids for one craft book tab.</summary>
    void FanoutRecipeBookList(IReadOnlyList<int> watcherObjectIds, RecipeBookListNetMsg list);

    /// <summary>Self-craft result after RequestRecipeItemMakeSelf.</summary>
    void FanoutRecipeItemMakeInfo(IReadOnlyList<int> watcherObjectIds, RecipeItemMakeInfoNetMsg info);

    /// <summary>Manufacture recipe list for a visitor who clicked a workshop pawn.</summary>
    void FanoutRecipeShopSellList(IReadOnlyList<int> watcherObjectIds, RecipeShopSellListNetMsg list);

    /// <summary>Manufacturer MP + recipe after make-info / make-item.</summary>
    void FanoutRecipeShopItemInfo(IReadOnlyList<int> watcherObjectIds, RecipeShopItemInfoNetMsg info);

    void FanoutFriendInviteAsk(IReadOnlyList<int> watcherObjectIds, FriendInviteAskNetMsg ask);

    void FanoutFriendList(IReadOnlyList<int> watcherObjectIds, FriendListNetMsg list);

    void FanoutFriendUpdate(IReadOnlyList<int> watcherObjectIds, FriendUpdateNetMsg update);

    void FanoutBlockList(IReadOnlyList<int> watcherObjectIds, BlockListNetMsg list);

    /// <summary>Boss schedule rows, the reply to a <c>BossStatusRequest</c>.</summary>
    void FanoutBossStatus(IReadOnlyList<int> watcherObjectIds, BossStatusNetMsg status);

    /// <summary>One castle's siege state, the reply to a <c>SiegeListRequest</c> and the echo after a change.</summary>
    void FanoutSiegeInfo(IReadOnlyList<int> watcherObjectIds, SiegeInfoNetMsg info);

    /// <summary>Olympiad period, standing, queue and match slot.</summary>
    void FanoutOlympiadStatus(IReadOnlyList<int> watcherObjectIds, OlympiadStatusNetMsg status);

    /// <summary>The match both participants were placed in.</summary>
    void FanoutOlympiadMatch(IReadOnlyList<int> watcherObjectIds, OlympiadMatchNetMsg match);

    /// <summary>One countdown step for both participants.</summary>
    void FanoutOlympiadCountdown(IReadOnlyList<int> watcherObjectIds, OlympiadCountdownNetMsg countdown);

    /// <summary>The finished match and its reward.</summary>
    void FanoutOlympiadResult(IReadOnlyList<int> watcherObjectIds, OlympiadResultNetMsg result);

    /// <summary>The Seven Signs record, also the reply to a status request.</summary>
    void FanoutSevenSignsStatus(IReadOnlyList<int> watcherObjectIds, SevenSignsStatusNetMsg status);

    /// <summary>The sky tint the record implies, so the client can recolour without polling.</summary>
    void FanoutSevenSignsSky(IReadOnlyList<int> watcherObjectIds, SevenSignsSkyNetMsg sky);

    /// <summary>The catacomb/necropolis result, also the validation kick.</summary>
    void FanoutDungeonEntered(IReadOnlyList<int> watcherObjectIds, DungeonEnteredNetMsg entered);
}
