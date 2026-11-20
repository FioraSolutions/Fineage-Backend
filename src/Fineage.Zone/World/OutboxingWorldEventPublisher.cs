using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Routes sim-tick fanout through <see cref="WorldTickOutbox"/>; handler-time traffic goes straight to the inner publisher.
/// </summary>
internal sealed class OutboxingWorldEventPublisher(IWorldEventPublisher inner, WorldTickOutbox outbox) : IWorldEventPublisher
{
    void IWorldEventPublisher.FanoutMoveState(IReadOnlyList<int> watcherObjectIds, int moverObjectId, int originX, int originY, int originZ, int destX, int destY, int destZ, bool running)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordReplaceable(PresenceCoalesceKind.MoveState, moverObjectId, p => p.FanoutMoveState(watcherObjectIds, moverObjectId, originX, originY, originZ, destX, destY, destZ, running));
            return;
        }
        inner.FanoutMoveState(watcherObjectIds, moverObjectId, originX, originY, originZ, destX, destY, destZ, running);
    }

    void IWorldEventPublisher.FanoutKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutKnownlistDiff(selfObjectId, added, removed));
            return;
        }
        inner.FanoutKnownlistDiff(selfObjectId, added, removed);
    }

    void IWorldEventPublisher.FanoutChangeMoveType(IReadOnlyList<int> watcherObjectIds, int moverObjectId, bool running, float runSpeed, float walkSpeed, bool inWater)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordReplaceable(PresenceCoalesceKind.ChangeMoveType, moverObjectId, p => p.FanoutChangeMoveType(watcherObjectIds, moverObjectId, running, runSpeed, walkSpeed, inWater));
            return;
        }
        inner.FanoutChangeMoveType(watcherObjectIds, moverObjectId, running, runSpeed, walkSpeed, inWater);
    }

    void IWorldEventPublisher.FanoutPositionCorrection(IReadOnlyList<int> watcherObjectIds, int objectId, int x, int y, int z, int heading)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPositionCorrection(watcherObjectIds, objectId, x, y, z, heading));
            return;
        }
        inner.FanoutPositionCorrection(watcherObjectIds, objectId, x, y, z, heading);
    }

    void IWorldEventPublisher.FanoutEtcStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EtcStatusUpdateMsg status)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutEtcStatusUpdate(watcherObjectIds, status));
            return;
        }
        inner.FanoutEtcStatusUpdate(watcherObjectIds, status);
    }

    void IWorldEventPublisher.FanoutAttackStance(IReadOnlyList<int> watcherObjectIds, int objectId, bool inStance)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutAttackStance(watcherObjectIds, objectId, inStance));
            return;
        }
        inner.FanoutAttackStance(watcherObjectIds, objectId, inStance);
    }

    void IWorldEventPublisher.FanoutAutoSoulShot(IReadOnlyList<int> watcherObjectIds, int itemId, bool enabled)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutAutoSoulShot(watcherObjectIds, itemId, enabled));
            return;
        }
        inner.FanoutAutoSoulShot(watcherObjectIds, itemId, enabled);
    }

    void IWorldEventPublisher.FanoutUserInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.UserInfoMsg info)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutUserInfo(watcherObjectIds, info));
            return;
        }
        inner.FanoutUserInfo(watcherObjectIds, info);
    }

    void IWorldEventPublisher.FanoutCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutCharInfo(watcherObjectIds, objectId));
            return;
        }
        inner.FanoutCharInfo(watcherObjectIds, objectId);
    }

    void IWorldEventPublisher.FanoutAttackState(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AttackMsg attack)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutAttackState(watcherObjectIds, attack));
            return;
        }
        inner.FanoutAttackState(watcherObjectIds, attack);
    }

    void IWorldEventPublisher.FanoutStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.StatusUpdateMsg status)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordReplaceable(PresenceCoalesceKind.StatusUpdate, status.ObjectId, p => p.FanoutStatusUpdate(watcherObjectIds, status));
            return;
        }
        inner.FanoutStatusUpdate(watcherObjectIds, status);
    }

    void IWorldEventPublisher.FanoutDie(IReadOnlyList<int> watcherObjectIds, int objectId, int killerObjectId, int restartFlags)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutDie(watcherObjectIds, objectId, killerObjectId, restartFlags));
            return;
        }
        inner.FanoutDie(watcherObjectIds, objectId, killerObjectId, restartFlags);
    }

    void IWorldEventPublisher.FanoutDropItem(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DropItemMsg drop)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutDropItem(watcherObjectIds, drop));
            return;
        }
        inner.FanoutDropItem(watcherObjectIds, drop);
    }

    void IWorldEventPublisher.FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutEntityDespawn(watcherObjectIds, objectId));
            return;
        }
        inner.FanoutEntityDespawn(watcherObjectIds, objectId);
    }

    void IWorldEventPublisher.FanoutInventoryUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.InventoryUpdateMsg update)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutInventoryUpdate(watcherObjectIds, update));
            return;
        }
        inner.FanoutInventoryUpdate(watcherObjectIds, update);
    }

    void IWorldEventPublisher.FanoutCreatureSay(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CreatureSayMsg say)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutCreatureSay(watcherObjectIds, say));
            return;
        }
        inner.FanoutCreatureSay(watcherObjectIds, say);
    }

    void IWorldEventPublisher.FanoutSystemMessage(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SystemMessageMsg message)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutSystemMessage(watcherObjectIds, message));
            return;
        }
        inner.FanoutSystemMessage(watcherObjectIds, message);
    }

    void IWorldEventPublisher.FanoutMagicSkillUse(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MagicSkillUseMsg cast)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutMagicSkillUse(watcherObjectIds, cast));
            return;
        }
        inner.FanoutMagicSkillUse(watcherObjectIds, cast);
    }

    void IWorldEventPublisher.FanoutRevive(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutRevive(watcherObjectIds, objectId));
            return;
        }
        inner.FanoutRevive(watcherObjectIds, objectId);
    }

    void IWorldEventPublisher.FanoutTeleportToLocation(IReadOnlyList<int> watcherObjectIds, int objectId, int x, int y, int z, int heading)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutTeleportToLocation(watcherObjectIds, objectId, x, y, z, heading));
            return;
        }
        inner.FanoutTeleportToLocation(watcherObjectIds, objectId, x, y, z, heading);
    }

    void IWorldEventPublisher.FanoutPlayerStats(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PlayerStatsMsg stats)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPlayerStats(watcherObjectIds, stats));
            return;
        }
        inner.FanoutPlayerStats(watcherObjectIds, stats);
    }

    void IWorldEventPublisher.FanoutAbnormalStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AbnormalStatusUpdateMsg update)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordReplaceable(PresenceCoalesceKind.AbnormalStatusUpdate, update.ObjectId, p => p.FanoutAbnormalStatusUpdate(watcherObjectIds, update));
            return;
        }
        inner.FanoutAbnormalStatusUpdate(watcherObjectIds, update);
    }

    void IWorldEventPublisher.FanoutNpcHtml(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.NpcHtmlMsg html)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutNpcHtml(watcherObjectIds, html));
            return;
        }
        inner.FanoutNpcHtml(watcherObjectIds, html);
    }

    void IWorldEventPublisher.FanoutBuyList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BuyListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutBuyList(watcherObjectIds, list));
            return;
        }
        inner.FanoutBuyList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SellListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutSellList(watcherObjectIds, list));
            return;
        }
        inner.FanoutSellList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutMultiSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MultiSellListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutMultiSellList(watcherObjectIds, list));
            return;
        }
        inner.FanoutMultiSellList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutShopBalance(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShopBalanceMsg balance)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutShopBalance(watcherObjectIds, balance));
            return;
        }
        inner.FanoutShopBalance(watcherObjectIds, balance);
    }

    void IWorldEventPublisher.FanoutWarehouseDepositList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WarehouseDepositListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutWarehouseDepositList(watcherObjectIds, list));
            return;
        }
        inner.FanoutWarehouseDepositList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutWarehouseWithdrawList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WarehouseWithdrawListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutWarehouseWithdrawList(watcherObjectIds, list));
            return;
        }
        inner.FanoutWarehouseWithdrawList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutQuestList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.QuestListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutQuestList(watcherObjectIds, list));
            return;
        }
        inner.FanoutQuestList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutQuestDirection(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.QuestDirectionNetMsg msg)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutQuestDirection(watcherObjectIds, msg));
            return;
        }
        inner.FanoutQuestDirection(watcherObjectIds, msg);
    }

    void IWorldEventPublisher.FanoutQuestNpcMarks(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.QuestNpcMarksNetMsg msg)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutQuestNpcMarks(watcherObjectIds, msg));
            return;
        }
        inner.FanoutQuestNpcMarks(watcherObjectIds, msg);
    }

    void IWorldEventPublisher.FanoutTutorialUi(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.TutorialUiNetMsg msg)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutTutorialUi(watcherObjectIds, msg));
            return;
        }
        inner.FanoutTutorialUi(watcherObjectIds, msg);
    }

    void IWorldEventPublisher.FanoutSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SkillListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutSkillList(watcherObjectIds, list));
            return;
        }
        inner.FanoutSkillList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutShortcutInit(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShortCutInitMsg init)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutShortcutInit(watcherObjectIds, init));
            return;
        }
        inner.FanoutShortcutInit(watcherObjectIds, init);
    }

    void IWorldEventPublisher.FanoutChangeWaitType(IReadOnlyList<int> watcherObjectIds, int objectId, int waitType, int x, int y, int z)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutChangeWaitType(watcherObjectIds, objectId, waitType, x, y, z));
            return;
        }
        inner.FanoutChangeWaitType(watcherObjectIds, objectId, waitType, x, y, z);
    }

    void IWorldEventPublisher.FanoutSocialAction(IReadOnlyList<int> watcherObjectIds, int objectId, int actionId)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutSocialAction(watcherObjectIds, objectId, actionId));
            return;
        }
        inner.FanoutSocialAction(watcherObjectIds, objectId, actionId);
    }

    void IWorldEventPublisher.FanoutTargetSelected(IReadOnlyList<int> watcherObjectIds, int targetObjectId, int colorHint)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutTargetSelected(watcherObjectIds, targetObjectId, colorHint));
            return;
        }
        inner.FanoutTargetSelected(watcherObjectIds, targetObjectId, colorHint);
    }

    void IWorldEventPublisher.FanoutTargetUnselected(IReadOnlyList<int> watcherObjectIds, int targetObjectId)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutTargetUnselected(watcherObjectIds, targetObjectId));
            return;
        }
        inner.FanoutTargetUnselected(watcherObjectIds, targetObjectId);
    }

    void IWorldEventPublisher.FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutAcquireSkillList(watcherObjectIds, list));
            return;
        }
        inner.FanoutAcquireSkillList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillInfoMsg info)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutAcquireSkillInfo(watcherObjectIds, info));
            return;
        }
        inner.FanoutAcquireSkillInfo(watcherObjectIds, info);
    }

    void IWorldEventPublisher.FanoutEnchantResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantResultMsg result)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutEnchantResult(watcherObjectIds, result));
            return;
        }
        inner.FanoutEnchantResult(watcherObjectIds, result);
    }

    void IWorldEventPublisher.FanoutPledgeStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeStatusChangedMsg status)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPledgeStatus(watcherObjectIds, status));
            return;
        }
        inner.FanoutPledgeStatus(watcherObjectIds, status);
    }

    void IWorldEventPublisher.FanoutPledgeMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeMemberListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPledgeMemberList(watcherObjectIds, list));
            return;
        }
        inner.FanoutPledgeMemberList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutCrestData(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.CrestDataNetMsg crest)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutCrestData(watcherObjectIds, crest));
            return;
        }
        inner.FanoutCrestData(watcherObjectIds, crest);
    }

    void IWorldEventPublisher.FanoutPledgeWarList(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PledgeWarListNetMsg wars)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPledgeWarList(watcherObjectIds, wars));
            return;
        }
        inner.FanoutPledgeWarList(watcherObjectIds, wars);
    }

    void IWorldEventPublisher.FanoutRelationChanged(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.RelationChangedNetMsg relation)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutRelationChanged(watcherObjectIds, relation));
            return;
        }
        inner.FanoutRelationChanged(watcherObjectIds, relation);
    }

    void IWorldEventPublisher.FanoutPledgeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PledgeInfoNetMsg info)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPledgeInfo(watcherObjectIds, info));
            return;
        }
        inner.FanoutPledgeInfo(watcherObjectIds, info);
    }

    void IWorldEventPublisher.FanoutAllyInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.AllyInfoNetMsg info)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutAllyInfo(watcherObjectIds, info));
            return;
        }
        inner.FanoutAllyInfo(watcherObjectIds, info);
    }

    void IWorldEventPublisher.FanoutAllyAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.AllyAskJoinNetMsg ask)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutAllyAskJoin(watcherObjectIds, ask));
            return;
        }
        inner.FanoutAllyAskJoin(watcherObjectIds, ask);
    }

    void IWorldEventPublisher.FanoutClanHallList(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.ClanHallListNetMsg halls)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutClanHallList(watcherObjectIds, halls));
            return;
        }
        inner.FanoutClanHallList(watcherObjectIds, halls);
    }

    void IWorldEventPublisher.FanoutDoorStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.DoorStatusNetMsg door)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutDoorStatus(watcherObjectIds, door));
            return;
        }
        inner.FanoutDoorStatus(watcherObjectIds, door);
    }

    void IWorldEventPublisher.FanoutCastleInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.CastleInfoNetMsg castle)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutCastleInfo(watcherObjectIds, castle));
            return;
        }
        inner.FanoutCastleInfo(watcherObjectIds, castle);
    }

    void IWorldEventPublisher.FanoutManorInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.ManorInfoNetMsg manor)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutManorInfo(watcherObjectIds, manor));
            return;
        }
        inner.FanoutManorInfo(watcherObjectIds, manor);
    }

    void IWorldEventPublisher.FanoutFishing(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.FishingEventNetMsg fishing)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutFishing(watcherObjectIds, fishing));
            return;
        }
        inner.FanoutFishing(watcherObjectIds, fishing);
    }

    void IWorldEventPublisher.FanoutSummonInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.SummonInfoNetMsg info)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutSummonInfo(watcherObjectIds, info));
            return;
        }
        inner.FanoutSummonInfo(watcherObjectIds, info);
    }

    void IWorldEventPublisher.FanoutSummonGone(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.SummonGoneNetMsg gone)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutSummonGone(watcherObjectIds, gone));
            return;
        }
        inner.FanoutSummonGone(watcherObjectIds, gone);
    }

    void IWorldEventPublisher.FanoutPetInventory(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.PetInventoryNetMsg inventory)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPetInventory(watcherObjectIds, inventory));
            return;
        }
        inner.FanoutPetInventory(watcherObjectIds, inventory);
    }

    void IWorldEventPublisher.FanoutDuelEvent(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.DuelEventNetMsg duel)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutDuelEvent(watcherObjectIds, duel));
            return;
        }
        inner.FanoutDuelEvent(watcherObjectIds, duel);
    }

    void IWorldEventPublisher.FanoutDuelUserInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Net.Contract.DuelUserInfoNetMsg info)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutDuelUserInfo(watcherObjectIds, info));
            return;
        }
        inner.FanoutDuelUserInfo(watcherObjectIds, info);
    }

    void IWorldEventPublisher.FanoutClanAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanAskJoinMsg ask)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutClanAskJoin(watcherObjectIds, ask));
            return;
        }
        inner.FanoutClanAskJoin(watcherObjectIds, ask);
    }

    void IWorldEventPublisher.FanoutClanJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanJoinResultMsg result)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutClanJoinResult(watcherObjectIds, result));
            return;
        }
        inner.FanoutClanJoinResult(watcherObjectIds, result);
    }

    void IWorldEventPublisher.FanoutPartyAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyAskJoinMsg ask)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPartyAskJoin(watcherObjectIds, ask));
            return;
        }
        inner.FanoutPartyAskJoin(watcherObjectIds, ask);
    }

    void IWorldEventPublisher.FanoutPartyJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyJoinResultMsg result)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPartyJoinResult(watcherObjectIds, result));
            return;
        }
        inner.FanoutPartyJoinResult(watcherObjectIds, result);
    }

    void IWorldEventPublisher.FanoutPartyMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPartyMemberList(watcherObjectIds, list));
            return;
        }
        inner.FanoutPartyMemberList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutPartyMemberUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberUpdateMsg update)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPartyMemberUpdate(watcherObjectIds, update));
            return;
        }
        inner.FanoutPartyMemberUpdate(watcherObjectIds, update);
    }

    void IWorldEventPublisher.FanoutPartySpelled(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AbnormalStatusUpdateMsg effects)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPartySpelled(watcherObjectIds, effects));
            return;
        }
        inner.FanoutPartySpelled(watcherObjectIds, effects);
    }

    void IWorldEventPublisher.FanoutPartyMemberPosition(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberPositionMsg positions)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPartyMemberPosition(watcherObjectIds, positions));
            return;
        }
        inner.FanoutPartyMemberPosition(watcherObjectIds, positions);
    }

    void IWorldEventPublisher.FanoutMagicSkillLaunched(IReadOnlyList<int> watcherObjectIds, int casterObjectId, int skillId, int skillLevel, int[] targetObjectIds)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutMagicSkillLaunched(watcherObjectIds, casterObjectId, skillId, skillLevel, targetObjectIds));
            return;
        }
        inner.FanoutMagicSkillLaunched(watcherObjectIds, casterObjectId, skillId, skillLevel, targetObjectIds);
    }

    void IWorldEventPublisher.FanoutMagicSkillCanceled(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutMagicSkillCanceled(watcherObjectIds, objectId));
            return;
        }
        inner.FanoutMagicSkillCanceled(watcherObjectIds, objectId);
    }

    void IWorldEventPublisher.FanoutPlaySound(IReadOnlyList<int> watcherObjectIds, int type, string file, int objectId, int x, int y, int z, int delayMs)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPlaySound(watcherObjectIds, type, file, objectId, x, y, z, delayMs));
            return;
        }
        inner.FanoutPlaySound(watcherObjectIds, type, file, objectId, x, y, z, delayMs);
    }

    void IWorldEventPublisher.FanoutTradeAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeAskMsg ask)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutTradeAsk(watcherObjectIds, ask));
            return;
        }
        inner.FanoutTradeAsk(watcherObjectIds, ask);
    }

    void IWorldEventPublisher.FanoutTradeRequestResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeRequestResultMsg result)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutTradeRequestResult(watcherObjectIds, result));
            return;
        }
        inner.FanoutTradeRequestResult(watcherObjectIds, result);
    }

    void IWorldEventPublisher.FanoutTradeStart(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeStartMsg start)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutTradeStart(watcherObjectIds, start));
            return;
        }
        inner.FanoutTradeStart(watcherObjectIds, start);
    }

    void IWorldEventPublisher.FanoutTradeOfferUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeOfferUpdateMsg update)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutTradeOfferUpdate(watcherObjectIds, update));
            return;
        }
        inner.FanoutTradeOfferUpdate(watcherObjectIds, update);
    }

    void IWorldEventPublisher.FanoutTradeConfirmState(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeConfirmStateMsg state)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutTradeConfirmState(watcherObjectIds, state));
            return;
        }
        inner.FanoutTradeConfirmState(watcherObjectIds, state);
    }

    void IWorldEventPublisher.FanoutTradeFinished(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeFinishedMsg finished)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutTradeFinished(watcherObjectIds, finished));
            return;
        }
        inner.FanoutTradeFinished(watcherObjectIds, finished);
    }

    void IWorldEventPublisher.FanoutPrivateStoreInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreInfoMsg info)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPrivateStoreInfo(watcherObjectIds, info));
            return;
        }
        inner.FanoutPrivateStoreInfo(watcherObjectIds, info);
    }

    void IWorldEventPublisher.FanoutPrivateStoreListSell(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreListSellMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPrivateStoreListSell(watcherObjectIds, list));
            return;
        }
        inner.FanoutPrivateStoreListSell(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutPrivateStoreListBuy(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreListBuyMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutPrivateStoreListBuy(watcherObjectIds, list));
            return;
        }
        inner.FanoutPrivateStoreListBuy(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutRecipeBookList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeBookListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutRecipeBookList(watcherObjectIds, list));
            return;
        }
        inner.FanoutRecipeBookList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutRecipeItemMakeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeItemMakeInfoMsg info)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutRecipeItemMakeInfo(watcherObjectIds, info));
            return;
        }
        inner.FanoutRecipeItemMakeInfo(watcherObjectIds, info);
    }

    void IWorldEventPublisher.FanoutRecipeShopSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeShopSellListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutRecipeShopSellList(watcherObjectIds, list));
            return;
        }
        inner.FanoutRecipeShopSellList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutRecipeShopItemInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeShopItemInfoMsg info)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutRecipeShopItemInfo(watcherObjectIds, info));
            return;
        }
        inner.FanoutRecipeShopItemInfo(watcherObjectIds, info);
    }

    void IWorldEventPublisher.FanoutFriendInviteAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendInviteAskMsg ask)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutFriendInviteAsk(watcherObjectIds, ask));
            return;
        }
        inner.FanoutFriendInviteAsk(watcherObjectIds, ask);
    }

    void IWorldEventPublisher.FanoutFriendList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutFriendList(watcherObjectIds, list));
            return;
        }
        inner.FanoutFriendList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutFriendUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.FriendUpdateMsg update)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutFriendUpdate(watcherObjectIds, update));
            return;
        }
        inner.FanoutFriendUpdate(watcherObjectIds, update);
    }

    void IWorldEventPublisher.FanoutBlockList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BlockListMsg list)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutBlockList(watcherObjectIds, list));
            return;
        }
        inner.FanoutBlockList(watcherObjectIds, list);
    }

    void IWorldEventPublisher.FanoutBossStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BossStatusMsg status)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutBossStatus(watcherObjectIds, status));
            return;
        }
        inner.FanoutBossStatus(watcherObjectIds, status);
    }

    void IWorldEventPublisher.FanoutSiegeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SiegeInfoMsg info)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutSiegeInfo(watcherObjectIds, info));
            return;
        }
        inner.FanoutSiegeInfo(watcherObjectIds, info);
    }

    void IWorldEventPublisher.FanoutOlympiadStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadStatusMsg status)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutOlympiadStatus(watcherObjectIds, status));
            return;
        }
        inner.FanoutOlympiadStatus(watcherObjectIds, status);
    }

    void IWorldEventPublisher.FanoutOlympiadMatch(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadMatchMsg match)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutOlympiadMatch(watcherObjectIds, match));
            return;
        }
        inner.FanoutOlympiadMatch(watcherObjectIds, match);
    }

    void IWorldEventPublisher.FanoutOlympiadCountdown(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadCountdownMsg countdown)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutOlympiadCountdown(watcherObjectIds, countdown));
            return;
        }
        inner.FanoutOlympiadCountdown(watcherObjectIds, countdown);
    }

    void IWorldEventPublisher.FanoutOlympiadResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadResultMsg result)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutOlympiadResult(watcherObjectIds, result));
            return;
        }
        inner.FanoutOlympiadResult(watcherObjectIds, result);
    }

    void IWorldEventPublisher.FanoutSevenSignsStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SevenSignsStatusMsg status)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutSevenSignsStatus(watcherObjectIds, status));
            return;
        }
        inner.FanoutSevenSignsStatus(watcherObjectIds, status);
    }

    void IWorldEventPublisher.FanoutSevenSignsSky(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SevenSignsSkyMsg sky)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutSevenSignsSky(watcherObjectIds, sky));
            return;
        }
        inner.FanoutSevenSignsSky(watcherObjectIds, sky);
    }

    void IWorldEventPublisher.FanoutDungeonEntered(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DungeonEnteredMsg entered)
    {
        if (outbox.IsRecording)
        {
            outbox.RecordOrdered(p => p.FanoutDungeonEntered(watcherObjectIds, entered));
            return;
        }
        inner.FanoutDungeonEntered(watcherObjectIds, entered);
    }

}