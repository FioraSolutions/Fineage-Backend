using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// <see cref="ZoneWorld.NetBridge"/> as seen by the simulation: inside a recorded step every call becomes an entry of
/// the step's <see cref="WorldFanoutBatch"/>; outside one it is sent at once through the
/// <see cref="WorldFanoutSequencer"/>. Watcher lists are copied when recorded; messages are built per call and are
/// not mutated after emission. Calls whose encoding reads world state are prepared before they are queued.
/// </summary>
internal sealed class OutboxingWorldEventPublisher(IWorldEventPublisher inner, WorldFanoutSequencer sequencer) : IWorldEventPublisher
{
    void Emit(string name, Action send)
    {
        var batch = sequencer.Recording;
        if (batch != null)
            batch.AddOrdered(name, send);
        else
            sequencer.SendNow(send);
    }

    void EmitReplaceable<T>(
        PresenceCoalesceKind kind, int subjectId, int[] audience, T payload, Func<T, T, T> merge, Func<T, Action> bind)
        where T : class
    {
        var batch = sequencer.Recording;
        if (batch != null)
            batch.AddReplaceable(kind, subjectId, audience, payload, merge, bind);
        else
            sequencer.SendNow(bind(payload));
    }

    static T Latest<T>(T superseded, T latest) => latest;

    /// <summary>StatusUpdate carries a subset of attributes: the merged frame keeps earlier attributes the new one omits.</summary>
    internal static StatusUpdateMsg MergeStatus(StatusUpdateMsg superseded, StatusUpdateMsg latest)
    {
        var ids = new List<int>(superseded.AttributeIds.Length + latest.AttributeIds.Length);
        var values = new List<int>(ids.Capacity);
        for (int i = 0; i < superseded.AttributeIds.Length; i++)
        {
            if (Array.IndexOf(latest.AttributeIds, superseded.AttributeIds[i]) >= 0)
                continue;
            ids.Add(superseded.AttributeIds[i]);
            values.Add(superseded.Values[i]);
        }

        ids.AddRange(latest.AttributeIds);
        values.AddRange(latest.Values);
        return new StatusUpdateMsg { ObjectId = latest.ObjectId, AttributeIds = [.. ids], Values = [.. values] };
    }

    sealed record MoveStateArgs(int OriginX, int OriginY, int OriginZ, int DestX, int DestY, int DestZ, bool Running);

    sealed record MoveTypeArgs(bool Running, float RunSpeed, float WalkSpeed, bool InWater);

    void IWorldEventPublisher.FanoutMoveState(IReadOnlyList<int> watcherObjectIds, int moverObjectId, int originX, int originY, int originZ, int destX, int destY, int destZ, bool running)
    {
        int[] watchers = [.. watcherObjectIds];
        EmitReplaceable(PresenceCoalesceKind.MoveState, moverObjectId, watchers,
            new MoveStateArgs(originX, originY, originZ, destX, destY, destZ, running), Latest,
            m => () => inner.FanoutMoveState(watchers, moverObjectId, m.OriginX, m.OriginY, m.OriginZ, m.DestX, m.DestY, m.DestZ, m.Running));
    }

    void IWorldEventPublisher.FanoutChangeMoveType(IReadOnlyList<int> watcherObjectIds, int moverObjectId, bool running, float runSpeed, float walkSpeed, bool inWater)
    {
        int[] watchers = [.. watcherObjectIds];
        EmitReplaceable(PresenceCoalesceKind.ChangeMoveType, moverObjectId, watchers,
            new MoveTypeArgs(running, runSpeed, walkSpeed, inWater), Latest,
            m => () => inner.FanoutChangeMoveType(watchers, moverObjectId, m.Running, m.RunSpeed, m.WalkSpeed, m.InWater));
    }

