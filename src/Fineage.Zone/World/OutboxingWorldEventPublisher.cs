using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// <see cref="ZoneWorld.NetBridge"/> as seen by the simulation: inside a recorded step every call becomes a
/// struct descriptor on the step's <see cref="WorldFanoutBatch"/>; outside one it is submitted at once.
/// Watcher lists are copied into an audience snapshot unless the caller already holds an id for this knownlist.
/// Messages are the caller's objects and are not mutated after emission. Calls whose encoding reads world state
/// are prepared before they are queued.
/// </summary>
internal sealed class OutboxingWorldEventPublisher(IWorldEventPublisher inner, WorldFanoutSequencer sequencer) : IWorldEventPublisher
{
    void Record(FanoutKind kind, int subject, IReadOnlyList<int> watchers, FanoutArgs args, bool replaceable)
    {
        var recording = sequencer.Recording;
        var batch = recording ?? new WorldFanoutBatch();
        batch.Bind(inner);
        var audience = AudienceSnapshots.Capture(watchers, sort: replaceable);
        if (replaceable)
            batch.AddReplaceable(kind, subject, audience, args);
        else
            batch.AddOrdered(kind, audience, args);
        if (recording == null)
            sequencer.Submit(batch);
    }

    Action RecordPrepared(Action send, IReadOnlyList<int> audienceIds)
    {
        var recording = sequencer.Recording;
        var batch = recording ?? new WorldFanoutBatch();
        batch.Bind(inner);
        batch.AddPrepared(send, audienceIds);
        if (recording == null)
            sequencer.Submit(batch);
        return send;
    }

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

    void IWorldEventPublisher.FanoutMoveState(IReadOnlyList<int> watcherObjectIds, int moverObjectId, int originX, int originY, int originZ, int destX, int destY, int destZ, bool running) =>
        Record(FanoutKind.MoveState, moverObjectId, watcherObjectIds, new FanoutArgs(A: moverObjectId, B: originX, C: originY, D: originZ, E: destX, F: destY, G: destZ, Flag: running), replaceable: true);

