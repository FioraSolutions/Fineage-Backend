using Fineage.Game;
using Fineage.Protocol;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Marks a live player dead or alive. Combat calls this when a player dies or revives.
    /// Players spawn alive with their saved HP. <see cref="ZoneEntity.CurHp"/> is not consulted:
    /// regen treats a low value as wounded, not dead.
    /// </summary>
    public bool TrySetPlayerDead(int objectId, bool dead)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(objectId, out var entity) || entity.Kind != ZoneEntityKind.Player)
                return false;
            entity.Dead = dead;
            return true;
        }
    }

    public async Task<string?> TryUseItemAsync(ZoneSession session, UseItemMsg request, CancellationToken ct)
    {
        var toSelf = new List<Pending>();
        var toOthers = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryUseItem(session, request.ObjectId, toSelf, toOthers);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        await FlushOthersAsync(toOthers, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryUnequipItemAsync(ZoneSession session, UnequipItemMsg request, CancellationToken ct)
    {
        var toSelf = new List<Pending>();
        var toOthers = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryUnequipItem(session, request.PaperdollSlot, toSelf, toOthers);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        await FlushOthersAsync(toOthers, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryGiveItemAsync(ZoneSession session, GiveItemMsg request, CancellationToken ct)
    {
        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryGiveItem(session, request, toSelf);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    string? TryUseItem(ZoneSession session, int objectId, List<Pending> toSelf, List<Pending> toOthers)
    {
        if (!TryLivePlayer(session, out var player, out var error))
            return error;

        var item = player.Inventory!.Find(objectId);
        if (item == null)
            return InventoryProtocol.NotOwned;
        if (!item.Template.Equippable)
            return InventoryProtocol.NotEquippable;
        // Unequip skips conditions. There is no cast state, so weapon changes are not deferred.
        if (!item.IsEquipped && Restriction(player, item) is { } denied)
            return denied;

        var before = PaperdollSnapshot(player.Inventory);
        try
        {
            if (item.IsEquipped)
                player.Inventory.Unequip(item);
            else
                player.Inventory.Equip(item);
        }
        catch (InvalidOperationException)
        {
            return InventoryProtocol.Blocked;
        }

        if (!PublishGear(session, player, before, toSelf, toOthers))
            return "bad_request";

        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"gear object={session.ObjectId} use={objectId} equipped={item.IsEquipped}");
        return null;
    }

    string? TryUnequipItem(ZoneSession session, int paperdollSlot, List<Pending> toSelf, List<Pending> toOthers)
    {
        if (!TryLivePlayer(session, out var player, out var error))
            return error;
        if (paperdollSlot < 0 || paperdollSlot > (int)PaperdollSlot.Hair2)
            return InventoryProtocol.BadSlot;

        var item = player.Inventory!.GetEquipped((PaperdollSlot)paperdollSlot);
        if (item == null)
            return InventoryProtocol.Empty;

        var before = PaperdollSnapshot(player.Inventory);
        player.Inventory.Unequip(item);
        if (!PublishGear(session, player, before, toSelf, toOthers))
            return "bad_request";

        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"gear object={session.ObjectId} unequip slot={paperdollSlot} item={item.ObjectId}");
        return null;
    }

    string? TryGiveItem(ZoneSession session, GiveItemMsg request, List<Pending> toSelf)
    {
        if (!IsLive(session))
            return "world";
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (request.ItemId <= 0 || request.Count <= 0 || request.Count > MaxGrantCount)
            return "bad_request";
        if (!_game.Items.TryGet(request.ItemId, out _))
            return "bad_request";

        var added = player.Inventory.Add(request.ItemId, request.Count);
        toSelf.Add(Pack(session, MsgType.InventoryUpdate, InventoryMessages.Update(InventoryChange.Add, added)));
        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"give object={session.ObjectId} item={request.ItemId} count={request.Count}");
        return null;
    }

    bool TryLivePlayer(ZoneSession session, out ZoneEntity player, out string? error)
    {
        player = null!;
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var entity) || entity.Inventory == null)
        {
            error = "world";
            return false;
        }

        if (entity.Dead)
        {
            error = InventoryProtocol.Dead;
            return false;
        }

        player = entity;
        error = null;
        return true;
    }

    bool PublishGear(
        ZoneSession session,
        ZoneEntity player,
        Dictionary<int, int> before,
        List<Pending> toSelf,
        List<Pending> toOthers)
    {
        var changed = PaperdollChanges(player.Inventory!, before);
        if (changed.Count == 0)
            return false;

        RefreshGradePenalty(player);
        toSelf.Add(Pack(session, MsgType.InventoryUpdate, InventoryMessages.Modify(changed)));
        toSelf.Add(Pack(session, MsgType.UserInfo, UserInfo(player)));
        byte[] body = MessagePackSerializer.Serialize(CharInfo(player));
        foreach (int id in player.Knownlist)
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
                toOthers.Add(new Pending(other.Session, MsgType.CharInfo, body));
        }

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
        player.WeaponGradePenalty = penalty.Weapon;
        player.ArmorGradePenalty = penalty.Armor;
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
