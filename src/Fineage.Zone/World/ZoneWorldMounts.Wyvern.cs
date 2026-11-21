using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Fineage.Zone.Net;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
  /// <summary>Mobius wyvern manager <c>RideWyvern</c> (castle-owning clan leader).</summary>
    string? TryWyvernRideFromManager(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var rules = MountRules;
        if (npc.MerchantCastleId <= 0)
            throw new InvalidOperationException($"wyvern manager npc {npc.NpcId} has no castle id (spawn map / merchant town).");
        var castle = _siege.Board.Find(npc.MerchantCastleId);
        if (castle == null)
            throw new InvalidOperationException($"wyvern manager castle missing from siege board: id {npc.MerchantCastleId}");
        var clan = ClanOf(player.ObjectId);
        if (clan == null || castle.OwnerClanId != clan.Id)
            return MountProtocol.Blocked;
        if (!player.ClanLeader)
            return MountProtocol.Blocked;
        if (player.Mount != null)
            return MountProtocol.Blocked;
        if (player.IsMounted)
            return MountProtocol.Blocked;
        if (PetEntityOf(player) is not { Summon.Pet: { } pet } summon)
            return MountProtocol.Blocked;
        if (pet.Template.MountKind != PetMountKind.Strider)
            return MountProtocol.Blocked;
        if (summon.Level < rules.WyvernMinStriderLevel)
            return MountProtocol.Blocked;
        if (player.Inventory == null || player.Inventory.CountOf(rules.WyvernCrystalItemId) < rules.WyvernCrystalCount)
            return MountProtocol.Blocked;
        if (!DisarmWeaponsForMount(player))
            return MountProtocol.Blocked;
        if (!ConsumeStackableLedger(session, player, npc.NpcId, rules.WyvernCrystalItemId, rules.WyvernCrystalCount,
                LedgerTxKind.ShopExchange))
            return MountProtocol.Blocked;

        MountWyvernFromManager(player, summon, pet);
        GrantTemporaryWyvernSkill(player);
        return null;
    }

    bool ConsumeStackableLedger(ZoneSession session, ZoneEntity player, int referenceNpcId, int itemId, int count, LedgerTxKind kind)
    {
        if (player.Inventory == null || count <= 0)
            return false;
        var item = player.Inventory.Items.FirstOrDefault(i => i.ItemId == itemId && !i.IsEquipped && i.Count >= count);
        if (item == null)
            return false;
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var move = BeginLedgerMove(kind, player.ObjectId, referenceNpcId);
        ObserveLedger(move, bag);
        if (!player.Inventory.TryReduce(item, count, out bool removedEntirely))
            return false;
        move.Post(bag, itemId, -count, PiecesOf([item]));
        move.Post(LedgerAccount.Of(SystemAccount.Destroyed), itemId, count);
        if (move.CheckCommit() != LedgerRejection.None)
            return false;
        move.Commit(LedgerHoldingsOf);
        var invUpdate = removedEntirely
            ? new InventoryUpdateMsg { Changes = [InventoryMessages.RemoveRow(item, -1)] }
            : InventoryMessages.Update(InventoryChange.Modify, item);
        NetBridge?.FanoutInventoryUpdate([session.ObjectId], invUpdate);
        SaveInventory(session, player.Inventory);
        return true;
    }

    void GrantTemporaryWyvernSkill(ZoneEntity player)
    {
        int skillId = MountRules.WyvernSkillId;
        player.KnownSkills[skillId] = 1;
        if (player.Session is { } session && NetBridge != null)
            NetBridge.FanoutSkillList([player.ObjectId],
                NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
    }

    void RevokeTemporaryWyvernSkill(ZoneEntity player)
    {
        int skillId = MountRules.WyvernSkillId;
        if (!player.KnownSkills.Remove(skillId))
            return;
        if (player.Session is { } && NetBridge != null)
            NetBridge.FanoutSkillList([player.ObjectId],
                NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
    }
}