    void IWorldEventPublisher.FanoutKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed)
    {
        // Knownlist drain reads live world to encode spawns; it runs on the simulation thread, not the fan-out worker.
        inner.PrepareKnownlistDiff(selfObjectId, added, removed)();
    }

    Action IWorldEventPublisher.PrepareKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed) =>
        inner.PrepareKnownlistDiff(selfObjectId, added, removed);

    Action IWorldEventPublisher.PrepareCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId) =>
        inner.PrepareCharInfo(watcherObjectIds, objectId);

    void IWorldEventPublisher.FanoutChangeMoveType(IReadOnlyList<int> watcherObjectIds, int moverObjectId, bool running, float runSpeed, float walkSpeed, bool inWater) =>
        Record(FanoutKind.ChangeMoveType, moverObjectId, watcherObjectIds, new FanoutArgs(A: moverObjectId, Flag: running, Fa: runSpeed, Fb: walkSpeed, Flag2: inWater), replaceable: true);

    void IWorldEventPublisher.FanoutPositionCorrection(IReadOnlyList<int> watcherObjectIds, int objectId, int x, int y, int z, int heading) =>
        Record(FanoutKind.PositionCorrection, 0, watcherObjectIds, new FanoutArgs(A: objectId, B: x, C: y, D: z, E: heading), replaceable: false);

    void IWorldEventPublisher.FanoutEtcStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EtcStatusUpdateMsg status) =>
        Record(FanoutKind.EtcStatusUpdate, 0, watcherObjectIds, new FanoutArgs(Payload: status), replaceable: false);

    void IWorldEventPublisher.FanoutAttackStance(IReadOnlyList<int> watcherObjectIds, int objectId, bool inStance) =>
        Record(FanoutKind.AttackStance, 0, watcherObjectIds, new FanoutArgs(A: objectId, Flag: inStance), replaceable: false);

    void IWorldEventPublisher.FanoutAutoSoulShot(IReadOnlyList<int> watcherObjectIds, int itemId, bool enabled) =>
        Record(FanoutKind.AutoSoulShot, 0, watcherObjectIds, new FanoutArgs(A: itemId, Flag: enabled), replaceable: false);

    void IWorldEventPublisher.FanoutUserInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.UserInfoMsg info) =>
        Record(FanoutKind.UserInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId) =>
        RecordPrepared(inner.PrepareCharInfo(watcherObjectIds, objectId), watcherObjectIds);

    void IWorldEventPublisher.FanoutAttackState(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AttackMsg attack) =>
        Record(FanoutKind.AttackState, 0, watcherObjectIds, new FanoutArgs(Payload: attack), replaceable: false);

    void IWorldEventPublisher.FanoutStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.StatusUpdateMsg status) =>
        Record(FanoutKind.StatusUpdate, status.ObjectId, watcherObjectIds, new FanoutArgs(Payload: status), replaceable: true);

    void IWorldEventPublisher.FanoutDie(IReadOnlyList<int> watcherObjectIds, int objectId, int killerObjectId, int restartFlags) =>
        Record(FanoutKind.Die, 0, watcherObjectIds, new FanoutArgs(A: objectId, B: killerObjectId, C: restartFlags), replaceable: false);

    void IWorldEventPublisher.FanoutDropItem(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DropItemMsg drop) =>
        Record(FanoutKind.DropItem, 0, watcherObjectIds, new FanoutArgs(Payload: drop), replaceable: false);

    void IWorldEventPublisher.FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, int objectId) =>
        Record(FanoutKind.EntityDespawn, 0, watcherObjectIds, new FanoutArgs(A: objectId), replaceable: false);

    void IWorldEventPublisher.FanoutInventoryUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.InventoryUpdateMsg update) =>
        Record(FanoutKind.InventoryUpdate, 0, watcherObjectIds, new FanoutArgs(Payload: update), replaceable: false);

    void IWorldEventPublisher.FanoutItemList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ItemListMsg list) =>
        Record(FanoutKind.ItemList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutCreatureSay(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CreatureSayMsg say) =>
        Record(FanoutKind.CreatureSay, 0, watcherObjectIds, new FanoutArgs(Payload: say), replaceable: false);

    void IWorldEventPublisher.FanoutSystemMessage(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SystemMessageMsg message) =>
        Record(FanoutKind.SystemMessage, 0, watcherObjectIds, new FanoutArgs(Payload: message), replaceable: false);

    void IWorldEventPublisher.FanoutMagicSkillUse(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MagicSkillUseMsg cast) =>
        Record(FanoutKind.MagicSkillUse, 0, watcherObjectIds, new FanoutArgs(Payload: cast), replaceable: false);

    void IWorldEventPublisher.FanoutRevive(IReadOnlyList<int> watcherObjectIds, int objectId) =>
        Record(FanoutKind.Revive, 0, watcherObjectIds, new FanoutArgs(A: objectId), replaceable: false);

    void IWorldEventPublisher.FanoutTeleportToLocation(IReadOnlyList<int> watcherObjectIds, int objectId, int x, int y, int z, int heading) =>
        Record(FanoutKind.TeleportToLocation, 0, watcherObjectIds, new FanoutArgs(A: objectId, B: x, C: y, D: z, E: heading), replaceable: false);

    void IWorldEventPublisher.FanoutPlayerStats(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PlayerStatsMsg stats) =>
        Record(FanoutKind.PlayerStats, 0, watcherObjectIds, new FanoutArgs(Payload: stats), replaceable: false);

    void IWorldEventPublisher.FanoutAbnormalStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AbnormalStatusUpdateMsg update) =>
        Record(FanoutKind.AbnormalStatusUpdate, update.ObjectId, watcherObjectIds, new FanoutArgs(Payload: update), replaceable: true);

    void IWorldEventPublisher.FanoutNpcHtml(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.NpcHtmlMsg html) =>
        Record(FanoutKind.NpcHtml, 0, watcherObjectIds, new FanoutArgs(Payload: html), replaceable: false);

    void IWorldEventPublisher.FanoutBuyList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BuyListMsg list) =>
        Record(FanoutKind.BuyList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SellListMsg list) =>
        Record(FanoutKind.SellList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutMultiSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MultiSellListMsg list) =>
        Record(FanoutKind.MultiSellList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutShopPreviewList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShopPreviewListMsg list) =>
        Record(FanoutKind.ShopPreviewList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutShopPreviewInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShopPreviewInfoMsg info) =>
        Record(FanoutKind.ShopPreviewInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutDice(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DiceMsg dice) =>
        Record(FanoutKind.Dice, dice.ObjectId, watcherObjectIds, new FanoutArgs(Payload: dice), replaceable: false);

    void IWorldEventPublisher.FanoutShowMiniMap(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShowMiniMapMsg map) =>
        Record(FanoutKind.ShowMiniMap, 0, watcherObjectIds, new FanoutArgs(Payload: map), replaceable: false);

    void IWorldEventPublisher.FanoutShowCalculator(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShowCalculatorMsg calc) =>
        Record(FanoutKind.ShowCalculator, 0, watcherObjectIds, new FanoutArgs(Payload: calc), replaceable: false);

    void IWorldEventPublisher.FanoutShowXMasSeal(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShowXMasSealMsg seal) =>
        Record(FanoutKind.ShowXMasSeal, 0, watcherObjectIds, new FanoutArgs(Payload: seal), replaceable: false);

    void IWorldEventPublisher.FanoutShopBalance(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShopBalanceMsg balance) =>
        Record(FanoutKind.ShopBalance, 0, watcherObjectIds, new FanoutArgs(Payload: balance), replaceable: false);

    void IWorldEventPublisher.FanoutWarehouseDepositList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WarehouseDepositListMsg list) =>
        Record(FanoutKind.WarehouseDepositList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutWarehouseWithdrawList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WarehouseWithdrawListMsg list) =>
        Record(FanoutKind.WarehouseWithdrawList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutPackageToList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PackageToListMsg list) =>
        Record(FanoutKind.PackageToList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutPackageSendableList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PackageSendableListMsg list) =>
        Record(FanoutKind.PackageSendableList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutQuestList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.QuestListMsg list) =>
        Record(FanoutKind.QuestList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutQuestDirection(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.QuestDirectionNetMsg msg) =>
        Record(FanoutKind.QuestDirection, 0, watcherObjectIds, new FanoutArgs(Payload: msg), replaceable: false);

    void IWorldEventPublisher.FanoutQuestNpcMarks(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.QuestNpcMarksNetMsg msg) =>
        Record(FanoutKind.QuestNpcMarks, 0, watcherObjectIds, new FanoutArgs(Payload: msg), replaceable: false);

    void IWorldEventPublisher.FanoutTutorialUi(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.TutorialUiNetMsg msg) =>
        Record(FanoutKind.TutorialUi, 0, watcherObjectIds, new FanoutArgs(Payload: msg), replaceable: false);

    void IWorldEventPublisher.FanoutSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SkillListMsg list) =>
        Record(FanoutKind.SkillList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutShortcutInit(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShortCutInitMsg init) =>
        Record(FanoutKind.ShortcutInit, 0, watcherObjectIds, new FanoutArgs(Payload: init), replaceable: false);

    void IWorldEventPublisher.FanoutMacroList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MacroListMsg list) =>
        Record(FanoutKind.MacroList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutChangeWaitType(IReadOnlyList<int> watcherObjectIds, int objectId, int waitType, int x, int y, int z) =>
        Record(FanoutKind.ChangeWaitType, 0, watcherObjectIds, new FanoutArgs(A: objectId, B: waitType, C: x, D: y, E: z), replaceable: false);

    void IWorldEventPublisher.FanoutSocialAction(IReadOnlyList<int> watcherObjectIds, int objectId, int actionId) =>
        Record(FanoutKind.SocialAction, 0, watcherObjectIds, new FanoutArgs(A: objectId, B: actionId), replaceable: false);

    void IWorldEventPublisher.FanoutTargetSelected(
        IReadOnlyList<int> watcherObjectIds,
        int targetObjectId,
        int colorHint,
        int selectorObjectId,
        int x,
        int y,
        int z) =>
        Record(
            FanoutKind.TargetSelected,
            0,
            watcherObjectIds,
            new FanoutArgs(A: targetObjectId, B: colorHint, C: selectorObjectId, D: x, E: y, F: z),
            replaceable: false);

    void IWorldEventPublisher.FanoutTargetUnselected(IReadOnlyList<int> watcherObjectIds, int targetObjectId, int selectorObjectId) =>
        Record(FanoutKind.TargetUnselected, 0, watcherObjectIds, new FanoutArgs(A: targetObjectId, B: selectorObjectId), replaceable: false);

    void IWorldEventPublisher.FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillListMsg list) =>
        Record(FanoutKind.AcquireSkillList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillInfoMsg info) =>
        Record(FanoutKind.AcquireSkillInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutEnchantSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantSkillListMsg list) =>
        Record(FanoutKind.EnchantSkillList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutEnchantSkillInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantSkillInfoMsg info) =>
        Record(FanoutKind.EnchantSkillInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutEnchantResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantResultMsg result) =>
        Record(FanoutKind.EnchantResult, 0, watcherObjectIds, new FanoutArgs(Payload: result), replaceable: false);

    void IWorldEventPublisher.FanoutAugmentShowMakeWindow(IReadOnlyList<int> watcherObjectIds) =>
        Record(FanoutKind.AugmentShowMakeWindow, 0, watcherObjectIds, default, replaceable: false);

    void IWorldEventPublisher.FanoutAugmentShowCancelWindow(IReadOnlyList<int> watcherObjectIds) =>
        Record(FanoutKind.AugmentShowCancelWindow, 0, watcherObjectIds, default, replaceable: false);

    void IWorldEventPublisher.FanoutAugmentPutItemResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationPutItemMsg msg) =>
        Record(FanoutKind.AugmentPutItemResult, 0, watcherObjectIds, new FanoutArgs(Payload: msg), replaceable: false);

    void IWorldEventPublisher.FanoutAugmentPutRefinerResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationPutRefinerMsg msg) =>
        Record(FanoutKind.AugmentPutRefinerResult, 0, watcherObjectIds, new FanoutArgs(Payload: msg), replaceable: false);

    void IWorldEventPublisher.FanoutAugmentPutGemResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationPutGemMsg msg) =>
        Record(FanoutKind.AugmentPutGemResult, 0, watcherObjectIds, new FanoutArgs(Payload: msg), replaceable: false);

    void IWorldEventPublisher.FanoutAugmentVariationResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationResultMsg msg) =>
        Record(FanoutKind.AugmentVariationResult, 0, watcherObjectIds, new FanoutArgs(Payload: msg), replaceable: false);

    void IWorldEventPublisher.FanoutAugmentPutCancelItemResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationCancelItemMsg msg) =>
        Record(FanoutKind.AugmentPutCancelItemResult, 0, watcherObjectIds, new FanoutArgs(Payload: msg), replaceable: false);

    void IWorldEventPublisher.FanoutAugmentCancelResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.VariationCancelResultMsg msg) =>
        Record(FanoutKind.AugmentCancelResult, 0, watcherObjectIds, new FanoutArgs(Payload: msg), replaceable: false);

    void IWorldEventPublisher.FanoutPledgeStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeStatusChangedMsg status) =>
        Record(FanoutKind.PledgeStatus, 0, watcherObjectIds, new FanoutArgs(Payload: status), replaceable: false);

    void IWorldEventPublisher.FanoutPledgeMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeMemberListMsg list) =>
        Record(FanoutKind.PledgeMemberList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutCrestData(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.CrestDataNetMsg crest) =>
        Record(FanoutKind.CrestData, 0, watcherObjectIds, new FanoutArgs(Payload: crest), replaceable: false);

    void IWorldEventPublisher.FanoutPledgeWarList(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PledgeWarListNetMsg wars) =>
        Record(FanoutKind.PledgeWarList, 0, watcherObjectIds, new FanoutArgs(Payload: wars), replaceable: false);

    void IWorldEventPublisher.FanoutRelationChanged(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.RelationChangedNetMsg relation) =>
        Record(FanoutKind.RelationChanged, 0, watcherObjectIds, new FanoutArgs(Payload: relation), replaceable: false);

    void IWorldEventPublisher.FanoutPledgeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PledgeInfoNetMsg info) =>
        Record(FanoutKind.PledgeInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutAllyInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.AllyInfoNetMsg info) =>
        Record(FanoutKind.AllyInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutAllyAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.AllyAskJoinNetMsg ask) =>
        Record(FanoutKind.AllyAskJoin, 0, watcherObjectIds, new FanoutArgs(Payload: ask), replaceable: false);

    void IWorldEventPublisher.FanoutClanHallList(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.ClanHallListNetMsg halls) =>
        Record(FanoutKind.ClanHallList, 0, watcherObjectIds, new FanoutArgs(Payload: halls), replaceable: false);

    void IWorldEventPublisher.FanoutAgitDecoInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.AgitDecoInfoNetMsg deco) =>
        Record(FanoutKind.AgitDecoInfo, 0, watcherObjectIds, new FanoutArgs(Payload: deco), replaceable: false);

    void IWorldEventPublisher.FanoutClientSetTime(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.ClientSetTimeNetMsg time) =>
        Record(FanoutKind.ClientSetTime, 0, watcherObjectIds, new FanoutArgs(Payload: time), replaceable: false);

    void IWorldEventPublisher.FanoutDoorStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.DoorStatusNetMsg door) =>
        Record(FanoutKind.DoorStatus, 0, watcherObjectIds, new FanoutArgs(Payload: door), replaceable: false);

    void IWorldEventPublisher.FanoutCastleInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.CastleInfoNetMsg castle) =>
        Record(FanoutKind.CastleInfo, 0, watcherObjectIds, new FanoutArgs(Payload: castle), replaceable: false);

    void IWorldEventPublisher.FanoutManorInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.ManorInfoNetMsg manor) =>
        Record(FanoutKind.ManorInfo, 0, watcherObjectIds, new FanoutArgs(Payload: manor), replaceable: false);

    void IWorldEventPublisher.FanoutFishing(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.FishingEventNetMsg fishing) =>
        Record(FanoutKind.Fishing, 0, watcherObjectIds, new FanoutArgs(Payload: fishing), replaceable: false);

    void IWorldEventPublisher.FanoutSummonInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.SummonInfoNetMsg info) =>
        Record(FanoutKind.SummonInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutSummonGone(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.SummonGoneNetMsg gone) =>
        Record(FanoutKind.SummonGone, 0, watcherObjectIds, new FanoutArgs(Payload: gone), replaceable: false);

    void IWorldEventPublisher.FanoutPetInventory(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PetInventoryNetMsg inventory) =>
        Record(FanoutKind.PetInventory, 0, watcherObjectIds, new FanoutArgs(Payload: inventory), replaceable: false);

    void IWorldEventPublisher.FanoutRide(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.RideNetMsg ride) =>
        Record(FanoutKind.Ride, 0, watcherObjectIds, new FanoutArgs(Payload: ride), replaceable: false);

    void IWorldEventPublisher.FanoutPlayerActionState(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PlayerActionStateNetMsg state) =>
        Record(FanoutKind.PlayerActionState, 0, watcherObjectIds, new FanoutArgs(Payload: state), replaceable: false);

    void IWorldEventPublisher.FanoutDuelEvent(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.DuelEventNetMsg duel) =>
        Record(FanoutKind.DuelEvent, 0, watcherObjectIds, new FanoutArgs(Payload: duel), replaceable: false);

    void IWorldEventPublisher.FanoutDuelUserInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.DuelUserInfoNetMsg info) =>
        Record(FanoutKind.DuelUserInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutClanAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanAskJoinMsg ask) =>
        Record(FanoutKind.ClanAskJoin, 0, watcherObjectIds, new FanoutArgs(Payload: ask), replaceable: false);

    void IWorldEventPublisher.FanoutClanJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanJoinResultMsg result) =>
        Record(FanoutKind.ClanJoinResult, 0, watcherObjectIds, new FanoutArgs(Payload: result), replaceable: false);

    void IWorldEventPublisher.FanoutPartyAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyAskJoinMsg ask) =>
        Record(FanoutKind.PartyAskJoin, 0, watcherObjectIds, new FanoutArgs(Payload: ask), replaceable: false);

    void IWorldEventPublisher.FanoutPartyJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyJoinResultMsg result) =>
        Record(FanoutKind.PartyJoinResult, 0, watcherObjectIds, new FanoutArgs(Payload: result), replaceable: false);

    void IWorldEventPublisher.FanoutPartyMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberListMsg list) =>
        Record(FanoutKind.PartyMemberList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutPartyMemberUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberUpdateMsg update) =>
        Record(FanoutKind.PartyMemberUpdate, 0, watcherObjectIds, new FanoutArgs(Payload: update), replaceable: false);

    void IWorldEventPublisher.FanoutPartySpelled(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AbnormalStatusUpdateMsg effects) =>
        Record(FanoutKind.PartySpelled, 0, watcherObjectIds, new FanoutArgs(Payload: effects), replaceable: false);

    void IWorldEventPublisher.FanoutPartyMemberPosition(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberPositionMsg positions) =>
        Record(FanoutKind.PartyMemberPosition, 0, watcherObjectIds, new FanoutArgs(Payload: positions), replaceable: false);

    void IWorldEventPublisher.FanoutCommandChannelAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CommandChannelAskJoinMsg ask) =>
        Record(FanoutKind.CommandChannelAskJoin, 0, watcherObjectIds, new FanoutArgs(Payload: ask), replaceable: false);

    void IWorldEventPublisher.FanoutCommandChannelOpen(IReadOnlyList<int> watcherObjectIds) =>
        Record(FanoutKind.CommandChannelOpen, 0, watcherObjectIds, default, replaceable: false);

    void IWorldEventPublisher.FanoutCommandChannelClose(IReadOnlyList<int> watcherObjectIds) =>
        Record(FanoutKind.CommandChannelClose, 0, watcherObjectIds, default, replaceable: false);

    void IWorldEventPublisher.FanoutCommandChannelInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CommandChannelInfoMsg info) =>
        Record(FanoutKind.CommandChannelInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutMagicSkillLaunched(IReadOnlyList<int> watcherObjectIds, int casterObjectId, int skillId, int skillLevel, int[] targetObjectIds)
    {
        int[] targetObjectIdsCopy = [.. targetObjectIds];
        Record(FanoutKind.MagicSkillLaunched, 0, watcherObjectIds, new FanoutArgs(A: casterObjectId, B: skillId, C: skillLevel, Payload: targetObjectIdsCopy), replaceable: false);
    }

    void IWorldEventPublisher.FanoutMagicSkillCanceled(IReadOnlyList<int> watcherObjectIds, int objectId) =>
        Record(FanoutKind.MagicSkillCanceled, 0, watcherObjectIds, new FanoutArgs(A: objectId), replaceable: false);

    void IWorldEventPublisher.FanoutPlaySound(IReadOnlyList<int> watcherObjectIds, int type, string file, int objectId, int x, int y, int z, int delayMs) =>
        Record(FanoutKind.PlaySound, 0, watcherObjectIds, new FanoutArgs(A: type, Payload: file, B: objectId, C: x, D: y, E: z, F: delayMs), replaceable: false);

    void IWorldEventPublisher.FanoutMonRaceInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.MonRaceInfoNetMsg packet) =>
        Record(FanoutKind.MonRaceInfo, 0, watcherObjectIds, new FanoutArgs(Payload: packet), replaceable: false);

    void IWorldEventPublisher.FanoutVehicleInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.VehicleInfoNetMsg info) =>
        Record(FanoutKind.VehicleInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutVehicleDeparture(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.VehicleDepartureNetMsg departure) =>
        Record(FanoutKind.VehicleDeparture, 0, watcherObjectIds, new FanoutArgs(Payload: departure), replaceable: false);

    void IWorldEventPublisher.FanoutTradeAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeAskMsg ask) =>
        Record(FanoutKind.TradeAsk, 0, watcherObjectIds, new FanoutArgs(Payload: ask), replaceable: false);

    void IWorldEventPublisher.FanoutTradeRequestResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeRequestResultMsg result) =>
        Record(FanoutKind.TradeRequestResult, 0, watcherObjectIds, new FanoutArgs(Payload: result), replaceable: false);

    void IWorldEventPublisher.FanoutTradeStart(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeStartMsg start) =>
        Record(FanoutKind.TradeStart, 0, watcherObjectIds, new FanoutArgs(Payload: start), replaceable: false);

    void IWorldEventPublisher.FanoutTradeOfferUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeOfferUpdateMsg update) =>
        Record(FanoutKind.TradeOfferUpdate, 0, watcherObjectIds, new FanoutArgs(Payload: update), replaceable: false);

    void IWorldEventPublisher.FanoutTradeConfirmState(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeConfirmStateMsg state) =>
        Record(FanoutKind.TradeConfirmState, 0, watcherObjectIds, new FanoutArgs(Payload: state), replaceable: false);

    void IWorldEventPublisher.FanoutTradeFinished(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeFinishedMsg finished) =>
        Record(FanoutKind.TradeFinished, 0, watcherObjectIds, new FanoutArgs(Payload: finished), replaceable: false);

    void IWorldEventPublisher.FanoutPrivateStoreInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreInfoMsg info) =>
        Record(FanoutKind.PrivateStoreInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutPrivateStoreListSell(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreListSellMsg list) =>
        Record(FanoutKind.PrivateStoreListSell, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutPrivateStoreListBuy(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreListBuyMsg list) =>
        Record(FanoutKind.PrivateStoreListBuy, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutRecipeBookList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeBookListMsg list) =>
        Record(FanoutKind.RecipeBookList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutRecipeItemMakeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeItemMakeInfoMsg info) =>
        Record(FanoutKind.RecipeItemMakeInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutRecipeShopSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeShopSellListMsg list) =>
        Record(FanoutKind.RecipeShopSellList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutRecipeShopItemInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeShopItemInfoMsg info) =>
        Record(FanoutKind.RecipeShopItemInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutFriendInviteAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendInviteAskMsg ask) =>
        Record(FanoutKind.FriendInviteAsk, 0, watcherObjectIds, new FanoutArgs(Payload: ask), replaceable: false);

    void IWorldEventPublisher.FanoutWeddingEngageAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WeddingEngageAskMsg ask) =>
        Record(FanoutKind.WeddingEngageAsk, 0, watcherObjectIds, new FanoutArgs(Payload: ask), replaceable: false);

    void IWorldEventPublisher.FanoutFriendList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendListMsg list) =>
        Record(FanoutKind.FriendList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutFriendUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendUpdateMsg update) =>
        Record(FanoutKind.FriendUpdate, 0, watcherObjectIds, new FanoutArgs(Payload: update), replaceable: false);

    void IWorldEventPublisher.FanoutBlockList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BlockListMsg list) =>
        Record(FanoutKind.BlockList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutBossStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BossStatusMsg status) =>
        Record(FanoutKind.BossStatus, 0, watcherObjectIds, new FanoutArgs(Payload: status), replaceable: false);

    void IWorldEventPublisher.FanoutSiegeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SiegeInfoMsg info) =>
        Record(FanoutKind.SiegeInfo, 0, watcherObjectIds, new FanoutArgs(Payload: info), replaceable: false);

    void IWorldEventPublisher.FanoutOlympiadStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadStatusMsg status) =>
        Record(FanoutKind.OlympiadStatus, 0, watcherObjectIds, new FanoutArgs(Payload: status), replaceable: false);

    void IWorldEventPublisher.FanoutOlympiadMatch(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadMatchMsg match) =>
        Record(FanoutKind.OlympiadMatch, 0, watcherObjectIds, new FanoutArgs(Payload: match), replaceable: false);

    void IWorldEventPublisher.FanoutOlympiadCountdown(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadCountdownMsg countdown) =>
        Record(FanoutKind.OlympiadCountdown, 0, watcherObjectIds, new FanoutArgs(Payload: countdown), replaceable: false);

    void IWorldEventPublisher.FanoutOlympiadResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadResultMsg result) =>
        Record(FanoutKind.OlympiadResult, 0, watcherObjectIds, new FanoutArgs(Payload: result), replaceable: false);

    void IWorldEventPublisher.FanoutSevenSignsStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SevenSignsStatusMsg status) =>
        Record(FanoutKind.SevenSignsStatus, 0, watcherObjectIds, new FanoutArgs(Payload: status), replaceable: false);

    void IWorldEventPublisher.FanoutSevenSignsSky(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SevenSignsSkyMsg sky) =>
        Record(FanoutKind.SevenSignsSky, 0, watcherObjectIds, new FanoutArgs(Payload: sky), replaceable: false);

    void IWorldEventPublisher.FanoutDungeonEntered(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DungeonEnteredMsg entered) =>
        Record(FanoutKind.DungeonEntered, 0, watcherObjectIds, new FanoutArgs(Payload: entered), replaceable: false);

    void IWorldEventPublisher.FanoutCursedWeaponList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CursedWeaponListMsg list) =>
        Record(FanoutKind.CursedWeaponList, 0, watcherObjectIds, new FanoutArgs(Payload: list), replaceable: false);

    void IWorldEventPublisher.FanoutCursedWeaponLocation(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CursedWeaponLocationMsg location) =>
        Record(FanoutKind.CursedWeaponLocation, 0, watcherObjectIds, new FanoutArgs(Payload: location), replaceable: false);
}
