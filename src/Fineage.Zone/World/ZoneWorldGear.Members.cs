using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    bool PublishGear(
        ZoneSession session,
        ZoneEntity player,
        Dictionary<int, int> before)
    {
        var changed = PaperdollChanges(player.Inventory!, before);
        if (changed.Count == 0)
            return false;

        RefreshGradePenalty(player);
        RefreshGearSkills(player);
        RefreshRunSpeedAndFanout(player);
        var invUpdate = InventoryMessages.Modify(changed);
        // UserInfo (paperdoll + CurrWeight/MaxWeight) is sent from SaveInventory →
        // RefreshWeightPenaltyAfterInventoryChange, which every equip/unequip path calls next.
        if (NetBridge != null)
            NetBridge.FanoutInventoryUpdate([session.ObjectId], invUpdate);
        List<int>? netPeers = null;
        foreach (int id in player.Knownlist)
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
            {
                (netPeers ??= []).Add(id);
            }
        }
        // Mobius broadcastUserInfo: peers get a fresh CharInfo (Net EntitySpawn with paperdoll) after gear changes.
        if (netPeers != null && NetBridge != null)
            NetBridge.FanoutCharInfo(netPeers, player.ObjectId);

        return true;
    }

    static Dictionary<int, int> PaperdollSnapshot(PlayerInventory inventory)
    {
        var slots = new Dictionary<int, int>(inventory.Items.Count);
        foreach (var item in inventory.Items)
            slots.Add(item.ObjectId, item.EquippedSlot is { } slot ? (int)slot : -1);
        return slots;
    }

    static List<ItemInstance> PaperdollChanges(PlayerInventory inventory, Dictionary<int, int> before)
    {
        var changed = new List<ItemInstance>();
        foreach (var item in inventory.Items)
        {
            int slot = item.EquippedSlot is { } worn ? (int)worn : -1;
            if (!before.TryGetValue(item.ObjectId, out int prior) || prior != slot)
                changed.Add(item);
        }

        changed.Sort(static (a, b) => a.ObjectId.CompareTo(b.ObjectId));
        return changed;
    }

    /// <summary>
    /// Clan, hero, castle, and PvP standing used by equip conditions.
    /// Defaults are an ordinary character: not a hero, not in a clan, not PvP flagged.
    /// </summary>
    public bool TrySetEquipStanding(int objectId, PlayerEquipStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);
        lock (_gate)
        {
            if (!_byId.TryGetValue(objectId, out var entity) || entity.Kind != ZoneEntityKind.Player)
                return false;
            entity.Hero = standing.Hero;
            entity.InClan = standing.InClan;
            entity.ClanLeader = standing.ClanLeader;
            entity.PledgeClass = standing.PledgeClass;
            entity.CastleId = standing.CastleId;
            entity.HideoutId = standing.HideoutId;
            entity.PvpFlagged = standing.PvpFlagged;
            entity.PkKills = standing.PkKills;
            entity.ExpertiseBonus = standing.ExpertiseBonus;
            RefreshGradePenalty(entity);
            return true;
        }
    }

    void RefreshGradePenalty(ZoneEntity player)
    {
        if (player.Inventory == null)
            return;
        var penalty = GradePenalty.Compute(
            player.Inventory.Items,
            Expertise.ForLevel(player.Level),
            player.ExpertiseBonus,
            _expertisePenalty);
        bool changed = penalty.Weapon != player.WeaponGradePenalty || penalty.Armor != player.ArmorGradePenalty;
        player.WeaponGradePenalty = penalty.Weapon;
        player.ArmorGradePenalty = penalty.Armor;
        player.GradePenaltyLevel = penalty.SkillLevel;
        // Mobius refreshExpertisePenalty ends with sendPacket(new EtcStatusUpdate(this)). Without it the Net client
        // keeps the previous grade-penalty flag, so the icon disagrees with the penalty UserInfo now reports.
        if (changed)
            FanoutNetEtcStatus(player);
    }

    static string? Restriction(ZoneEntity player, ItemInstance item)
    {
        var deny = item.Template.Equip?.Deny(Actor(player));
        return deny == null ? null : EquipDenyCode(deny.Value);
    }

    static EquipActor Actor(ZoneEntity player) => new(
        player.ClassId,
        player.Race,
        player.Sex,
        player.Level,
        player.Hero,
        player.InClan,
        player.ClanLeader,
        player.PledgeClass,
        player.CastleId,
        player.HideoutId,
        player.PvpFlagged,
        player.PkKills);

    static string EquipDenyCode(EquipDeny deny) => deny switch
    {
        EquipDeny.Class => InventoryProtocol.ClassRestricted,
        EquipDeny.Race => InventoryProtocol.RaceRestricted,
        EquipDeny.Sex => InventoryProtocol.SexRestricted,
        EquipDeny.Hero => InventoryProtocol.HeroRestricted,
        EquipDeny.ClanLeader => InventoryProtocol.ClanLeaderRestricted,
        EquipDeny.Pledge => InventoryProtocol.PledgeRestricted,
        EquipDeny.Pvp => InventoryProtocol.PvpRestricted,
        EquipDeny.Level => InventoryProtocol.LevelRestricted,
        EquipDeny.Castle => InventoryProtocol.CastleRestricted,
        EquipDeny.ClanHall => InventoryProtocol.ClanHallRestricted,
        EquipDeny.Pk => InventoryProtocol.PkRestricted,
        EquipDeny.Grade => InventoryProtocol.GradeRestricted,
        EquipDeny.Condition => InventoryProtocol.ConditionRestricted,
        _ => InventoryProtocol.ConditionRestricted
    };
}
