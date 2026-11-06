using Fineage.Net.Contract;

namespace Fineage.Zone.Net;

/// <summary>
/// ZoneWorld → Net PacketEnvelope fanout for sim ticks (NPC chase, attacks, vitals, die/drop).
/// Player enter/move knownlist diffs stay in <see cref="NetZoneServer"/>.
/// </summary>
interface INetPresenceBridge
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

    void FanoutStatusUpdate(IReadOnlyList<int> watcherObjectIds, StatusUpdateNetMsg status);

    void FanoutDie(IReadOnlyList<int> watcherObjectIds, EntityDieMsg die);

    void FanoutDropItem(IReadOnlyList<int> watcherObjectIds, DropItemNetMsg drop);

    void FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, EntityDespawnMsg despawn);

    void FanoutInventoryUpdate(IReadOnlyList<int> watcherObjectIds, InventoryUpdateNetMsg update);

    void FanoutCreatureSay(IReadOnlyList<int> watcherObjectIds, CreatureSayNetMsg say);

    void FanoutSystemMessage(IReadOnlyList<int> watcherObjectIds, SystemMessageNetMsg message);

    void FanoutMagicSkillUse(IReadOnlyList<int> watcherObjectIds, MagicSkillUseNetMsg cast);

    void FanoutRevive(IReadOnlyList<int> watcherObjectIds, ReviveNetMsg revive);

    void FanoutTeleportToLocation(IReadOnlyList<int> watcherObjectIds, TeleportToLocationNetMsg teleport);

    void FanoutPlayerStats(IReadOnlyList<int> watcherObjectIds, PlayerStatsMsg stats);

    void FanoutAbnormalStatusUpdate(IReadOnlyList<int> watcherObjectIds, AbnormalStatusUpdateNetMsg update);

    void FanoutNpcHtml(IReadOnlyList<int> watcherObjectIds, NpcHtmlNetMsg html);

    void FanoutBuyList(IReadOnlyList<int> watcherObjectIds, BuyListNetMsg list);

    void FanoutSellList(IReadOnlyList<int> watcherObjectIds, SellListNetMsg list);

    void FanoutMultiSellList(IReadOnlyList<int> watcherObjectIds, MultiSellListNetMsg list);

    void FanoutShopBalance(IReadOnlyList<int> watcherObjectIds, ShopBalanceNetMsg balance);

    void FanoutWarehouseDepositList(IReadOnlyList<int> watcherObjectIds, WarehouseDepositListNetMsg list);

    void FanoutWarehouseWithdrawList(IReadOnlyList<int> watcherObjectIds, WarehouseWithdrawListNetMsg list);

    void FanoutQuestList(IReadOnlyList<int> watcherObjectIds, QuestListNetMsg list);

    /// <summary>Full SkillList re-send after a trainer learn (Mobius sends SkillList after RequestAcquireSkill).</summary>
    void FanoutSkillList(IReadOnlyList<int> watcherObjectIds, SkillListNetMsg list);

    /// <summary>Mobius ChangeWaitType (sit/stand) for <paramref name="waitType"/>'s actor. Called under the ZoneWorld gate.</summary>
    void FanoutChangeWaitType(IReadOnlyList<int> watcherObjectIds, ChangeWaitTypeNetMsg waitType);

    /// <summary>Mobius SocialAction (gesture) for an actor. Called under the ZoneWorld gate.</summary>
    void FanoutSocialAction(IReadOnlyList<int> watcherObjectIds, SocialActionNetMsg action);

    /// <summary>
    /// Mobius <c>MyTargetSelected</c>: the owner's own click answer (nameplate colour for the target window).
    /// Called under the ZoneWorld gate.
    /// </summary>
    void FanoutTargetSelected(IReadOnlyList<int> watcherObjectIds, TargetSelectedNetMsg selected);

    /// <summary>
    /// Mobius <c>TargetUnselected</c>: the selection was dropped. Called under the ZoneWorld gate.
    /// </summary>
    void FanoutTargetUnselected(IReadOnlyList<int> watcherObjectIds, TargetUnselectedNetMsg unselected);

    /// <summary>Mobius <c>AcquireSkillList</c>: the trainer's learnable rows. Called under the ZoneWorld gate.</summary>
    void FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, AcquireSkillListNetMsg list);

    /// <summary>Mobius <c>AcquireSkillInfo</c>: the confirmation page for one trainer offer. Called under the ZoneWorld gate.</summary>
    void FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, AcquireSkillInfoNetMsg info);

    void FanoutEnchantResult(IReadOnlyList<int> watcherObjectIds, EnchantResultNetMsg result);

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
