using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Siege;

namespace Fineage.Zone.World;

/// <summary>
/// Castle lord functions after the Mobius <c>CastleChamberlain</c> bypasses: castle info, tax rate (0–15%, leader or
/// CASTLE_TAXES, not during a siege), treasury deposit / withdraw (CASTLE_VAULT, no tax), door / wall upgrade
/// (ratio 2/3/5, CASTLE_MANAGE_FUNCTIONS, priced per Feature.ini, not during a siege), the Lord's Crown (lord only),
/// circlets (clan members, Fineage: Mobius hands them out through a quest-like dialog) and opening / closing gates
/// (CASTLE_OPEN_DOOR, not during a siege). Mercenaries, castle functions and the manor live elsewhere (TODO).
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius chamberlain npc ids per castle (index = castle id).</summary>
    static readonly int[] ChamberlainNpcIds = [0, 35100, 35142, 35184, 35226, 35274, 35316, 35363, 35509, 35555];

    /// <summary>Mobius Feature.ini door upgrade prices: [door type 1 outer / 2 inner / 3 wall][ratio 2 / 3 / 5].</summary>
    static long DoorUpgradePrice(int type, int ratio)
    {
        int step = ratio switch { 2 => 0, 3 => 1, 5 => 2, _ => -1 };
        if (step < 0)
            return -1;
        return type switch
        {
            1 => new long[] { 3_000_000, 4_000_000, 5_000_000 }[step],
            3 => new long[] { 1_600_000, 1_800_000, 2_000_000 }[step],
            _ => new long[] { 750_000, 900_000, 1_000_000 }[step]
        };
    }

    public Task<string?> TryCastleCommandAsync(ZoneSession session, CastleCommandNetMsg command, DateTimeOffset now,
        CancellationToken ct)
    {
        if (command == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        lock (_gate)
            return Task.FromResult(TryCastleCommand(session, command, now.ToUnixTimeMilliseconds()));
    }

    string? TryCastleCommand(ZoneSession session, CastleCommandNetMsg c, long now)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        var clan = ClanOf(player.ObjectId);
        int castleId = c.CastleId > 0 ? c.CastleId : clan != null ? _siege.CastleOwnedBy(clan.Id) : 0;
        var castle = castleId > 0 ? _siege.Find(castleId) : null;
        if (castle == null)
            return SiegeProtocol.Castle;
        if (c.Kind == CastleCommandKind.Info)
        {
            SendCastleInfo(session, castle);
            return null;
        }

        // Mobius CastleChamberlain.isOwner: the speaker's clan owns this castle, and the chamberlain is near when the
        // world has spawned it (split zones without the castle npcs skip the range check).
        if (clan == null || castle.OwnerClanId != clan.Id)
            return SiegeProtocol.Rights;
        if (!NearChamberlain(player, castleId))
            return SiegeProtocol.Range;
        string? error = c.Kind switch
        {
            CastleCommandKind.SetTax => SetCastleTax(session, player, clan, castle, c.IntArg1),
            CastleCommandKind.Withdraw => WithdrawTreasury(session, player, clan, castle, c.Amount),
            CastleCommandKind.Deposit => DepositTreasury(session, player, castle, c.Amount),
            CastleCommandKind.UpgradeDoor => UpgradeCastleDoor(session, player, clan, castle, c.IntArg1, c.IntArg2),
            CastleCommandKind.TakeCrown => TakeCastleItem(session, player, castle, SiegeProtocol.LordsCrownItemId,
                allowed: clan.LeaderId == player.ObjectId),
            CastleCommandKind.TakeCirclet => TakeCastleItem(session, player, castle, SiegeProtocol.CircletItemIds[castle.Id], allowed: true),
            CastleCommandKind.OperateDoor => OperateCastleDoor(session, player, clan, castle, c.IntArg1, c.IntArg2 != 0),
            _ => "bad_request"
        };
        if (error == null)
            SendCastleInfo(session, castle);
        return error;
    }

    bool NearChamberlain(ZoneEntity player, int castleId)
    {
        int npcId = castleId < ChamberlainNpcIds.Length ? ChamberlainNpcIds[castleId] : 0;
        bool any = false;
        foreach (var npc in _byId.Values)
        {
            if (npc.Kind != ZoneEntityKind.Npc || npc.NpcId != npcId)
                continue;
            any = true;
            if (ZoneCombat.Within(player.X, player.Y, npc.X, npc.Y, NpcInteractionRange))
                return true;
        }

        return !any;
    }

    const int NpcInteractionRange = 250;

    void SendCastleInfo(ZoneSession session, CastleSiege castle)
    {
        var clan = ClanOf(session.ObjectId);
        bool owner = clan != null && clan.Id == castle.OwnerClanId;
        bool vault = owner && (clan!.LeaderId == session.ObjectId || HasPriv(clan, session.ObjectId, PledgeProtocol.PrivCastleVault));
        var doors = new List<CastleDoorNet>();
        if (owner && CastleTable.Find(castle.Id) is { } data)
        {
            foreach (var row in data.Doors)
            {
                _castleDoors.TryGetValue(row.Id, out int objectId);
                _byId.TryGetValue(objectId, out var door);
                doors.Add(new CastleDoorNet
                {
                    DoorId = row.Id,
                    UpgradeType = row.UpgradeType,
                    UpgradeRatio = castle.DoorUpgrades.TryGetValue(row.Id, out int r) ? r : 1,
                    CurHp = door?.CurHp ?? 0,
                    MaxHp = door?.MaxHp ?? row.Hp,
                    Open = door != null && DoorOpen(door)
                });
            }
        }

        NetBridge?.FanoutCastleInfo([session.ObjectId], new CastleInfoNetMsg
        {
            CastleId = castle.Id,
            Name = castle.Name,
            OwnerClanId = castle.OwnerClanId,
            OwnerClanName = _clans.TryGetValue(castle.OwnerClanId, out var oc) ? oc.Name : "",
            TaxPercent = castle.TaxPercent,
            MaxTaxPercent = SiegeProtocol.MaxTaxPercent,
            Treasury = vault ? castle.Treasury : 0,
            SiegeUnixMs = castle.SiegeUnixMs,
            InProgress = castle.InProgress,
            Doors = [.. doors]
        });
    }

    string? SiegeBusy(ZoneSession session, CastleSiege castle)
    {
        if (!castle.InProgress)
            return null;
        SelfLine(session, SiegeProtocol.GatesDuringSiege, "You cannot do that while the castle is under siege.");
        return SiegeProtocol.Progress;
    }

    string? SetCastleTax(ZoneSession session, ZoneEntity player, ZoneClan clan, CastleSiege castle, int percent)
    {
        // Mobius: the leader or CASTLE_TAXES (our vault privilege bit) may set it; not during a siege.
        if (clan.LeaderId != player.ObjectId && !HasPriv(clan, player.ObjectId, PledgeProtocol.PrivCastleVault))
            return Rights(session);
        if (SiegeBusy(session, castle) is { } busy)
            return busy;
        if (percent < 0 || percent > SiegeProtocol.MaxTaxPercent)
            return SiegeProtocol.Tax;
        _siege.SetTax(castle.Id, percent);
        PersistSiege();
        ZoneLog.Info($"[castle] {castle.Name} tax={percent}% by {player.ObjectId}");
        return null;
    }

    string Rights(ZoneSession session)
    {
        SelfLine(session, SiegeProtocol.NotAuthorized, "You are not authorized to do that.");
        return SiegeProtocol.Rights;
    }

    string? WithdrawTreasury(ZoneSession session, ZoneEntity player, ZoneClan clan, CastleSiege castle, long amount)
    {
        if (clan.LeaderId != player.ObjectId && !HasPriv(clan, player.ObjectId, PledgeProtocol.PrivCastleVault))
            return Rights(session);
        var inventory = player.Inventory;
        if (inventory == null || amount <= 0 || amount > castle.Treasury ||
            (long)inventory.CountOf(PledgeProtocol.AdenaItemId) + amount > int.MaxValue)
            return SiegeProtocol.Adena;
        if (!_siege.AddTreasury(castle.Id, -amount))
            return SiegeProtocol.Adena;
        GiveItem(session, inventory, PledgeProtocol.AdenaItemId, (int)amount);
        PersistSiege();
        return null;
    }

    string? DepositTreasury(ZoneSession session, ZoneEntity player, CastleSiege castle, long amount)
    {
        var inventory = player.Inventory;
        if (inventory == null || amount <= 0 || amount > int.MaxValue || inventory.CountOf(PledgeProtocol.AdenaItemId) < amount ||
            castle.Treasury + amount > SiegeProtocolLimits.MaxTreasury)
        {
            SelfLine(session, SiegeProtocol.NotEnoughAdena, "You do not have enough adena.");
            return SiegeProtocol.Adena;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        if (!Take(inventory, PledgeProtocol.AdenaItemId, (int)amount, changes))
            return SiegeProtocol.Adena;
        _siege.AddTreasury(castle.Id, amount);
        FanInventory(session, Collapse(changes));
        SaveInventory(session, inventory);
        PersistSiege();
        return null;
    }

    void GiveItem(ZoneSession session, PlayerInventory inventory, int itemId, int count)
    {
        int before = inventory.CountOf(itemId);
        var stack = inventory.Add(itemId, count);
        NetBridge?.FanoutInventoryUpdate([session.ObjectId],
            InventoryMessages.Update(before == 0 ? InventoryChange.Add : InventoryChange.Modify, stack));
        SaveInventory(session, inventory);
    }

    string? UpgradeCastleDoor(ZoneSession session, ZoneEntity player, ZoneClan clan, CastleSiege castle, int doorId, int ratio)
    {
        if (clan.LeaderId != player.ObjectId && !HasPriv(clan, player.ObjectId, PledgeProtocol.PrivCastleManageFunctions))
            return Rights(session);
        if (SiegeBusy(session, castle) is { } busy)
            return busy;
        var row = CastleTable.Find(castle.Id)?.Doors.FirstOrDefault(d => d.Id == doorId);
        if (row == null)
            return SiegeProtocol.Door;
        long price = DoorUpgradePrice(row.UpgradeType, ratio);
        int current = castle.DoorUpgrades.TryGetValue(doorId, out int r) ? r : 1;
        if (price < 0 || ratio <= current)
            return SiegeProtocol.UpgradeLevel;
        var inventory = player.Inventory;
        var changes = new List<InventoryUpdateEntryMsg>();
        if (inventory == null || inventory.CountOf(PledgeProtocol.AdenaItemId) < price ||
            !Take(inventory, PledgeProtocol.AdenaItemId, (int)price, changes))
        {
            SelfLine(session, SiegeProtocol.NotEnoughAdena, "You do not have enough adena.");
            return SiegeProtocol.Adena;
        }

        FanInventory(session, Collapse(changes));
        SaveInventory(session, inventory);
        castle.DoorUpgrades[doorId] = ratio;
        if (_castleDoors.TryGetValue(doorId, out int objectId) && _byId.TryGetValue(objectId, out var door) && !door.Dead)
        {
            door.MaxHp = door.SiegeNpc!.BaseHp * ratio;
            door.CurHp = door.MaxHp;
            FanoutVitals(door, StatusFor(door));
            FanoutDoorStatus(door);
        }

        PersistSiege();
        return null;
    }

    string? TakeCastleItem(ZoneSession session, ZoneEntity player, CastleSiege castle, int itemId, bool allowed)
    {
        if (!allowed)
            return Rights(session);
        if (SiegeBusy(session, castle) is { } busy)
            return busy;
        var inventory = player.Inventory;
        if (inventory == null || itemId <= 0)
            return SiegeProtocol.Inventory;
        if (inventory.CountOf(itemId) > 0)
            return SiegeProtocol.HasItem;
        if (_game.Items.TryGet(itemId, out _))
            GiveItem(session, inventory, itemId, 1);
        else
            return SiegeProtocol.Inventory;
        return null;
    }

    string? OperateCastleDoor(ZoneSession session, ZoneEntity player, ZoneClan clan, CastleSiege castle, int doorId, bool open)
    {
        if (clan.LeaderId != player.ObjectId && !HasPriv(clan, player.ObjectId, PledgeProtocol.PrivCastleOpenDoor))
            return Rights(session);
        if (SiegeBusy(session, castle) is { } busy)
            return busy;
        if (CastleTable.Find(castle.Id)?.Doors.FirstOrDefault(d => d.Id == doorId) is not { Wall: false } ||
            !_castleDoors.TryGetValue(doorId, out int objectId) || !_byId.TryGetValue(objectId, out var door) || door.Dead)
            return SiegeProtocol.Door;
        SetGeoDoorOpen(doorId, open);
        FanoutDoorStatus(door);
        return null;
    }

    /// <summary>Mobius <c>Castle.removeOwner</c> / <c>CastleManager.removeCirclet</c>: online members lose the circlet (and the lord the crown).</summary>
    void RemoveCirclets(ZoneClan clan, int castleId)
    {
        int circlet = castleId > 0 && castleId < SiegeProtocol.CircletItemIds.Length ? SiegeProtocol.CircletItemIds[castleId] : 0;
        foreach (var session in OnlineMembers(clan))
        {
            if (!_byId.TryGetValue(session.ObjectId, out var member) || member.Inventory is not { } inventory)
                continue;
            var changes = new List<InventoryUpdateEntryMsg>();
            if (circlet > 0 && inventory.CountOf(circlet) > 0)
                Take(inventory, circlet, inventory.CountOf(circlet), changes);
            if (member.ObjectId == clan.LeaderId && inventory.CountOf(SiegeProtocol.LordsCrownItemId) > 0)
                Take(inventory, SiegeProtocol.LordsCrownItemId, inventory.CountOf(SiegeProtocol.LordsCrownItemId), changes);
            if (changes.Count == 0)
                continue;
            FanInventory(session, Collapse(changes));
            SaveInventory(session, inventory);
        }
    }

    const int AdenCastleId = 5, GoddardCastleId = 7, RuneCastleId = 8, SchuttgartCastleId = 9;

    /// <summary>
    /// Mobius <c>Castle.addToTreasury</c>: shop tax lands in the castle treasury. Goddard and Schuttgart pay Rune's rate
    /// to Rune, the southern castles pay Aden's rate to Aden (when that castle has an owner); Aden and Rune keep all.
    /// </summary>
    void CreditCastleTax(int castleId, long amount)
    {
        if (castleId <= 0 || amount <= 0)
            return;
        var castle = _siege.Find(castleId);
        if (castle == null || castle.OwnerClanId <= 0)
            return;
        int overlordId = castleId switch
        {
            AdenCastleId or RuneCastleId => 0,
            GoddardCastleId or SchuttgartCastleId => RuneCastleId,
            _ => AdenCastleId
        };
        if (overlordId > 0 && _siege.Find(overlordId) is { OwnerClanId: > 0 } overlord)
        {
            long cut = (long)(amount * (overlord.TaxPercent / 100d));
            if (cut > 0 && _siege.AddTreasury(overlordId, cut))
                amount -= cut;
        }

        if (amount > 0)
            _siege.AddTreasury(castleId, amount);
        PersistSiege();
    }

    /// <summary>Test hook: castle as the board holds it.</summary>
    internal CastleSiege? CastleForTests(int castleId)
    {
        lock (_gate)
            return _siege.Find(castleId);
    }
}
