using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Manor;
using Fineage.Zone.Simulation;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

/// <summary>
/// Castle manor after Mobius <c>CastleManorManager</c> and the manor packets: manor managers sell this period's seeds and
/// buy crops for reward items, the castle lord sets the next period's seed sales and crop purchases, and the day cycle
/// (20:00 maintenance, 20:06 editable, 04:30 approved) rolls periods, pays from / back into the treasury and matures the
/// bought crops in the owner clan warehouse.
/// </summary>
public sealed partial class ZoneWorld
{
    ManorActor _manor = null!;
    Random _manorRandom = null!;

    internal ManorBoard Manor => _manor.Board;

    void InitManor(ManorSettings? settings, Random? random)
    {
        _manorRandom = random ?? GlobalStream(RandomPurpose.Manor);
        _manor = new ManorActor(new ManorBoard(ManorData.Default, settings ?? ManorSettings.FromEnvironment()));
        if (_accounts != null)
            _manor.Board.Import(_accounts.ListManorProduction(), _accounts.ListManorProcure());
        if (_manor.Board.Settings.Enabled)
        {
            long now = NowUnixMs();
            _manor.Board.Mode = ManorRules.ModeAt(now, _manor.Board.Settings.UtcOffsetMinutes);
            _manor.Board.NextChangeMs = ManorRules.NextChange(_manor.Board.Mode, now, _manor.Board.Settings.UtcOffsetMinutes);
        }
    }

    bool PersistManor() =>
        _accounts == null || _accounts.SaveManor(_manor.Board.ExportProduction(), _manor.Board.ExportProcure());

    /// <summary>Mobius <c>CastleManorManager.changeMode</c>: what a mode change does to every castle's manor.</summary>
    int ApplyManorModes(IReadOnlyList<ManorModeChange> changes)
    {
        foreach (var change in changes)
            ApplyManorMode(change);
        return changes.Count;
    }

    void ApplyManorMode(ManorModeChange change)
    {
        switch (change.To)
        {
            case ManorProtocol.ModeMaintenance:
                foreach (var castle in _siege.Board.Castles)
                    CloseManorPeriod(castle);
                PersistManor();
                PersistSiege();
                break;
            case ManorProtocol.ModeModifiable:
                foreach (var castle in _siege.Board.Castles)
                {
                    if (castle.OwnerClanId > 0 && _clan.TryGet(castle.OwnerClanId, out var owner))
                        LeaderLine(owner, ManorProtocol.ManorUpdated, "The manor information has been updated.");
                }

                break;
            case ManorProtocol.ModeApproved:
                foreach (var castle in _siege.Board.Castles)
                    ApproveManorPeriod(castle);
                PersistManor();
                PersistSiege();
                break;
        }

        ZoneLog.Info($"[manor] mode={_manor.Board.Mode} next={_manor.Board.NextChangeMs}");
    }

    void LeaderLine(ZoneClan clan, int id, string text)
    {
        if (OnlineMembers(clan).FirstOrDefault(s => s.ObjectId == clan.LeaderId) is { } leader)
            SelfLine(leader, id, text);
    }

