using Fineage.Accounts;
using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.CursedWeapons;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly CursedWeaponsManager _cursedWeapons;

    public const int CursedSysDisappeared = 1590;
    public const int CursedSysDroppedInRegion = 1591;
    public const int CursedSysOwnerAppeared = 1592;
    public const int CursedSysOwnerLoggedIn = 1593;
    public const int CursedSysMinutesRemaining = 1594;

    internal bool WieldsCursedWeapon(ZoneEntity player) =>
        player.Kind == ZoneEntityKind.Player && _cursedWeapons.WieldsBlade(player.ObjectId);

    internal bool WieldsCursedWeaponForTest(ZoneEntity player) => WieldsCursedWeapon(player);

    internal CursedWeaponsManager CursedWeaponsForTest => _cursedWeapons;

    internal void ActivateCursedWeaponForTest(ZoneEntity player, int itemId)
    {
        if (_cursedWeapons.Get(itemId) is not { } runtime)
            return;
        var item = player.Inventory?.FindByItemId(itemId);
        if (item == null)
            return;
        ActivateCursedWeapon(player, runtime, item, fromGround: false);
    }

    void TickCursedWeapons(long nowUnixMs)
    {
        if (!_cursedWeapons.NeedsSimulationTick(nowUnixMs))
            return;
        foreach (var runtime in _cursedWeapons.All)
        {
            if (!runtime.IsActive || runtime.EndTimeUnixMs <= nowUnixMs)
            {
                if (runtime.IsActive && runtime.EndTimeUnixMs > 0 && runtime.EndTimeUnixMs <= nowUnixMs)
                    EndCursedWeaponLife(runtime, nowUnixMs);
                continue;
            }

            if (runtime.Dropped && runtime.GroundObjectId > 0
                && !_byId.TryGetValue(runtime.GroundObjectId, out _))
            {
                runtime.Dropped = false;
                runtime.GroundObjectId = 0;
                PersistCursedWeapons();
                FanoutCursedWeaponState();
            }
        }
    }

    void CheckCursedDropOnMobDeath(ZoneEntity npc, ZoneEntity? killer, Random rng)
    {
        if (npc.Kind != ZoneEntityKind.Npc || !npc.Attackable)
            return;
        if (CursedWeaponsManager.NpcExcludedFromDrop(npc.NpcType))
            return;
        if (IsTreasureBox(npc.NpcId) || IsChestNpc(npc))
            return;
        if (killer?.Session == null)
            return;

        foreach (var runtime in _cursedWeapons.All)
        {
            if (runtime.IsActive)
                continue;
            if (rng.Next(CursedWeaponRuntime.MobiusDropDenom) >= runtime.Definition.DropRate)
                continue;
            SpawnCursedBladeDrop(runtime, npc.X, npc.Y, npc.Z, killer);
            runtime.Dropped = true;
            runtime.EndTimeUnixMs = NowUnixMs() + runtime.Definition.DurationMinutes * 60_000L;
            PersistCursedWeapons();
            AnnounceCursedDropped(runtime, killer);
            FanoutCursedWeaponState();
            break;
        }
    }

    void SpawnCursedBladeDrop(CursedWeaponRuntime runtime, int x, int y, int z, ZoneEntity killer)
    {
        if (!_game.Items.TryGet(runtime.Definition.ItemId, out var template))
        {
            ZoneLog.Warn($"cursed weapon drop missing item template {runtime.Definition.ItemId}");
            return;
        }

        int groundId = _itemIds.Next();
        var watchers = WatchingPlayers(killer.ObjectId);
        long now = NowUnixMs();
        var ground = SpawnGroundStack(
            runtime.Definition.ItemId,
            1,
            x,
            y,
            z,
            killer.InstanceId,
            watchers,
            dropperObjectId: killer.ObjectId,
            reserveObjectId: groundId,
            expireAtMs: now + GroundItemAutoDestroyMs);
        if (ground != null)
            runtime.GroundObjectId = ground.ObjectId;
    }

    void OnCursedWeaponGroundPickup(ZoneEntity player, ZoneEntity ground)
    {
        if (!CursedWeaponIds.IsCursedItem(ground.GroundItemId))
            return;
        var runtime = _cursedWeapons.Get(ground.GroundItemId);
        if (runtime == null || !runtime.Dropped)
            return;
        var item = player.Inventory?.FindByItemId(ground.GroundItemId);
        if (item == null)
            return;
        ActivateCursedWeapon(player, runtime, item, fromGround: true);
        runtime.Dropped = false;
        runtime.GroundObjectId = 0;
    }

    void OnCursedWeaponEquipped(ZoneSession session, ZoneEntity player, ItemInstance item)
    {
        if (!item.IsEquipped || !CursedWeaponIds.IsCursedItem(item.ItemId))
            return;
        var runtime = _cursedWeapons.Get(item.ItemId);
        if (runtime == null || runtime.Activated)
            return;
        if (runtime.Dropped)
            ActivateCursedWeapon(player, runtime, item, fromGround: false);
    }

    void ActivateCursedWeapon(ZoneEntity player, CursedWeaponRuntime runtime, ItemInstance item, bool fromGround)
    {
        if (player.Session == null || player.Inventory == null)
            return;

        int otherId = _cursedWeapons.EquippedItemId(player.ObjectId);
        if (otherId > 0 && otherId != runtime.Definition.ItemId)
        {
            var other = _cursedWeapons.Get(otherId);
            if (other != null)
                EndCursedWeaponLife(other, NowUnixMs());
        }
        else if (WieldsCursedWeapon(player))
        {
            var existing = _cursedWeapons.Get(runtime.Definition.ItemId);
            if (existing != null)
            {
                existing.NbKills = Math.Max(0, existing.Definition.StageKills - 1);
                RecordCursedKill(player, existing);
            }
            DestroyCursedInventoryItem(player, runtime.Definition.ItemId);
            return;
        }

        runtime.Activated = true;
        runtime.Dropped = false;
        runtime.OwnerObjectId = player.ObjectId;
        runtime.PlayerReputation = player.Karma;
        runtime.PlayerPkKills = player.PkKills;
        if (runtime.EndTimeUnixMs <= NowUnixMs())
            runtime.EndTimeUnixMs = NowUnixMs() + runtime.Definition.DurationMinutes * 60_000L;

        player.CursedWeaponItemId = runtime.Definition.ItemId;
        player.SavedKarmaBeforeCurse = runtime.PlayerReputation;
        player.SavedPkKillsBeforeCurse = runtime.PlayerPkKills;
        player.Karma = CursedWeaponRuntime.KarmaWhileWielding;
        player.PkKills = runtime.NbKills;
        PersistKarmaPk(player);

        TryPartyLeave(player.ObjectId);
        ApplyCursedSkill(player, runtime);
        if (!item.IsEquipped)
        {
            try
            {
                player.Inventory.Equip(item);
                PublishGear(player.Session, player, PaperdollSnapshot(player.Inventory));
            }
            catch (InvalidOperationException)
            {
                ZoneLog.Warn($"cursed weapon equip failed object={player.ObjectId} item={item.ObjectId}");
            }
        }

        player.CurHp = player.MaxHp;
        player.CurMp = player.MaxMp;
        player.CurCp = player.MaxCp;
        SelfUserInfo(player);
        FanoutPlayerStats(player);
        PersistCursedWeapons();
        AnnounceCursedOwnerAppeared(runtime, player);
        FanoutCursedWeaponState();
        ZoneLog.Info($"cursed activate object={player.ObjectId} item={runtime.Definition.ItemId} ground={fromGround}");
    }

    void ApplyCursedSkill(ZoneEntity player, CursedWeaponRuntime runtime)
    {
        int maxLevel = ResolveCursedSkillMaxLevel(runtime.Definition.SkillId);
        int level = runtime.SkillLevel(maxLevel);
        player.CursedSkills[runtime.Definition.SkillId] = level;
        RefreshPlayerMaxima(player, fanout: true);
        if (player.Session != null)
        {
            NetBridge?.FanoutSkillList(
                [player.ObjectId],
                Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
        }
    }

    void RemoveCursedSkill(ZoneEntity player, CursedWeaponRuntime runtime)
    {
        player.CursedSkills.Remove(runtime.Definition.SkillId);
        RefreshPlayerMaxima(player, fanout: true);
        if (player.Session != null)
        {
            NetBridge?.FanoutSkillList(
                [player.ObjectId],
                Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
        }
    }

    int ResolveCursedSkillMaxLevel(int skillId)
    {
        if (_game.Skills.Find(skillId) is { } skill && skill.MaxLevel > 0)
            return skill.MaxLevel;
        ZoneLog.Warn($"cursed weapon skill {skillId} missing from catalogs; defaulting max level to 10");
        return 10;
    }

    void RecordCursedKill(ZoneEntity killer, CursedWeaponRuntime? runtime = null)
    {
        runtime ??= _cursedWeapons.Get(_cursedWeapons.EquippedItemId(killer.ObjectId));
        if (runtime == null || !runtime.Activated || runtime.OwnerObjectId != killer.ObjectId)
            return;
        runtime.NbKills++;
        killer.PkKills = runtime.NbKills;
        runtime.EndTimeUnixMs -= runtime.Definition.DurationLostMinutes * 60_000L;
        int maxLevel = ResolveCursedSkillMaxLevel(runtime.Definition.SkillId);
        int stage = runtime.Definition.StageKills;
        if (stage > 0 && runtime.NbKills % stage == 0 && runtime.SkillLevel(maxLevel) <= maxLevel)
            ApplyCursedSkill(killer, runtime);
        PersistKarmaPk(killer);
        SelfUserInfo(killer);
        PersistCursedWeapons();
        FanoutCursedWeaponState();
    }

    void OnCursedOwnerDeath(ZoneEntity player, ZoneEntity? killer)
    {
        int itemId = _cursedWeapons.EquippedItemId(player.ObjectId);
        if (itemId <= 0)
            return;
        var runtime = _cursedWeapons.Get(itemId);
        if (runtime == null || !runtime.Activated)
            return;

        var rng = _dropRandom.For(_regions.RegionOf(player.InstanceId, player.X, player.Y));
        if (rng.Next(100) <= runtime.Definition.DisappearChance)
        {
            EndCursedWeaponLife(runtime, NowUnixMs());
            return;
        }

        DropCursedFromOwner(player, runtime, killer);
    }

    void DropCursedFromOwner(ZoneEntity player, CursedWeaponRuntime runtime, ZoneEntity? killer)
    {
        DestroyCursedInventoryItem(player, runtime.Definition.ItemId);
        ClearCursedOwnerState(player, runtime);
        SpawnCursedBladeDrop(runtime, player.X, player.Y, player.Z, player);
        runtime.Activated = false;
        runtime.Dropped = true;
        runtime.OwnerObjectId = 0;
        AnnounceCursedDropped(runtime, player);
        PersistCursedWeapons();
        FanoutCursedWeaponState();
        SelfUserInfo(player);
        FanoutCharInfoPeers(player);
    }

    void EndCursedWeaponLife(CursedWeaponRuntime runtime, long nowUnixMs)
    {
        if (runtime.Activated && runtime.OwnerObjectId > 0
            && _byId.TryGetValue(runtime.OwnerObjectId, out var owner)
            && owner.Kind == ZoneEntityKind.Player)
        {
            ClearCursedOwnerState(owner, runtime);
            DestroyCursedInventoryItem(owner, runtime.Definition.ItemId);
            SelfUserInfo(owner);
            FanoutCharInfoPeers(owner);
        }
        else if (runtime.GroundObjectId > 0 && _byId.TryGetValue(runtime.GroundObjectId, out var ground))
            RemoveGroundItem(ground);

        AnnounceCursedDisappeared(runtime);
        runtime.Reset();
        PersistCursedWeapons();
        FanoutCursedWeaponState();
        _ = nowUnixMs;
    }

    void ClearCursedOwnerState(ZoneEntity player, CursedWeaponRuntime runtime)
    {
        player.Karma = runtime.PlayerReputation;
        player.PkKills = runtime.PlayerPkKills;
        player.CursedWeaponItemId = 0;
        player.SavedKarmaBeforeCurse = 0;
        player.SavedPkKillsBeforeCurse = 0;
        RemoveCursedSkill(player, runtime);
        PersistKarmaPk(player);
    }

    void DestroyCursedInventoryItem(ZoneEntity player, int itemId)
    {
        if (player.Inventory == null || player.Session == null)
            return;
        var item = player.Inventory.FindByItemId(itemId);
        if (item == null)
            return;
        player.Inventory.Remove(item);
        SaveInventory(player.Session, player.Inventory);
        NetBridge?.FanoutInventoryUpdate(
            [player.ObjectId],
            InventoryMessages.Update(InventoryChange.Remove, item));
    }

    void CheckCursedWeaponOnEnter(ZoneEntity player)
    {
        foreach (var runtime in _cursedWeapons.All)
        {
            if (!runtime.Activated || runtime.OwnerObjectId != player.ObjectId)
                continue;
            if (player.Inventory?.FindByItemId(runtime.Definition.ItemId) is not { } item)
            {
                EndCursedWeaponLife(runtime, NowUnixMs());
                continue;
            }

            player.CursedWeaponItemId = runtime.Definition.ItemId;
            player.SavedKarmaBeforeCurse = runtime.PlayerReputation;
            player.SavedPkKillsBeforeCurse = runtime.PlayerPkKills;
            player.Karma = CursedWeaponRuntime.KarmaWhileWielding;
            player.PkKills = runtime.NbKills;
            ApplyCursedSkill(player, runtime);
            if (!item.IsEquipped)
            {
                try
                {
                    player.Inventory.Equip(item);
                }
                catch (InvalidOperationException)
                {
                    ZoneLog.Warn($"cursed re-equip on enter failed object={player.ObjectId}");
                }
            }

            int minutes = (int)(runtime.TimeLeftMs(NowUnixMs()) / 60_000);
            FanCursedTimeRemaining(player, runtime.Definition.ItemId, minutes);
            break;
        }

        if (_cursedWeapons.HasClientVisibleState())
            FanoutCursedWeaponStateTo([player.ObjectId]);
    }

    void PersistCursedWeapons() => _accounts?.SaveCursedWeapons(_cursedWeapons.ExportSaves());

    void FanoutCursedWeaponState() => FanoutCursedWeaponStateTo(OnlinePlayerObjectIds());

    void FanoutCursedWeaponStateTo(IReadOnlyList<int> watchers)
    {
        if (NetBridge == null || watchers.Count == 0)
            return;
        var list = BuildCursedWeaponList();
        var loc = BuildCursedWeaponLocations();
        NetBridge.FanoutCursedWeaponList(watchers, list);
        NetBridge.FanoutCursedWeaponLocation(watchers, loc);
    }

    CursedWeaponListMsg BuildCursedWeaponList()
    {
        var ids = new List<int>();
        foreach (var runtime in _cursedWeapons.All)
        {
            if (runtime.IsActive)
                ids.Add(runtime.Definition.ItemId);
        }

        return new CursedWeaponListMsg { ItemIds = ids.ToArray() };
    }

    CursedWeaponLocationMsg BuildCursedWeaponLocations()
    {
        var rows = new List<CursedWeaponLocationRowMsg>();
        foreach (var runtime in _cursedWeapons.All)
        {
            if (!runtime.IsActive)
                continue;
            int x, y, z;
            int activated;
            if (runtime.Activated && _byId.TryGetValue(runtime.OwnerObjectId, out var owner))
            {
                x = owner.X;
                y = owner.Y;
                z = owner.Z;
                activated = 1;
            }
            else if (runtime.GroundObjectId > 0 && _byId.TryGetValue(runtime.GroundObjectId, out var ground))
            {
                x = ground.X;
                y = ground.Y;
                z = ground.Z;
                activated = 0;
            }
            else
                continue;

            rows.Add(new CursedWeaponLocationRowMsg
            {
                ItemId = runtime.Definition.ItemId,
                Activated = activated,
                X = x,
                Y = y,
                Z = z
            });
        }

        return new CursedWeaponLocationMsg { Rows = rows.ToArray() };
    }

    void AnnounceCursedDisappeared(CursedWeaponRuntime runtime) =>
        BroadcastCursedSystem(CursedSysDisappeared, [runtime.Definition.ItemId.ToString()],
            $"{runtime.Definition.Name} has disappeared.");

    void AnnounceCursedDropped(CursedWeaponRuntime runtime, ZoneEntity near) =>
        BroadcastCursedSystem(CursedSysDroppedInRegion, [RegionLabel(near), runtime.Definition.ItemId.ToString()],
            $"{runtime.Definition.Name} was dropped in {RegionLabel(near)}.");

    void AnnounceCursedOwnerAppeared(CursedWeaponRuntime runtime, ZoneEntity owner) =>
        BroadcastCursedSystem(CursedSysOwnerAppeared, [RegionLabel(owner), runtime.Definition.ItemId.ToString()],
            $"The owner of {runtime.Definition.Name} has appeared in {RegionLabel(owner)}.");

    void FanCursedTimeRemaining(ZoneEntity player, int itemId, int minutes) =>
        NetBridge?.FanoutSystemMessage([player.ObjectId], new SystemMessageMsg
        {
            Id = CursedSysMinutesRemaining,
            Params = [itemId.ToString(), minutes.ToString()],
            Text = $"{itemId} has {minutes} minute(s) of usage time remaining."
        });

    void BroadcastCursedSystem(int id, string[] pars, string text)
    {
        var line = new SystemMessageMsg { Id = id, Params = pars, Text = text };
        var live = OnlinePlayerObjectIds();
        if (live.Count > 0)
            NetBridge?.FanoutSystemMessage(live, line);
    }

    static string RegionLabel(ZoneEntity entity) => $"{entity.X},{entity.Y}";

    List<int> OnlinePlayerObjectIds()
    {
        var list = new List<int>();
        foreach (var entity in _byId.Values)
        {
            if (entity.Kind == ZoneEntityKind.Player && entity.Session != null)
                list.Add(entity.ObjectId);
        }

        return list;
    }

    void FanoutCharInfoPeers(ZoneEntity player)
    {
        List<int>? peers = null;
        foreach (int id in player.Knownlist)
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
                (peers ??= []).Add(id);
        }

        if (peers != null && NetBridge != null)
            NetBridge.FanoutCharInfo(peers, player.ObjectId);
    }

    void FanoutPlayerStats(ZoneEntity player)
    {
        if (player.Session == null || NetBridge == null)
            return;
        NetBridge.FanoutPlayerStats([player.ObjectId], new PlayerStatsMsg
        {
            ObjectId = player.ObjectId,
            Level = player.Level,
            Exp = player.Exp,
            Sp = player.Sp,
            Hp = player.CurHp,
            MaxHp = player.MaxHp,
            Mp = player.CurMp,
            MaxMp = player.MaxMp,
            Cp = player.CurCp,
            MaxCp = player.MaxCp
        });
    }
}