    void IWorldEventPublisher.FanoutStatusUpdate(IReadOnlyList<int> watcherObjectIds, StatusUpdateMsg status)
    {
        int[] watchers = [.. watcherObjectIds];
        EmitReplaceable(PresenceCoalesceKind.StatusUpdate, status.ObjectId, watchers, status, MergeStatus,
            s => () => inner.FanoutStatusUpdate(watchers, s));
    }

    void IWorldEventPublisher.FanoutAbnormalStatusUpdate(IReadOnlyList<int> watcherObjectIds, AbnormalStatusUpdateMsg update)
    {
        int[] watchers = [.. watcherObjectIds];
        EmitReplaceable(PresenceCoalesceKind.AbnormalStatusUpdate, update.ObjectId, watchers, update, Latest,
            u => () => inner.FanoutAbnormalStatusUpdate(watchers, u));
    }

    void IWorldEventPublisher.FanoutKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed) =>
        Emit(nameof(IWorldEventPublisher.FanoutKnownlistDiff), inner.PrepareKnownlistDiff(selfObjectId, [.. added], [.. removed]));

    void IWorldEventPublisher.FanoutCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId) =>
        Emit(nameof(IWorldEventPublisher.FanoutCharInfo), inner.PrepareCharInfo([.. watcherObjectIds], objectId));

    Action IWorldEventPublisher.PrepareKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed) =>
        inner.PrepareKnownlistDiff(selfObjectId, added, removed);

    Action IWorldEventPublisher.PrepareCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId) =>
        inner.PrepareCharInfo(watcherObjectIds, objectId);

    void IWorldEventPublisher.FanoutPositionCorrection(IReadOnlyList<int> watcherObjectIds, int objectId, int x, int y, int z, int heading)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPositionCorrection), () => inner.FanoutPositionCorrection(watcherObjectIdsCopy, objectId, x, y, z, heading));
    }

    void IWorldEventPublisher.FanoutEtcStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EtcStatusUpdateMsg status)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutEtcStatusUpdate), () => inner.FanoutEtcStatusUpdate(watcherObjectIdsCopy, status));
    }

    void IWorldEventPublisher.FanoutAttackStance(IReadOnlyList<int> watcherObjectIds, int objectId, bool inStance)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutAttackStance), () => inner.FanoutAttackStance(watcherObjectIdsCopy, objectId, inStance));
    }

    void IWorldEventPublisher.FanoutAutoSoulShot(IReadOnlyList<int> watcherObjectIds, int itemId, bool enabled)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutAutoSoulShot), () => inner.FanoutAutoSoulShot(watcherObjectIdsCopy, itemId, enabled));
    }

    void IWorldEventPublisher.FanoutUserInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.UserInfoMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutUserInfo), () => inner.FanoutUserInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutAttackState(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AttackMsg attack)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutAttackState), () => inner.FanoutAttackState(watcherObjectIdsCopy, attack));
    }

    void IWorldEventPublisher.FanoutDie(IReadOnlyList<int> watcherObjectIds, int objectId, int killerObjectId, int restartFlags)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutDie), () => inner.FanoutDie(watcherObjectIdsCopy, objectId, killerObjectId, restartFlags));
    }

    void IWorldEventPublisher.FanoutDropItem(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DropItemMsg drop)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutDropItem), () => inner.FanoutDropItem(watcherObjectIdsCopy, drop));
    }

    void IWorldEventPublisher.FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutEntityDespawn), () => inner.FanoutEntityDespawn(watcherObjectIdsCopy, objectId));
    }

    void IWorldEventPublisher.FanoutInventoryUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.InventoryUpdateMsg update)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutInventoryUpdate), () => inner.FanoutInventoryUpdate(watcherObjectIdsCopy, update));
    }

    void IWorldEventPublisher.FanoutCreatureSay(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CreatureSayMsg say)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutCreatureSay), () => inner.FanoutCreatureSay(watcherObjectIdsCopy, say));
    }

    void IWorldEventPublisher.FanoutSystemMessage(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SystemMessageMsg message)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutSystemMessage), () => inner.FanoutSystemMessage(watcherObjectIdsCopy, message));
    }

    void IWorldEventPublisher.FanoutMagicSkillUse(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MagicSkillUseMsg cast)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutMagicSkillUse), () => inner.FanoutMagicSkillUse(watcherObjectIdsCopy, cast));
    }

    void IWorldEventPublisher.FanoutRevive(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutRevive), () => inner.FanoutRevive(watcherObjectIdsCopy, objectId));
    }

    void IWorldEventPublisher.FanoutTeleportToLocation(IReadOnlyList<int> watcherObjectIds, int objectId, int x, int y, int z, int heading)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutTeleportToLocation), () => inner.FanoutTeleportToLocation(watcherObjectIdsCopy, objectId, x, y, z, heading));
    }

    void IWorldEventPublisher.FanoutPlayerStats(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PlayerStatsMsg stats)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPlayerStats), () => inner.FanoutPlayerStats(watcherObjectIdsCopy, stats));
    }

    void IWorldEventPublisher.FanoutNpcHtml(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.NpcHtmlMsg html)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutNpcHtml), () => inner.FanoutNpcHtml(watcherObjectIdsCopy, html));
    }

    void IWorldEventPublisher.FanoutBuyList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BuyListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutBuyList), () => inner.FanoutBuyList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SellListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutSellList), () => inner.FanoutSellList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutMultiSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MultiSellListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutMultiSellList), () => inner.FanoutMultiSellList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutShopBalance(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShopBalanceMsg balance)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutShopBalance), () => inner.FanoutShopBalance(watcherObjectIdsCopy, balance));
    }

    void IWorldEventPublisher.FanoutWarehouseDepositList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WarehouseDepositListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutWarehouseDepositList), () => inner.FanoutWarehouseDepositList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutWarehouseWithdrawList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WarehouseWithdrawListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutWarehouseWithdrawList), () => inner.FanoutWarehouseWithdrawList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutPackageToList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PackageToListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPackageToList), () => inner.FanoutPackageToList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutPackageSendableList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PackageSendableListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPackageSendableList), () => inner.FanoutPackageSendableList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutQuestList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.QuestListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutQuestList), () => inner.FanoutQuestList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutQuestDirection(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.QuestDirectionNetMsg msg)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutQuestDirection), () => inner.FanoutQuestDirection(watcherObjectIdsCopy, msg));
    }

    void IWorldEventPublisher.FanoutQuestNpcMarks(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.QuestNpcMarksNetMsg msg)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutQuestNpcMarks), () => inner.FanoutQuestNpcMarks(watcherObjectIdsCopy, msg));
    }

    void IWorldEventPublisher.FanoutTutorialUi(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.TutorialUiNetMsg msg)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutTutorialUi), () => inner.FanoutTutorialUi(watcherObjectIdsCopy, msg));
    }

    void IWorldEventPublisher.FanoutSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SkillListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutSkillList), () => inner.FanoutSkillList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutShortcutInit(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShortCutInitMsg init)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutShortcutInit), () => inner.FanoutShortcutInit(watcherObjectIdsCopy, init));
    }

    void IWorldEventPublisher.FanoutChangeWaitType(IReadOnlyList<int> watcherObjectIds, int objectId, int waitType, int x, int y, int z)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutChangeWaitType), () => inner.FanoutChangeWaitType(watcherObjectIdsCopy, objectId, waitType, x, y, z));
    }

    void IWorldEventPublisher.FanoutSocialAction(IReadOnlyList<int> watcherObjectIds, int objectId, int actionId)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutSocialAction), () => inner.FanoutSocialAction(watcherObjectIdsCopy, objectId, actionId));
    }

    void IWorldEventPublisher.FanoutTargetSelected(IReadOnlyList<int> watcherObjectIds, int targetObjectId, int colorHint)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutTargetSelected), () => inner.FanoutTargetSelected(watcherObjectIdsCopy, targetObjectId, colorHint));
    }

    void IWorldEventPublisher.FanoutTargetUnselected(IReadOnlyList<int> watcherObjectIds, int targetObjectId)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutTargetUnselected), () => inner.FanoutTargetUnselected(watcherObjectIdsCopy, targetObjectId));
    }

    void IWorldEventPublisher.FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutAcquireSkillList), () => inner.FanoutAcquireSkillList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillInfoMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutAcquireSkillInfo), () => inner.FanoutAcquireSkillInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutEnchantSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantSkillListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutEnchantSkillList), () => inner.FanoutEnchantSkillList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutEnchantSkillInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantSkillInfoMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutEnchantSkillInfo), () => inner.FanoutEnchantSkillInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutEnchantResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantResultMsg result)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutEnchantResult), () => inner.FanoutEnchantResult(watcherObjectIdsCopy, result));
    }

    void IWorldEventPublisher.FanoutAugmentShowMakeWindow(IReadOnlyList<int> watcherObjectIds) =>
        Emit(nameof(IWorldEventPublisher.FanoutAugmentShowMakeWindow), () => inner.FanoutAugmentShowMakeWindow([.. watcherObjectIds]));

    void IWorldEventPublisher.FanoutAugmentShowCancelWindow(IReadOnlyList<int> watcherObjectIds) =>
        Emit(nameof(IWorldEventPublisher.FanoutAugmentShowCancelWindow), () => inner.FanoutAugmentShowCancelWindow([.. watcherObjectIds]));

    void IWorldEventPublisher.FanoutAugmentPutItemResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationPutItemMsg msg) =>
        Emit(nameof(IWorldEventPublisher.FanoutAugmentPutItemResult), () => inner.FanoutAugmentPutItemResult([.. watcherObjectIds], msg));

    void IWorldEventPublisher.FanoutAugmentPutRefinerResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationPutRefinerMsg msg) =>
        Emit(nameof(IWorldEventPublisher.FanoutAugmentPutRefinerResult), () => inner.FanoutAugmentPutRefinerResult([.. watcherObjectIds], msg));

    void IWorldEventPublisher.FanoutAugmentPutGemResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationPutGemMsg msg) =>
        Emit(nameof(IWorldEventPublisher.FanoutAugmentPutGemResult), () => inner.FanoutAugmentPutGemResult([.. watcherObjectIds], msg));

    void IWorldEventPublisher.FanoutAugmentVariationResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationResultMsg msg) =>
        Emit(nameof(IWorldEventPublisher.FanoutAugmentVariationResult), () => inner.FanoutAugmentVariationResult([.. watcherObjectIds], msg));

    void IWorldEventPublisher.FanoutAugmentPutCancelItemResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationCancelItemMsg msg) =>
        Emit(nameof(IWorldEventPublisher.FanoutAugmentPutCancelItemResult), () => inner.FanoutAugmentPutCancelItemResult([.. watcherObjectIds], msg));

    void IWorldEventPublisher.FanoutAugmentCancelResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationCancelResultMsg msg) =>
        Emit(nameof(IWorldEventPublisher.FanoutAugmentCancelResult), () => inner.FanoutAugmentCancelResult([.. watcherObjectIds], msg));

    void IWorldEventPublisher.FanoutPledgeStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeStatusChangedMsg status)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPledgeStatus), () => inner.FanoutPledgeStatus(watcherObjectIdsCopy, status));
    }

    void IWorldEventPublisher.FanoutPledgeMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeMemberListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPledgeMemberList), () => inner.FanoutPledgeMemberList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutCrestData(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.CrestDataNetMsg crest)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutCrestData), () => inner.FanoutCrestData(watcherObjectIdsCopy, crest));
    }

    void IWorldEventPublisher.FanoutPledgeWarList(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PledgeWarListNetMsg wars)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPledgeWarList), () => inner.FanoutPledgeWarList(watcherObjectIdsCopy, wars));
    }

    void IWorldEventPublisher.FanoutRelationChanged(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.RelationChangedNetMsg relation)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutRelationChanged), () => inner.FanoutRelationChanged(watcherObjectIdsCopy, relation));
    }

    void IWorldEventPublisher.FanoutPledgeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PledgeInfoNetMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPledgeInfo), () => inner.FanoutPledgeInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutAllyInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.AllyInfoNetMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutAllyInfo), () => inner.FanoutAllyInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutAllyAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.AllyAskJoinNetMsg ask)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutAllyAskJoin), () => inner.FanoutAllyAskJoin(watcherObjectIdsCopy, ask));
    }

    void IWorldEventPublisher.FanoutClanHallList(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.ClanHallListNetMsg halls)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutClanHallList), () => inner.FanoutClanHallList(watcherObjectIdsCopy, halls));
    }

    void IWorldEventPublisher.FanoutAgitDecoInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.AgitDecoInfoNetMsg deco)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutAgitDecoInfo), () => inner.FanoutAgitDecoInfo(watcherObjectIdsCopy, deco));
    }

    void IWorldEventPublisher.FanoutDoorStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.DoorStatusNetMsg door)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutDoorStatus), () => inner.FanoutDoorStatus(watcherObjectIdsCopy, door));
    }

    void IWorldEventPublisher.FanoutCastleInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.CastleInfoNetMsg castle)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutCastleInfo), () => inner.FanoutCastleInfo(watcherObjectIdsCopy, castle));
    }

    void IWorldEventPublisher.FanoutManorInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.ManorInfoNetMsg manor)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutManorInfo), () => inner.FanoutManorInfo(watcherObjectIdsCopy, manor));
    }

    void IWorldEventPublisher.FanoutFishing(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.FishingEventNetMsg fishing)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutFishing), () => inner.FanoutFishing(watcherObjectIdsCopy, fishing));
    }

    void IWorldEventPublisher.FanoutSummonInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.SummonInfoNetMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutSummonInfo), () => inner.FanoutSummonInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutSummonGone(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.SummonGoneNetMsg gone)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutSummonGone), () => inner.FanoutSummonGone(watcherObjectIdsCopy, gone));
    }

    void IWorldEventPublisher.FanoutRide(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.RideNetMsg ride)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutRide), () => inner.FanoutRide(watcherObjectIdsCopy, ride));
    }

    void IWorldEventPublisher.FanoutPlayerActionState(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PlayerActionStateNetMsg state)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPlayerActionState), () => inner.FanoutPlayerActionState(watcherObjectIdsCopy, state));
    }

    void IWorldEventPublisher.FanoutPetInventory(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PetInventoryNetMsg inventory)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPetInventory), () => inner.FanoutPetInventory(watcherObjectIdsCopy, inventory));
    }

    void IWorldEventPublisher.FanoutDuelEvent(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.DuelEventNetMsg duel)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutDuelEvent), () => inner.FanoutDuelEvent(watcherObjectIdsCopy, duel));
    }

    void IWorldEventPublisher.FanoutDuelUserInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.DuelUserInfoNetMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutDuelUserInfo), () => inner.FanoutDuelUserInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutClanAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanAskJoinMsg ask)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutClanAskJoin), () => inner.FanoutClanAskJoin(watcherObjectIdsCopy, ask));
    }

    void IWorldEventPublisher.FanoutClanJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanJoinResultMsg result)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutClanJoinResult), () => inner.FanoutClanJoinResult(watcherObjectIdsCopy, result));
    }

    void IWorldEventPublisher.FanoutPartyAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyAskJoinMsg ask)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPartyAskJoin), () => inner.FanoutPartyAskJoin(watcherObjectIdsCopy, ask));
    }

    void IWorldEventPublisher.FanoutPartyJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyJoinResultMsg result)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPartyJoinResult), () => inner.FanoutPartyJoinResult(watcherObjectIdsCopy, result));
    }

    void IWorldEventPublisher.FanoutPartyMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPartyMemberList), () => inner.FanoutPartyMemberList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutPartyMemberUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberUpdateMsg update)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPartyMemberUpdate), () => inner.FanoutPartyMemberUpdate(watcherObjectIdsCopy, update));
    }

    void IWorldEventPublisher.FanoutPartySpelled(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AbnormalStatusUpdateMsg effects)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPartySpelled), () => inner.FanoutPartySpelled(watcherObjectIdsCopy, effects));
    }

    void IWorldEventPublisher.FanoutPartyMemberPosition(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberPositionMsg positions)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPartyMemberPosition), () => inner.FanoutPartyMemberPosition(watcherObjectIdsCopy, positions));
    }

    void IWorldEventPublisher.FanoutMagicSkillLaunched(IReadOnlyList<int> watcherObjectIds, int casterObjectId, int skillId, int skillLevel, int[] targetObjectIds)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        int[] targetObjectIdsCopy = [.. targetObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutMagicSkillLaunched), () => inner.FanoutMagicSkillLaunched(watcherObjectIdsCopy, casterObjectId, skillId, skillLevel, targetObjectIdsCopy));
    }

    void IWorldEventPublisher.FanoutMagicSkillCanceled(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutMagicSkillCanceled), () => inner.FanoutMagicSkillCanceled(watcherObjectIdsCopy, objectId));
    }

    void IWorldEventPublisher.FanoutPlaySound(IReadOnlyList<int> watcherObjectIds, int type, string file, int objectId, int x, int y, int z, int delayMs)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPlaySound), () => inner.FanoutPlaySound(watcherObjectIdsCopy, type, file, objectId, x, y, z, delayMs));
    }

    void IWorldEventPublisher.FanoutMonRaceInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.MonRaceInfoNetMsg packet)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutMonRaceInfo), () => inner.FanoutMonRaceInfo(watcherObjectIdsCopy, packet));
    }

    void IWorldEventPublisher.FanoutVehicleInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.VehicleInfoNetMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutVehicleInfo), () => inner.FanoutVehicleInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutVehicleDeparture(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.VehicleDepartureNetMsg departure)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutVehicleDeparture), () => inner.FanoutVehicleDeparture(watcherObjectIdsCopy, departure));
    }

    void IWorldEventPublisher.FanoutTradeAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeAskMsg ask)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutTradeAsk), () => inner.FanoutTradeAsk(watcherObjectIdsCopy, ask));
    }

    void IWorldEventPublisher.FanoutTradeRequestResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeRequestResultMsg result)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutTradeRequestResult), () => inner.FanoutTradeRequestResult(watcherObjectIdsCopy, result));
    }

    void IWorldEventPublisher.FanoutTradeStart(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeStartMsg start)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutTradeStart), () => inner.FanoutTradeStart(watcherObjectIdsCopy, start));
    }

    void IWorldEventPublisher.FanoutTradeOfferUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeOfferUpdateMsg update)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutTradeOfferUpdate), () => inner.FanoutTradeOfferUpdate(watcherObjectIdsCopy, update));
    }

    void IWorldEventPublisher.FanoutTradeConfirmState(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeConfirmStateMsg state)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutTradeConfirmState), () => inner.FanoutTradeConfirmState(watcherObjectIdsCopy, state));
    }

    void IWorldEventPublisher.FanoutTradeFinished(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeFinishedMsg finished)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutTradeFinished), () => inner.FanoutTradeFinished(watcherObjectIdsCopy, finished));
    }

    void IWorldEventPublisher.FanoutPrivateStoreInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreInfoMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPrivateStoreInfo), () => inner.FanoutPrivateStoreInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutPrivateStoreListSell(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreListSellMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPrivateStoreListSell), () => inner.FanoutPrivateStoreListSell(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutPrivateStoreListBuy(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreListBuyMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutPrivateStoreListBuy), () => inner.FanoutPrivateStoreListBuy(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutRecipeBookList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeBookListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutRecipeBookList), () => inner.FanoutRecipeBookList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutRecipeItemMakeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeItemMakeInfoMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutRecipeItemMakeInfo), () => inner.FanoutRecipeItemMakeInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutRecipeShopSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeShopSellListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutRecipeShopSellList), () => inner.FanoutRecipeShopSellList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutRecipeShopItemInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeShopItemInfoMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutRecipeShopItemInfo), () => inner.FanoutRecipeShopItemInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutFriendInviteAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendInviteAskMsg ask)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutFriendInviteAsk), () => inner.FanoutFriendInviteAsk(watcherObjectIdsCopy, ask));
    }

    void IWorldEventPublisher.FanoutWeddingEngageAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WeddingEngageAskMsg ask)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutWeddingEngageAsk), () => inner.FanoutWeddingEngageAsk(watcherObjectIdsCopy, ask));
    }

    void IWorldEventPublisher.FanoutFriendList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutFriendList), () => inner.FanoutFriendList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutFriendUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendUpdateMsg update)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutFriendUpdate), () => inner.FanoutFriendUpdate(watcherObjectIdsCopy, update));
    }

    void IWorldEventPublisher.FanoutBlockList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BlockListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutBlockList), () => inner.FanoutBlockList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutBossStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BossStatusMsg status)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutBossStatus), () => inner.FanoutBossStatus(watcherObjectIdsCopy, status));
    }

    void IWorldEventPublisher.FanoutSiegeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SiegeInfoMsg info)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutSiegeInfo), () => inner.FanoutSiegeInfo(watcherObjectIdsCopy, info));
    }

    void IWorldEventPublisher.FanoutOlympiadStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadStatusMsg status)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutOlympiadStatus), () => inner.FanoutOlympiadStatus(watcherObjectIdsCopy, status));
    }

    void IWorldEventPublisher.FanoutOlympiadMatch(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadMatchMsg match)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutOlympiadMatch), () => inner.FanoutOlympiadMatch(watcherObjectIdsCopy, match));
    }

    void IWorldEventPublisher.FanoutOlympiadCountdown(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadCountdownMsg countdown)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutOlympiadCountdown), () => inner.FanoutOlympiadCountdown(watcherObjectIdsCopy, countdown));
    }

    void IWorldEventPublisher.FanoutOlympiadResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadResultMsg result)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutOlympiadResult), () => inner.FanoutOlympiadResult(watcherObjectIdsCopy, result));
    }

    void IWorldEventPublisher.FanoutSevenSignsStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SevenSignsStatusMsg status)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutSevenSignsStatus), () => inner.FanoutSevenSignsStatus(watcherObjectIdsCopy, status));
    }

    void IWorldEventPublisher.FanoutSevenSignsSky(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SevenSignsSkyMsg sky)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutSevenSignsSky), () => inner.FanoutSevenSignsSky(watcherObjectIdsCopy, sky));
    }

    void IWorldEventPublisher.FanoutDungeonEntered(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DungeonEnteredMsg entered)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutDungeonEntered), () => inner.FanoutDungeonEntered(watcherObjectIdsCopy, entered));
    }

    void IWorldEventPublisher.FanoutCursedWeaponList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CursedWeaponListMsg list)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutCursedWeaponList), () => inner.FanoutCursedWeaponList(watcherObjectIdsCopy, list));
    }

    void IWorldEventPublisher.FanoutCursedWeaponLocation(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CursedWeaponLocationMsg location)
    {
        int[] watcherObjectIdsCopy = [.. watcherObjectIds];
        Emit(nameof(IWorldEventPublisher.FanoutCursedWeaponLocation), () => inner.FanoutCursedWeaponLocation(watcherObjectIdsCopy, location));
    }
}