    /// <summary>
    /// Mobius APPROVED → MAINTENANCE per owned castle: 90% of the bought crops mature into the clan warehouse, unspent
    /// procure money returns to the treasury, next period becomes current, and the new next period starts from the same
    /// settings (full amounts) when the treasury can pay for them, or empty when it cannot.
    /// </summary>
    void CloseManorPeriod(Siege.CastleSiege castle)
    {
        if (castle.OwnerClanId <= 0 || !_clan.TryGet(castle.OwnerClanId, out var owner))
            return;
        int castleId = castle.Id;
        var warehouse = ClanWarehouseOf(owner);
        bool warehouseChanged = false;
        foreach (var crop in _manor.Board.Procure(castleId, next: false))
        {
            if (crop.StartAmount <= 0)
                continue;
            if (crop.StartAmount != crop.Amount && _manor.Board.Data.SeedByCrop(crop.CropId) is { } seed && warehouse != null)
            {
                int matured = (int)((crop.StartAmount - crop.Amount) * ManorProtocol.MatureShare);
                if (matured < 1 && _manorRandom.Next(99) < 90)
                    matured = 1;
                if (matured > 0 && _game.Items.TryGet(seed.MatureId, out _))
                {
                    warehouse.Add(seed.MatureId, matured, 0, out _);
                    warehouseChanged = true;
                }
            }

            if (crop.Amount > 0)
                _siege.Board.AddTreasury(castleId, crop.Amount * crop.Price);
        }

        if (warehouseChanged)
            SaveClan(owner);

        _manor.Board.SetProduction(castleId, false, _manor.Board.Production(castleId, next: true));
        _manor.Board.SetProcure(castleId, false, _manor.Board.Procure(castleId, next: true));
        if (castle.Treasury < _manor.Board.ManorCost(castleId, next: false))
        {
            _manor.Board.SetProduction(castleId, true, []);
            _manor.Board.SetProcure(castleId, true, []);
        }
        else
        {
            _manor.Board.SetProduction(castleId, true,
                _manor.Board.Production(castleId, false).Select(p => new ManorProduct(p.SeedId, p.StartAmount, p.Price, p.StartAmount)).ToList());
            _manor.Board.SetProcure(castleId, true,
                _manor.Board.Procure(castleId, false).Select(p => new ManorProcure(p.CropId, p.StartAmount, p.RewardType, p.StartAmount, p.Price)).ToList());
        }
    }

    /// <summary>
    /// Mobius MODIFIABLE → APPROVED per owned castle: the next period is paid from the treasury, or dropped (935 to the
    /// lord) when the treasury cannot pay or the clan warehouse has no room for the new mature crops.
    /// </summary>
    void ApproveManorPeriod(Siege.CastleSiege castle)
    {
        if (castle.OwnerClanId <= 0 || !_clan.TryGet(castle.OwnerClanId, out var owner))
            return;
        int castleId = castle.Id;
        var warehouse = ClanWarehouseOf(owner);
        int slots = 0;
        foreach (var crop in _manor.Board.Procure(castleId, next: true))
        {
            if (crop.StartAmount > 0 && _manor.Board.Data.SeedByCrop(crop.CropId) is { } seed && warehouse?.FindByItemId(seed.MatureId) == null)
                slots++;
        }

        long cost = _manor.Board.ManorCost(castleId, next: true);
        bool room = warehouse == null || warehouse.Items.Count + slots <= PledgeProtocol.ClanWarehouseSlots;
        // Fineage: Mobius drops the period only when both checks fail (&&); either one failing would overdraw the
        // treasury or lose the crops, so either one stops the manor here.
        if (!room || castle.Treasury < cost)
        {
            _manor.Board.SetProduction(castleId, true, []);
            _manor.Board.SetProcure(castleId, true, []);
            LeaderLine(owner, ManorProtocol.ManorNotOperating, "The amount is not sufficient and so the manor is not in operation.");
            return;
        }

        if (cost > 0)
            _siege.Board.AddTreasury(castleId, -cost);
    }

    /// <summary>Mobius <c>Castle.updateOwnerInDB(null)</c> → <c>resetManorData</c>: an ownerless castle has no manor.</summary>
    void ResetManorIfOwnerless(int castleId)
    {
        if (castleId <= 0 || _siege.Board.Find(castleId) is not { OwnerClanId: 0 })
            return;
        _manor.Board.Reset(castleId);
        PersistManor();
    }

