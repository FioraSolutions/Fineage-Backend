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
}
