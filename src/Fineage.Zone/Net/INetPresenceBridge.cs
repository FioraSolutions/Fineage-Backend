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

    /// <summary>Mobius <c>AcquireSkillList</c>: the trainer's learnable rows. Called under the ZoneWorld gate.</summary>
    void FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, AcquireSkillListNetMsg list);

    /// <summary>Mobius <c>AcquireSkillInfo</c>: the confirmation page for one trainer offer. Called under the ZoneWorld gate.</summary>
    void FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, AcquireSkillInfoNetMsg info);

    void FanoutEnchantResult(IReadOnlyList<int> watcherObjectIds, EnchantResultNetMsg result);

    void FanoutPartyAskJoin(IReadOnlyList<int> watcherObjectIds, PartyAskJoinNetMsg ask);

    void FanoutPartyJoinResult(IReadOnlyList<int> watcherObjectIds, PartyJoinResultNetMsg result);

    void FanoutPartyMemberList(IReadOnlyList<int> watcherObjectIds, PartyMemberListNetMsg list);

    void FanoutTradeAsk(IReadOnlyList<int> watcherObjectIds, TradeAskNetMsg ask);

    void FanoutTradeRequestResult(IReadOnlyList<int> watcherObjectIds, TradeRequestResultNetMsg result);

    void FanoutTradeStart(IReadOnlyList<int> watcherObjectIds, TradeStartNetMsg start);

    void FanoutTradeOfferUpdate(IReadOnlyList<int> watcherObjectIds, TradeOfferUpdateNetMsg update);

    void FanoutTradeConfirmState(IReadOnlyList<int> watcherObjectIds, TradeConfirmStateNetMsg state);

    void FanoutTradeFinished(IReadOnlyList<int> watcherObjectIds, TradeFinishedNetMsg finished);
}