    public Task<string?> TryManorCommandAsync(ZoneSession session, ManorCommandNetMsg command, CancellationToken ct)
    {
        if (command == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        return Task.FromResult(WithDeferredInventoryPersist(() => TryManorCommand(session, command)));
    }

    string? TryManorCommand(ZoneSession session, ManorCommandNetMsg c)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        if (_manor.Board.Disabled)
            return ManorProtocol.Disabled;
        int castleId = c.CastleId;
        if (c.Kind != ManorCommandKind.DefaultInfo && c.Kind != ManorCommandKind.SellList && c.Kind != ManorCommandKind.SellCrops &&
            _siege.Board.Find(castleId) == null)
            return ManorProtocol.Castle;
        if (c.Kind is ManorCommandKind.BuyList or ManorCommandKind.BuySeeds or ManorCommandKind.SellList or ManorCommandKind.SellCrops
            && _manor.Board.UnderMaintenance)
        {
            SelfLine(session, ManorProtocol.UnderMaintenance, "The manor system is currently under maintenance.");
            return ManorProtocol.Maintenance;
        }

        return c.Kind switch
        {
            ManorCommandKind.SeedInfo => SendSeedInfo(session, castleId, c.Next, ManorCommandKind.SeedInfo),
            ManorCommandKind.CropInfo => SendCropInfo(session, castleId, c.Next, ManorCommandKind.CropInfo),
            ManorCommandKind.DefaultInfo => SendDefaultInfo(session),
            ManorCommandKind.BuyList => SendBuyList(session, player, c),
            ManorCommandKind.BuySeeds => BuySeeds(session, player, c),
            ManorCommandKind.SellList => SendSellList(session, player, c),
            ManorCommandKind.SellCrops => SellCrops(session, player, c),
            ManorCommandKind.SeedSetting => LordPage(session, player, castleId, seeds: true),
            ManorCommandKind.CropSetting => LordPage(session, player, castleId, seeds: false),
            ManorCommandKind.SetSeeds => SetNextSeeds(session, player, castleId, c.Rows),
            ManorCommandKind.SetCrops => SetNextCrops(session, player, castleId, c.Rows),
            _ => "bad_request"
        };
    }

    /// <summary>
    /// The manor manager the player talks to (Mobius <c>getLastFolkNPC</c> + <c>canInteract</c>): its castle, or 0. When the
    /// world has not spawned any manager for <paramref name="castleId"/> (split zones, tests) the castle itself stands in.
    /// </summary>
    int ManagerCastle(ZoneEntity player, int npcObjectId, int castleId)
    {
        if (npcObjectId > 0)
        {
            if (!_byId.TryGetValue(npcObjectId, out var npc) || npc.Kind != ZoneEntityKind.Npc)
                return 0;
            int managed = _manor.Board.Data.ManagerCastle(npc.NpcId);
            if (managed <= 0 || !ZoneCombat.Within(player.X, player.Y, npc.X, npc.Y, NpcInteractionRange))
                return 0;
            return managed;
        }

        bool spawned = _byId.Values.Any(e => e.Kind == ZoneEntityKind.Npc && _manor.Board.Data.ManagerCastle(e.NpcId) == castleId);
        return spawned ? 0 : castleId;
    }

    ManorInfoNetMsg ManorHeader(int view, int castleId, bool next) => new()
    {
        View = view,
        CastleId = castleId,
        Next = next,
        Mode = _manor.Board.Mode,
        NextChangeUnixMs = _manor.Board.NextChangeMs
    };

    void SendManor(ZoneSession session, ManorInfoNetMsg info) => NetBridge?.FanoutManorInfo([session.ObjectId], info);

    ManorSeedNet SeedRow(ManorSeed s, ManorProduct? row, ManorProduct? current = null) => new()
    {
        SeedId = s.SeedId,
        CropId = s.CropId,
        Level = s.Level,
        Reward1 = s.Reward1,
        Reward2 = s.Reward2,
        Alternative = s.Alternative,
        Limit = _manor.Board.SeedLimit(s),
        ReferencePrice = s.SeedPrice,
        MinPrice = s.SeedMinPrice,
        MaxPrice = s.SeedMaxPrice,
        Amount = row?.Amount ?? 0,
        StartAmount = row?.StartAmount ?? 0,
        Price = row?.Price ?? 0,
        CurrentStartAmount = current?.StartAmount ?? 0,
        CurrentPrice = current?.Price ?? 0
    };

    ManorCropNet CropRow(ManorSeed s, int manorId, ManorProcure? row, ManorProcure? current = null) => new()
    {
        CropId = s.CropId,
        SeedId = s.SeedId,
        Level = s.Level,
        Reward1 = s.Reward1,
        Reward2 = s.Reward2,
        Limit = _manor.Board.CropLimit(s),
        ReferencePrice = s.CropPrice,
        MinPrice = s.CropMinPrice,
        MaxPrice = s.CropMaxPrice,
        ManorId = manorId,
        Amount = row?.Amount ?? 0,
        StartAmount = row?.StartAmount ?? 0,
        Price = row?.Price ?? 0,
        RewardType = row?.RewardType ?? 0,
        CurrentStartAmount = current?.StartAmount ?? 0,
        CurrentPrice = current?.Price ?? 0,
        CurrentRewardType = current?.RewardType ?? 0
    };

