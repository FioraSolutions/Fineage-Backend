using Fineage.Zone.Geo;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Combat;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.Drops;
using Fineage.Zone.Bosses;
using Fineage.Game.Shops;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Shops;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Dungeons;
using Fineage.Zone.Siege;
using Fineage.Zone.Olympiad;
using Fineage.Game.Services;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// All live entities on the session knownlist (players + NPCs + ground items).
    /// </summary>
    internal List<ZoneEntity> SnapshotKnownEntities(ZoneSession session)
    {
        lock (_gate)
        {
            if (!TryGetLivePlayerUnlocked(session, out var self))
                return [];

            var list = new List<ZoneEntity>(self.Knownlist.Count);
            foreach (int id in self.Knownlist)
            {
                if (!_byId.TryGetValue(id, out var other) || other.InstanceId != self.InstanceId)
                    continue;
                if (other.Kind == ZoneEntityKind.Player && other.Session is not { InWorld: true })
                    continue;
                list.Add(other);
            }

            list.Sort(static (a, b) => a.ObjectId.CompareTo(b.ObjectId));
            return list;
        }
    }

    /// <summary>Object ids on the session's knownlist (any entity kind). For move enter/leave diffs.</summary>
    internal HashSet<int> SnapshotKnownIds(ZoneSession session)
    {
        lock (_gate)
        {
            if (!TryGetLivePlayerUnlocked(session, out var self))
                return [];
            return new HashSet<int>(self.Knownlist);
        }
    }

    bool TryGetLivePlayerUnlocked(ZoneSession session, out ZoneEntity entity)
    {
        if (session.ObjectId != 0 &&
            _byId.TryGetValue(session.ObjectId, out entity!) &&
            entity.Kind == ZoneEntityKind.Player &&
            ReferenceEquals(entity.Session, session) &&
            session.InWorld)
            return true;
        entity = null!;
        return false;
    }

    /// <summary>Live player by object id (for Net knownlist enter diffs).</summary>
    internal bool TryGetLivePlayerById(int objectId, out ZoneEntity entity)
    {
        lock (_gate)
        {
            if (objectId != 0 &&
                _byId.TryGetValue(objectId, out entity!) &&
                entity.Kind == ZoneEntityKind.Player &&
                entity.Session is { InWorld: true })
                return true;
            entity = null!;
            return false;
        }
    }

    /// <summary>Any world entity by id (player / NPC) for Net knownlist diffs.</summary>
    internal bool TryGetEntityById(int objectId, out ZoneEntity entity)
    {
        lock (_gate)
        {
            if (objectId != 0 && _byId.TryGetValue(objectId, out entity!))
                return true;
            entity = null!;
            return false;
        }
    }

    /// <summary>
    /// Test hook: kill <paramref name="player"/> as if <paramref name="killer"/> had landed the final blow, so the
    /// exp loss and Death Penalty roll run through the production path.
    /// </summary>
    internal void KillPlayerByNpcForTest(ZoneEntity player, ZoneEntity killer)
    {
        lock (_gate)
            ApplyPlayerDeath(player, killer.ObjectId);
    }

    /// <summary>Test hook: rebuild the book after a test mutates <see cref="ZoneEntity.LearnedSkills"/>.</summary>
    internal void RefreshKnownSkillsForTest(ZoneEntity player)
    {
        lock (_gate)
            RefreshKnownSkills(player);
    }

    /// <summary>Test hook: run Mobius <c>checkPlayerSkills</c> without going through a death.</summary>
    internal void DecreaseSkillsOnDelevelForTest(ZoneEntity player)
    {
        lock (_gate)
            DecreaseSkillsOnDelevel(player);
    }

    public async Task<bool> TryEnterAsync(ZoneSession session, CancellationToken ct)
    {
        // Load durable character state off the world lock so a slow Postgres round-trip
        // cannot stall move/combat handlers waiting on _gate.
        CharacterRecord? stored = null;
        if (_accounts != null && session.ObjectId > 0)
        {
            stored = await Task.Run(
                () => _accounts.FindById(session.Account, session.ObjectId),
                ct).ConfigureAwait(false);
        }

        // Starter-kit backfill is durable IO — keep it off the lock too.
        if (stored is { Items: null } && _accounts != null)
        {
            var granted = _game.CreateStarterInventory(session.ClassId, _itemIds);
            await Task.Run(
                () => _persist.ReplaceItemsFromInventory(session.Account, session.ObjectId, granted),
                ct).ConfigureAwait(false);
            stored = stored with { Items = CharacterInventory.Rows(granted) };
        }

        bool ok;
        int known;
        int enterX = 0, enterY = 0, enterZ = 0;
        List<UnlinkPersistSnapshot>? pendingUnlink = null;
        lock (_gate)
            ok = TryEnter(session, stored, out known, out enterX, out enterY, out enterZ, out pendingUnlink);
        if (!ok)
            return false;

        if (pendingUnlink is { Count: > 0 })
        {
            foreach (var snapshot in pendingUnlink)
                PersistUnlinkSnapshot(snapshot);
        }

        // Presence / last_access after the world commit so DB latency is not under _gate.
        SavePresence(session, enterX, enterY, enterZ, touchAccess: true);
        ZoneLog.EnterWorld(session, known);
        return true;
    }

    public Task LeaveAsync(ZoneSession session, CancellationToken ct)
    {
        UnlinkPersistSnapshot? snapshot = null;
        lock (_gate)
            TryDetachPlayer(session, capturePersist: true, out _, out snapshot);
        if (snapshot == null)
            return Task.CompletedTask;

        PersistUnlinkSnapshot(snapshot.Value);

        _sevenSigns.Save();
        _bosses.Save();
        ZoneLog.LeaveWorld(session);
        return Task.CompletedTask;
    }

    public Task<string?> TryMoveAsync(ZoneSession session, int destX, int destY, int destZ, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = TryMove(session, destX, destY, destZ);
        if (error != null)
            return Task.FromResult<string?>(error);

        MaybeSavePresenceOnMove(session);
        return Task.FromResult<string?>(null);
    }

    bool TryEnter(
        ZoneSession session,
        CharacterRecord? stored,
        out int known,
        out int enterX,
        out int enterY,
        out int enterZ,
        out List<UnlinkPersistSnapshot>? pendingUnlink)
    {
        known = 0;
        enterX = enterY = enterZ = 0;
        pendingUnlink = null;
        if (session.ObjectId <= 0)
            return false;
        if (_byId.TryGetValue(session.ObjectId, out var occupied) && occupied.Kind != ZoneEntityKind.Player)
            return false;

        // Same-session re-enter: detach without flushing (avoid double write), then rebuild
        // from the preloaded CharacterRecord so durable edits while InWorld (death penalty,
        // etc.) still apply — Reattach alone would keep a stale in-memory snapshot.
        if (session.InWorld)
        {
            if (!TryDetachPlayer(session, capturePersist: false, out _, out _))
                return false;
        }
        else if (_byId.TryGetValue(session.ObjectId, out var current) &&
                 current.Session != null &&
                 !ReferenceEquals(current.Session, session) &&
                 TryDetachPlayer(current.Session, capturePersist: true, out _, out var evicted))
        {
            pendingUnlink = [evicted!.Value];
        }

        var spawn = SpawnFor(session, stored);
        var inventory = LoadInventory(session, stored);
        var vitals = LoadVitals(session, stored);
        var warehouse = new PrivateWarehouse(_game.Items);
        if (stored?.Warehouse != null)
            warehouse.Restore(stored.Warehouse.Select(item => new WarehouseRow(item.ItemId, item.Count, item.EnchantLevel, item.ObjectId)));
        RememberPenalties(session, stored);
        var entity = new ZoneEntity
        {
            ObjectId = session.ObjectId,
            Kind = ZoneEntityKind.Player,
            Name = session.Name,
            X = spawn.X,
            Y = spawn.Y,
            Z = spawn.Z,
            ClassId = session.ClassId,
            RunSpeed = MoveSpeed(session.ClassId).run,
            WalkSpeed = MoveSpeed(session.ClassId).walk,
            Dex = DexOf(session.ClassId),
            // Mobius player templates always declare baseMoveSpd slowSwim/fastSwim = 50.
            BaseSwimRunSpeed = ZoneCombat.DefaultSwimSpeed,
            BaseSwimWalkSpeed = ZoneCombat.DefaultSwimSpeed,
            Running = true,
            Session = session,
            Inventory = inventory,
            MaxHp = vitals.maxHp,
            CurHp = vitals.hp,
            MaxMp = vitals.maxMp,
            CurMp = vitals.mp,
            Level = vitals.level,
            Race = vitals.race,
            Sex = vitals.sex,
            ClanId = ClanIdFor(session.ObjectId),
            Warehouse = warehouse,
            Newbie = stored?.Newbie ?? true,
            // Mobius Player.loadCharFromDB:6578 restores _deathPenaltyBuffLevel, so the debuff outlives the session.
            DeathPenaltyLevel = Math.Clamp(stored?.DeathPenaltyLevel ?? 0, 0, DeathPenalty.MaxLevel),
            Karma = Math.Max(0, stored?.Karma ?? 0),
            PkKills = Math.Max(0, stored?.PkKills ?? 0),
            PvpKills = Math.Max(0, stored?.PvpKills ?? 0)
        };
        LoadHenna(entity, stored);
        LoadQuestProgress(session, entity, stored);
        LoadLearnedSkills(entity, stored);
        LoadKnownRecipes(entity, stored);
        LoadSocialLists(entity, stored);
        RefreshKnownSkills(entity);
        // Mobius GameClient:493 / EnterWorld:208 — refreshOverloaded() runs before the first UserInfo, so a character
        // that logs in while overloaded already carries skill 4270's level and its slowed speeds. This must come after
        // RefreshKnownSkills: Weight Limit 150 and the armor-set weightLimit/weightPenalty passives are only in
        // KnownSkills once the skill book is rebuilt, and they change both the limit and the bonus.
        int penalty = WeightPenaltyLevelOf(entity);
        if (penalty != entity.WeightPenaltyLevel)
        {
            entity.WeightPenaltyLevel = penalty;
            RefreshRunSpeed(entity); // PlayerStatMods folds 4270, so the live speed needs the level first
        }
        LoadShortcuts(entity, stored);
        SyncClanStanding(entity);
        RefreshGradePenalty(entity);
        _byId[entity.ObjectId] = entity;
        _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
        session.InWorld = true;
        session.X = entity.X;
        session.Y = entity.Y;
        session.Z = entity.Z;
        enterX = entity.X;
        enterY = entity.Y;
        enterZ = entity.Z;

        // The enter sequence itself (UserInfo, ItemList, SkillList, spawns) is Net-only and lives in
        // NetZoneServer.OnEnterAsync; the knownlist is still built here because the sim owns it.
        foreach (var other in OrderedVisible(entity.ObjectId))
        {
            entity.Knownlist.Add(other.ObjectId);
            if (other.Session != null)
                other.Knownlist.Add(entity.ObjectId);
        }

        AnnounceClanEnter(entity);
        FriendPlayerEnteredWorld(entity);
        known = entity.Knownlist.Count;
        return true;
    }

}
