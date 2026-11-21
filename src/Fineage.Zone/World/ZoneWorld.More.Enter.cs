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
using Fineage.Game.Economy;
using Fineage.Game.Services;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<bool> TryEnterAsync(ZoneSession session, CancellationToken ct) =>
        await EnterAsync(session, static _ => EnterAccepted, ct).ConfigureAwait(false) != null;

    static readonly object EnterAccepted = new();

    sealed record EnterOutcome<T>(T Projection, int Known, int X, int Y, int Z, List<UnlinkPersistSnapshot>? Evicted);

    /// <summary>
    /// Loads the character, enters it on the simulation thread and, in the same step, runs <paramref name="project"/>
    /// on the new player, so what the client is sent describes the world it entered. Null when the enter is refused.
    /// </summary>
    internal async Task<T?> EnterAsync<T>(ZoneSession session, Func<ZoneEntity, T> project, CancellationToken ct)
        where T : class
    {
        // Durable character state and the starter-kit backfill are Postgres round-trips: they stay on the caller.
        CharacterRecord? stored = null;
        if (_accounts != null && session.ObjectId > 0)
        {
            stored = await Task.Run(
                () => _accounts.FindById(session.Account, session.ObjectId),
                ct).ConfigureAwait(false);
        }

        bool persistStarterBackfill = stored is { Items: null } && _accounts != null;

        var entered = await OnSimulationAsync(session, "enter", () =>
        {
            if (!TryEnter(session, stored, out int known, out int x, out int y, out int z, out var evicted))
                return null;
            CountOnlinePlayers();
            if (!TryGetLivePlayer(session, out var player))
                throw new InvalidOperationException($"object {session.ObjectId} entered without a live player");
            return new EnterOutcome<T>(project(player), known, x, y, z, evicted);
        }, ct).ConfigureAwait(false);
        if (entered == null)
            return null;

        if (persistStarterBackfill &&
            TryGetLivePlayer(session, out var backfillPlayer) &&
            backfillPlayer.Inventory != null)
        {
            await Task.Run(
                () => _persist.ReplaceItemsFromInventory(session.Account, session.ObjectId, backfillPlayer.Inventory),
                ct).ConfigureAwait(false);
        }

        if (entered.Evicted is { Count: > 0 })
        {
            foreach (var snapshot in entered.Evicted)
                PersistUnlinkSnapshot(snapshot);
        }

        SavePresence(session, entered.X, entered.Y, entered.Z, touchAccess: true);
        ZoneLog.EnterWorld(session, entered.Known);
        return entered.Projection;
    }

    public Task LeaveAsync(ZoneSession session, CancellationToken ct) => LeaveAsync<object>(session, null, ct);

    /// <summary>
    /// Takes the session's player out of the world on the simulation thread; <paramref name="projectBefore"/> runs on
    /// the live player first, in the same step (what its peers must be told). Null when it was not in the world.
    /// </summary>
    internal async Task<T?> LeaveAsync<T>(ZoneSession session, Func<ZoneEntity, T>? projectBefore, CancellationToken ct)
        where T : class
    {
        if (session.ObjectId == 0)
            return null;
        var (projection, snapshot) = await OnSimulationAsync(session, "leave", () =>
        {
            var before = projectBefore != null && TryGetLivePlayer(session, out var player) ? projectBefore(player) : null;
            TryDetachPlayer(session, capturePersist: true, out _, out var captured);
            CountOnlinePlayers();
            return (before, captured);
        }, ct).ConfigureAwait(false);
        if (snapshot == null)
            return projection;

        PersistUnlinkSnapshot(snapshot.Value);

        _sevenSigns.Engine.Save();
        _bosses.Schedule.Save();
        ZoneLog.LeaveWorld(session);
        return projection;
    }

    public Task<string?> TryMoveAsync(ZoneSession session, int destX, int destY, int destZ, CancellationToken ct)
    {
        string? error = TryMove(session, destX, destY, destZ);
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
        long enterNow = NowUnixMs();
        if (PunishmentBlocksEnter(session, enterNow))
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
        bool applyStarterKit = NeedsStarterInventory(stored);
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
            Title = stored?.Title ?? "",
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
            MaxCp = vitals.maxCp,
            CurCp = vitals.cp,
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
        LoadClassState(entity, stored);
        SyncOlympiadNoblesse(entity);
        LoadQuestProgress(session, entity, stored);
        LoadLearnedSkills(entity, stored);
        LoadKnownRecipes(entity, stored);
        LoadSocialLists(entity, stored);
        RefreshKnownSkills(entity);
        RefreshGearSkills(entity);
        RefreshPlayerMaxima(entity, fanout: false);
        RestoreStoredEffects(entity, stored);
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
        if (applyStarterKit)
            ApplyStarterKitLedger(entity);
        _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
        session.InWorld = true;
        _chat.Entered(entity.ObjectId, entity.Name);
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
            TrackWatcher(entity.ObjectId, other.ObjectId);
            if (other.Session != null)
            {
                other.Knownlist.Add(entity.ObjectId);
                TrackWatcher(other.ObjectId, entity.ObjectId);
            }
            else
                TrackThink(other);
        }

        AnnounceClanEnter(entity);
        FriendPlayerEnteredWorld(entity);
        SyncPunishmentsOnEnter(session, entity, enterNow);
        known = entity.Knownlist.Count;
        return true;
    }

}