    /// <summary>Mobius ExShowSeedInfo: the castle's seed sale rows for this or the next period.</summary>
    string? SendSeedInfo(ZoneSession session, int castleId, bool next, int view)
    {
        var info = ManorHeader(view, castleId, next);
        info.Seeds = _manor.Board.Production(castleId, next)
            .Select(p => _manor.Board.Data.Seed(p.SeedId) is { } s ? SeedRow(s, p) : null)
            .Where(r => r != null).Select(r => r!).ToArray();
        SendManor(session, info);
        return null;
    }

    /// <summary>Mobius ExShowCropInfo: the castle's crop purchase rows.</summary>
    string? SendCropInfo(ZoneSession session, int castleId, bool next, int view)
    {
        var info = ManorHeader(view, castleId, next);
        info.Crops = _manor.Board.Procure(castleId, next)
            .Select(p => _manor.Board.Data.SeedByCrop(p.CropId, castleId) is { } s ? CropRow(s, castleId, p) : null)
            .Where(r => r != null).Select(r => r!).ToArray();
        SendManor(session, info);
        return null;
    }

    /// <summary>Mobius ExShowManorDefaultInfo: every crop, its level, rewards and reference prices.</summary>
    string? SendDefaultInfo(ZoneSession session)
    {
        var info = ManorHeader(ManorCommandKind.DefaultInfo, 0, false);
        info.Crops = _manor.Board.Data.Crops().Select(s => CropRow(s, 0, null)).ToArray();
        SendManor(session, info);
        return null;
    }

    /// <summary>Mobius ManorManager request 1 → BuyListSeed: only the manager of that castle sells its seeds (1605).</summary>
    string? SendBuyList(ZoneSession session, ZoneEntity player, ManorCommandNetMsg c)
    {
        int managed = ManagerCastle(player, c.NpcObjectId, c.CastleId);
        if (managed <= 0)
            return ManorProtocol.Manager;
        if (managed != c.CastleId)
        {
            string name = _siege.Board.Find(managed)?.Name ?? "";
            SelfLine(session, ManorProtocol.OnlyThisManorSeeds, $"Here you can buy only seeds of {name} Manor.", name);
            return ManorProtocol.Manager;
        }

        var info = ManorHeader(ManorCommandKind.BuyList, c.CastleId, false);
        info.Adena = player.Inventory?.CountOf(PledgeProtocol.AdenaItemId) ?? 0;
        info.Seeds = _manor.Board.Production(c.CastleId, false).Where(p => p.Amount > 0 && p.Price > 0)
            .Select(p => _manor.Board.Data.Seed(p.SeedId) is { } s ? SeedRow(s, p) : null)
            .Where(r => r != null).Select(r => r!).ToArray();
        SendManor(session, info);
        return null;
    }

    bool FitsBag(ZoneEntity player, IEnumerable<(int ItemId, long Count)> incoming, out bool weight)
    {
        weight = false;
        var inventory = player.Inventory!;
        int slots = 0;
        long load = 0;
        foreach (var (itemId, count) in incoming)
        {
            if (!_game.Items.TryGet(itemId, out var t))
                return false;
            load += (long)_weights.Of(itemId) * count;
            if (!t.Stackable)
                slots += (int)Math.Min(count, int.MaxValue);
            else if (inventory.FindByItemId(itemId) == null)
                slots++;
        }

        int carried = _weights.Load(inventory.Items.Select(i => (i.ItemId, i.Count)));
        if (carried + load > MaxLoadOf(player))
        {
            weight = true;
            return false;
        }

        return inventory.Items.Count + slots <= WarehouseRules.InventorySlots(player.Race);
    }

    string BagRefusal(ZoneSession session, bool weight)
    {
        if (weight)
            Notice(session, NpcServiceProtocol.WeightLimit, NpcServiceProtocol.WeightLimitText);
        else
            Notice(session, NpcServiceProtocol.InventoryFull, NpcServiceProtocol.InventoryFullText);
        return ManorProtocol.Inventory;
    }

    /// <summary>
    /// Mobius <c>RequestBuySeed</c>: every row must be on sale with enough left; weight, slots and adena are checked for
    /// the whole order; the adena goes into the treasury without tax and the buyer sees 672.
    /// </summary>
    string? BuySeeds(ZoneSession session, ZoneEntity player, ManorCommandNetMsg c)
    {
        int managed = ManagerCastle(player, c.NpcObjectId, c.CastleId);
        if (managed <= 0 || managed != c.CastleId)
            return ManorProtocol.Manager;
        var inventory = player.Inventory;
        if (inventory == null || c.Rows.Length == 0)
            return "bad_request";
        long total = 0;
        var order = new List<(ManorProduct Product, long Count)>();
        foreach (var row in c.Rows)
        {
            var product = _manor.Board.Product(c.CastleId, row.ItemId, next: false);
            if (product == null || product.Price <= 0 || row.Count <= 0 || product.Amount < row.Count ||
                order.Any(o => o.Product.SeedId == row.ItemId) || int.MaxValue / row.Count < product.Price)
                return ManorProtocol.Product;
            total += product.Price * row.Count;
            if (total > int.MaxValue)
                return ManorProtocol.Adena;
            order.Add((product, row.Count));
        }

        if (!FitsBag(player, order.Select(o => (o.Product.SeedId, o.Count)), out bool weight))
            return BagRefusal(session, weight);
        if (inventory.CountOf(PledgeProtocol.AdenaItemId) < total)
        {
            Notice(session, ManorProtocol.NotEnoughAdena, "You do not have enough adena.");
            return ManorProtocol.Adena;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        if (!Take(inventory, PledgeProtocol.AdenaItemId, (int)total, changes))
            return ManorProtocol.Adena;
        foreach (var (product, count) in order)
        {
            product.TryDecrease(count);
            int before = inventory.CountOf(product.SeedId);
            var stack = inventory.Add(product.SeedId, (int)count);
            changes.Add(InventoryMessages.ChangeRow(before == 0 ? InventoryChange.Add : InventoryChange.Modify, stack));
        }

        FanInventory(session, Collapse(changes));
        SaveInventory(session, inventory);
        _siege.Board.AddTreasury(c.CastleId, total);
        SelfLine(session, ManorProtocol.AdenaDisappeared, $"{total} adena disappeared.", total.ToString());
        PersistManor();
        PersistSiege();
        ZoneLog.Info($"[manor] buy seeds castle={c.CastleId} by={player.ObjectId} adena={total}");
        return SendBuyList(session, player, c);
    }

    /// <summary>
    /// Mobius ExShowSellCropList: each crop in the bag with every castle that buys it this period (the selling manager's
    /// castle buys without the 5% fee).
    /// </summary>
    string? SendSellList(ZoneSession session, ZoneEntity player, ManorCommandNetMsg c)
    {
        int managed = ManagerCastle(player, c.NpcObjectId, c.CastleId);
        if (managed <= 0)
            return ManorProtocol.Manager;
        var inventory = player.Inventory;
        if (inventory == null)
            return "world";
        var info = ManorHeader(ManorCommandKind.SellList, managed, false);
        info.Adena = inventory.CountOf(PledgeProtocol.AdenaItemId);
        var rows = new List<ManorCropNet>();
        foreach (var item in inventory.Items.Where(i => !i.IsEquipped && _manor.Board.Data.IsCrop(i.ItemId)))
        {
            foreach (var castle in _siege.Board.Castles)
            {
                var procure = _manor.Board.CropProcure(castle.Id, item.ItemId, next: false);
                if (procure == null || procure.Amount <= 0 || _manor.Board.Data.SeedByCrop(item.ItemId, castle.Id) is not { } seed)
                    continue;
                var row = CropRow(seed, castle.Id, procure);
                row.InventoryObjectId = item.ObjectId;
                row.InventoryCount = item.Count;
                rows.Add(row);
            }
        }

        info.Crops = [.. rows];
        SendManor(session, info);
        return null;
    }

    /// <summary>
    /// Mobius <c>RequestProcureCropList</c>: a crop sells to the procure list of row.ManorId; the reward is price ×
    /// count / reward reference price items (1491 when that is below one), with a 5% adena fee for another castle.
    /// </summary>
    string? SellCrops(ZoneSession session, ZoneEntity player, ManorCommandNetMsg c)
    {
        int managed = ManagerCastle(player, c.NpcObjectId, c.CastleId);
        if (managed <= 0)
            return ManorProtocol.Manager;
        var inventory = player.Inventory;
        if (inventory == null || c.Rows.Length == 0)
            return "bad_request";

        var plan = new List<(ManorRowNet Row, ManorProcure Procure, int RewardId)>();
        foreach (var row in c.Rows)
        {
            var item = inventory.Find(row.ObjectId);
            if (item == null || item.ItemId != row.ItemId || row.Count <= 0 || item.Count < row.Count || item.IsEquipped)
                return ManorProtocol.Product;
            var procure = _manor.Board.CropProcure(row.ManorId, row.ItemId, next: false);
            var seed = _manor.Board.Data.SeedByCrop(row.ItemId, row.ManorId);
            if (procure == null || seed == null || procure.Amount < row.Count)
                return ManorProtocol.Product;
            plan.Add((row, procure, seed.Reward(procure.RewardType)));
        }

        if (!FitsBag(player, plan.Select(p => (p.RewardId, p.Row.Count)), out bool weight))
            return BagRefusal(session, weight);

        var changes = new List<InventoryUpdateEntryMsg>();
        bool sold = false;
        foreach (var (row, procure, rewardId) in plan)
        {
            string cropName = _game.Items.TryGet(row.ItemId, out var ct) ? ct.Name : row.ItemId.ToString();
            int rewardPrice = _game.Items.TryGet(rewardId, out var rt) ? rt.ReferencePrice : 0;
            if (rewardPrice <= 0)
                continue;
            long price = row.Count * procure.Price;
            long rewardCount = price / rewardPrice;
            if (rewardCount < 1)
            {
                SelfLine(session, ManorProtocol.FailedCropTrade, $"Failed in trading {row.Count} of crop {cropName}.", cropName, row.Count.ToString());
                continue;
            }

            int fee = managed == row.ManorId ? 0 : (int)(price * ManorProtocol.ForeignCastleFee);
            if (fee > 0 && inventory.CountOf(PledgeProtocol.AdenaItemId) < fee)
            {
                SelfLine(session, ManorProtocol.FailedCropTrade, $"Failed in trading {row.Count} of crop {cropName}.", cropName, row.Count.ToString());
                Notice(session, ManorProtocol.NotEnoughAdena, "You do not have enough adena.");
                continue;
            }

            if (!procure.TryDecrease(row.Count))
                continue;
            if (fee > 0)
                Take(inventory, PledgeProtocol.AdenaItemId, fee, changes);
            Take(inventory, row.ItemId, (int)row.Count, changes);
            int before = inventory.CountOf(rewardId);
            var stack = inventory.Add(rewardId, (int)Math.Min(rewardCount, int.MaxValue));
            changes.Add(InventoryMessages.ChangeRow(before == 0 ? InventoryChange.Add : InventoryChange.Modify, stack));
            SelfLine(session, ManorProtocol.TradedCrop, $"Traded {row.Count} of crop {cropName}.", cropName, row.Count.ToString());
            sold = true;
            ZoneLog.Info($"[manor] sell crop={row.ItemId} x{row.Count} manor={row.ManorId} by={player.ObjectId} reward={rewardId} x{rewardCount} fee={fee}");
        }

        if (changes.Count > 0)
        {
            FanInventory(session, Collapse(changes));
            SaveInventory(session, inventory);
        }

        if (sold)
            PersistManor();
        return null;
    }

    /// <summary>Mobius RequestSetSeed / RequestSetCrop rights: the owner clan, CASTLE_MANOR (leader always), near the chamberlain.</summary>
    string? LordRights(ZoneSession session, ZoneEntity player, int castleId)
    {
        var clan = ClanOf(player.ObjectId);
        var castle = _siege.Board.Find(castleId);
        if (clan == null || castle == null || castle.OwnerClanId != clan.Id ||
            (clan.LeaderId != player.ObjectId && !HasPriv(clan, player.ObjectId, PledgeProtocol.PrivCastleManor)))
            return Rights(session);
        if (!NearChamberlain(player, castleId))
            return ManorProtocol.Manager;
        return null;
    }

    /// <summary>Mobius ExShowSeedSetting / ExShowCropSetting: every castle seed with the current and next period rows.</summary>
    string? LordPage(ZoneSession session, ZoneEntity player, int castleId, bool seeds)
    {
        if (LordRights(session, player, castleId) is { } denied)
            return denied;
        var info = ManorHeader(seeds ? ManorCommandKind.SeedSetting : ManorCommandKind.CropSetting, castleId, true);
        info.Treasury = _siege.Board.Find(castleId)!.Treasury;
        info.ManorCost = _manor.Board.ManorCost(castleId, next: true);
        var castleSeeds = _manor.Board.Data.SeedsFor(castleId).ToList();
        if (seeds)
            info.Seeds = castleSeeds.Select(s => SeedRow(s, _manor.Board.Product(castleId, s.SeedId, true), _manor.Board.Product(castleId, s.SeedId, false))).ToArray();
        else
            info.Crops = castleSeeds.GroupBy(s => s.CropId).Select(g => g.First())
                .Select(s => CropRow(s, castleId, _manor.Board.CropProcure(castleId, s.CropId, true), _manor.Board.CropProcure(castleId, s.CropId, false)))
                .ToArray();
        SendManor(session, info);
        return null;
    }

    /// <summary>Mobius RequestSetSeed: only while editable; rows over the limit or outside 60%–1000% of the reference are dropped.</summary>
    string? SetNextSeeds(ZoneSession session, ZoneEntity player, int castleId, ManorRowNet[] rows)
    {
        if (!_manor.Board.Modifiable)
            return ManorProtocol.Period;
        if (LordRights(session, player, castleId) is { } denied)
            return denied;
        var list = new List<ManorProduct>();
        foreach (var row in rows)
        {
            if (row.ItemId < 1 || row.Count < 0 || row.Price < 0)
                return "bad_request";
            var seed = _manor.Board.Data.Seed(row.ItemId);
            if (row.Count == 0 || seed == null || seed.CastleId != castleId || list.Any(p => p.SeedId == row.ItemId))
                continue;
            if (row.Count <= _manor.Board.SeedLimit(seed) && row.Price >= seed.SeedMinPrice && row.Price <= seed.SeedMaxPrice)
                list.Add(new ManorProduct(row.ItemId, row.Count, row.Price, row.Count));
        }

        _manor.Board.SetProduction(castleId, true, list);
        PersistManor();
        return LordPage(session, player, castleId, seeds: true);
    }

    /// <summary>Mobius RequestSetCrop: like seeds, with the reward type 1 / 2 kept per row.</summary>
    string? SetNextCrops(ZoneSession session, ZoneEntity player, int castleId, ManorRowNet[] rows)
    {
        if (!_manor.Board.Modifiable)
            return ManorProtocol.Period;
        if (LordRights(session, player, castleId) is { } denied)
            return denied;
        var list = new List<ManorProcure>();
        foreach (var row in rows)
        {
            if (row.ItemId < 1 || row.Count < 0 || row.Price < 0)
                return "bad_request";
            var seed = _manor.Board.Data.SeedByCrop(row.ItemId, castleId);
            if (row.Count == 0 || seed == null || list.Any(p => p.CropId == row.ItemId))
                continue;
            if (row.Count <= _manor.Board.CropLimit(seed) && row.Price >= seed.CropMinPrice && row.Price <= seed.CropMaxPrice)
                list.Add(new ManorProcure(row.ItemId, row.Count, row.RewardType == 2 ? 2 : 1, row.Count, row.Price));
        }

        _manor.Board.SetProcure(castleId, true, list);
        PersistManor();
        return LordPage(session, player, castleId, seeds: false);
    }

    /// <summary>Test hook: run the next manor mode change now.</summary>
    internal void ChangeManorModeForTests()
    {
        using (_affinity.Step())
        {
            if (_manor.Advance(NowUnixMs()) is { } change)
                ApplyManorMode(change);
        }
    }

    internal void TickManorForTests()
    {
        using (_affinity.Step())
            AtHookTick(tick => ApplyManorModes(_manor.Step(tick)));
    }
}
