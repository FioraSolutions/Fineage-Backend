using System.Diagnostics;
using Fineage.Zone.Geo;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Combat;
using Fineage.Zone.Interest;
using Fineage.Zone.Simulation;
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
using Fineage.Zone.Net;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly SemaphoreSlim _enterLoads = new(
        ZoneSimulationSettings.EnterLoadConcurrency,
        ZoneSimulationSettings.EnterLoadConcurrency);

    public async Task<bool> TryEnterAsync(ZoneSession session, CancellationToken ct) =>
        await EnterWithSnapshotAsync(session, static _ => Fineage.Zone.Net.EnterSnapshot.ForTestEnter(), ct).ConfigureAwait(false) != null;

    sealed record EnterOutcome<T>(T Projection, int Known, int X, int Y, int Z);

    /// <summary>A character loaded off the simulation thread, not yet in the world.</summary>
    internal sealed class EnteringPlayer
    {
        public ZoneEntity? Entity { get; init; }
        public CharacterRecord? Stored { get; init; }
        public long CharacterEpoch { get; init; }
        public long PetEpoch { get; init; }
        public string? Failure { get; init; }
    }

    /// <summary>
    /// Load the character off the simulation thread, let <paramref name="projectOffTick"/> build the self frames
    /// from that body, then insert it through the command scheduler. The insert is the same
    /// <c>request:enter</c> admission as every other command.
    /// </summary>
    internal async Task<Fineage.Zone.Net.EnterSnapshot?> EnterWithSnapshotAsync(
        ZoneSession session,
        Func<ZoneEntity, Fineage.Zone.Net.EnterSnapshot> commit,
        CancellationToken ct,
        Action<ZoneEntity>? projectOffTick = null)
    {
        session.EnterRefusal = null;
        EnterOutcome<Fineage.Zone.Net.EnterSnapshot>? entered = null;
        long deadline = Stopwatch.GetTimestamp() + (long)(EnterChainBudget.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            // #1116: chain after this character's previous logout save, and read the store only once it is durable.
            string? waitRefusal = await AwaitPreviousSaveAsync(session.ObjectId, session.InWorld, deadline, ct).ConfigureAwait(false);
            if (waitRefusal != null)
            {
                session.EnterRefusal = waitRefusal;
                ZoneLog.Warn(waitRefusal);
                return null;
            }

            EnteringPlayer loaded = await LoadEnteringPlayerAsync(session, ct).ConfigureAwait(false);
            if (loaded.Failure != null || loaded.Entity == null)
            {
                string reason = loaded.Failure ?? $"enter refused: character {session.ObjectId} load produced no player";
                session.EnterRefusal = reason;
                ZoneLog.Warn(reason);
                return null;
            }

            session.PendingEnterSelfPrepare = Fineage.Zone.Net.EnterSelfPrepare.FromStored(loaded.Stored, _game.Items, _itemIds);
            try
            {
                projectOffTick?.Invoke(loaded.Entity);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                session.PendingEnterSelfPrepare = null;
                session.PendingEnterProjection = null;
                string frame = ex.StackTrace?.Split('\n', 2)[0].Trim() ?? "";
                string reason = $"enter refused: character {session.ObjectId} self projection failed: {ex.GetType().Name}: {ex.Message}"
                    + (frame.Length == 0 ? "" : $" ({frame})");
                session.EnterRefusal = reason;
                ZoneLog.Warn(reason);
                return null;
            }
            bool stale = false;
            List<(UnlinkPersistSnapshot Snapshot, LogoutSave Save)>? evictedForReload = null;
            try
            {
                using var enterScope = _syncIo.EnterScope();
                entered = await OnSimulationAsync(session, "enter", () =>
                {
                    if (!TryInsertEnteringPlayer(session, loaded, out int known, out int x, out int y, out int z, out evictedForReload, out stale))
                        return null;
                    CountOnlinePlayers();
                    if (!TryGetLivePlayer(session, out var player))
                        throw new InvalidOperationException($"object {session.ObjectId} entered without a live player");
                    return new EnterOutcome<Fineage.Zone.Net.EnterSnapshot>(commit(player), known, x, y, z);
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                session.PendingEnterSelfPrepare = null;
                session.PendingEnterProjection = null;
                string? syncIo = _syncIo.ConsumeSummary();
                if (syncIo != null)
                    ZoneLog.Warn($"enter object={session.ObjectId} sync-io on simulation thread: {syncIo}");
            }

            // Another session still held this character: it was evicted now, so this load predates its last state.
            // Its save runs before the next load (AwaitPreviousSaveAsync waits for the save registered at the evict).
            if (evictedForReload != null)
                await PersistEvictedAsync(evictedForReload).ConfigureAwait(false);
            if (!stale)
                break;
            if (Stopwatch.GetTimestamp() < deadline)
                continue;
            string changed = $"enter refused: character {session.ObjectId} changed while loading for {EnterChainBudget.TotalSeconds:0.#}s";
            session.EnterRefusal = changed;
            ZoneLog.Warn(changed);
            return null;
        }

        if (entered == null)
            return null;

        SavePresence(session, entered.X, entered.Y, entered.Z, touchAccess: true);
        ZoneLog.EnterWorld(session, entered.Known);
        SendPcCafeOnEnter(session);
        return entered.Projection;
    }

    async Task<EnteringPlayer> LoadEnteringPlayerAsync(ZoneSession session, CancellationToken ct)
    {
        await _enterLoads.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => LoadEnteringPlayer(session), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The first frame is often inside the BCL (Dictionary.TryInsert); name the first Fineage frame too and
            // keep the whole trace in the log so a load-path race can be found from one run.
            string frame = ex.StackTrace?.Split('\n', 2)[0].Trim() ?? "";
            string? ours = ex.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains(" Fineage.", StringComparison.Ordinal));
            if (ours != null && ours != frame)
                frame = $"{frame} <- {ours}";
            ZoneLog.Error($"enter load failed character={session.ObjectId}: {ex}");
            return new EnteringPlayer
            {
                Failure = $"enter refused: character {session.ObjectId} load failed: {ex.GetType().Name}: {ex.Message}"
                    + (frame.Length == 0 ? "" : $" ({frame})")
            };
        }
        finally
        {
            _enterLoads.Release();
        }
    }

    EnteringPlayer LoadEnteringPlayer(ZoneSession session)
    {
        if (session.ObjectId <= 0)
            return new EnteringPlayer { Failure = $"enter refused: character id {session.ObjectId} is not bound" };

        if (_accounts != null)
        {
            if (!_snapshots.DrainOwner(CharacterPersistWorker.CharacterDomain, session.ObjectId, TimeSpan.FromSeconds(2))
                || !_snapshots.DrainDomain(CharacterPersistWorker.PetDomain, TimeSpan.FromSeconds(2)))
            {
                return new EnteringPlayer
                {
                    Failure = $"enter refused: character {session.ObjectId} snapshots did not drain within 2s"
                };
            }
        }

        long characterEpoch = _snapshots.OwnerEpoch(CharacterPersistWorker.CharacterDomain, session.ObjectId);
        long petEpoch = _snapshots.DomainEpoch(CharacterPersistWorker.PetDomain);
        CharacterRecord? stored = _accounts?.FindById(session.Account, session.ObjectId);
        // #984: a saved row naming an id the data no longer has refuses the enter with every name, before anything is
        // built. Loading without it would drop the row from the live state and the next save would delete it.
        Fineage.Zone.Persistence.StoredStateAudit.RequireLoadable(stored, _game, _henna);
        var entity = BuildEnteringPlayer(session, stored);
        return new EnteringPlayer
        {
            Entity = entity,
            Stored = stored,
            CharacterEpoch = characterEpoch,
            PetEpoch = petEpoch
        };
    }

    ZoneEntity BuildEnteringPlayer(ZoneSession session, CharacterRecord? stored)
    {
        var spawn = SpawnFor(session, stored);
        var inventory = LoadInventory(session, stored);
        var vitals = LoadVitals(session, stored);
        var warehouse = new PrivateWarehouse(_game.Items, _itemIds);
        if (stored?.Warehouse != null)
            warehouse.Restore(stored.Warehouse.Select(item => new WarehouseRow(item.ItemId, item.Count, item.EnchantLevel, item.ObjectId)));
        LogWarehouseLoad("warehouse", session.ObjectId, warehouse);
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
        RefreshKnownSkills(entity);
        if (NeedsStarterInventory(stored))
            GrantStarterKitBody(entity);
        RefreshGearSkills(entity);
        RefreshPlayerMaxima(entity, fanout: false);
        RestoreStoredEffects(entity, stored);
        int penalty = WeightPenaltyLevelOf(entity);
        if (penalty != entity.WeightPenaltyLevel)
        {
            entity.WeightPenaltyLevel = penalty;
            RefreshRunSpeed(entity);
        }

        LoadShortcuts(entity, stored);
        LoadMacros(entity, stored);
        RefreshGradePenalty(entity);
        EnsureFreightLoaded(entity, stored);
        return entity;
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
        var (projection, snapshot, save) = await OnSimulationAsync(session, "leave", () =>
        {
            var before = projectBefore != null && TryGetLivePlayer(session, out var player) ? projectBefore(player) : null;
            TryDetachPlayer(session, capturePersist: true, out _, out var captured);
            CountOnlinePlayers();
            // Registered in the detach step, so an enter of this character inserting from now on sees the save (#1116).
            var registered = captured != null ? BeginLogoutSave(session.ObjectId) : null;
            return (before, captured, registered);
        }, ct).ConfigureAwait(false);
        if (snapshot == null)
            return projection;

        var unlinked = snapshot.Value;
        // The writes are synchronous and wait on Postgres, so they run on the logout lane while this handler awaits;
        // on the thread pool a reconnect storm parked every pool thread here (#1110).
        try
        {
            await _logoutPersist.RunAsync(() =>
            {
                try
                {
                    PersistUnlinkSnapshot(unlinked);
                    // Logout writes the pending snapshots, skipping backoff, so the leave does not return while they
                    // are still only queued. A failure is logged with the character id and stays pending.
                    FlushCharacterSnapshots(session.ObjectId);
                }
                finally
                {
                    // The character's own writes are done; the world sidecars below do not hold its enter.
                    EndLogoutSave(session.ObjectId, save!);
                }

                _sevenSigns.Engine.Save();
                _bosses.Schedule.Save();
            }).ConfigureAwait(false);
        }
        finally
        {
            EndLogoutSave(session.ObjectId, save!);
        }
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

    bool TryInsertEnteringPlayer(
        ZoneSession session,
        EnteringPlayer loaded,
        out int known,
        out int enterX,
        out int enterY,
        out int enterZ,
        out List<(UnlinkPersistSnapshot Snapshot, LogoutSave Save)>? evictedForReload,
        out bool stale)
    {
        using var tryTimer = Fineage.Zone.Net.EnterMetrics.MeasureTryEnter();
        known = 0;
        enterX = enterY = enterZ = 0;
        evictedForReload = null;
        stale = false;
        var entity = loaded.Entity;
        if (entity == null || session.ObjectId <= 0)
            return false;
        if (!TryPrepareSameSessionReenter(session, loaded.CharacterEpoch, loaded.PetEpoch, out stale))
            return false;

        long enterNow = NowUnixMs();
        if (PunishmentBlocksEnter(session, enterNow))
            return false;
        if (_byId.TryGetValue(session.ObjectId, out var occupied) && occupied.Kind != ZoneEntityKind.Player)
            return false;

        else if (_byId.TryGetValue(session.ObjectId, out var current) &&
                 current.Session != null &&
                 !ReferenceEquals(current.Session, session) &&
                 TryDetachPlayer(current.Session, capturePersist: true, out _, out var evicted))
        {
            // The load ran while that session was live, so it misses whatever that session changed. Evict it,
            // register its save, and load again once the save lands instead of inserting the stale body.
            CountOnlinePlayers();
            evictedForReload = [(evicted!.Value, BeginLogoutSave(session.ObjectId))];
            stale = true;
            return false;
        }

        // A logout save registered after the enter's wait began, or economy not durable yet: the load may be older
        // than the store is about to be. Load again after it lands (#1116). A live holder was evicted just above.
        if (PreviousSaveInFlight(session.ObjectId))
        {
            stale = true;
            return false;
        }

        // On the simulation thread (#1116): the clan actor's penalty maps are plain dictionaries the step owns. Written
        // from the parallel enter loads, a reconnect storm corrupted them and every later enter threw.
        RememberPenalties(session, loaded.Stored);
        _byId[entity.ObjectId] = entity;
        if (entity.Abnormals.Count > 0)
            _byId.Join(entity, ActiveSets.WithAbnormals);
        // After any previous session for this id has left: leave unloads the lists, and the new player needs its own.
        LoadSocialLists(entity, loaded.Stored);
        // These read the live directory (partner, pledge class). The entity is in it now, and only this thread may read it.
        ApplyWeddingOnEnter(entity);
        SyncClanStanding(entity);
        // No-restart oust and rule-zone membership are world admission. Oust moves the body before AOI enter.
        ApplyNoRestartOnLogin(entity, loaded.Stored?.LastAccess);
        UpdateRuleZoneState(entity);
        // Login-created characters already have the kit in the store; this still posts
        // the create grant if the journal has never seen the account.
        ApplyStarterKitLedger(entity);
        if (LoadTestCombatProfileNeeded(entity))
            TryApplyLoadTestCombatProfile(entity);
        // The enter ItemList is version 1. The client treats 0 as "no version" and would not track later updates.
        entity.Inventory?.EstablishCursor();
        _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
        session.InWorld = true;
        NoteInWorldPlayer(entity);
        _chat.Entered(entity.ObjectId, entity.Name);
        session.X = entity.X;
        session.Y = entity.Y;
        session.Z = entity.Z;
        enterX = entity.X;
        enterY = entity.Y;
        enterZ = entity.Z;

        // The enter sequence itself (UserInfo, ItemList, SkillList, spawns) is Net-only and lives in
        // NetZoneServer.OnEnterAsync; the knownlist is still built here because the sim owns it.
        BindKnownlistOnEnter(entity);

        // Region activation + NPC think scheduling runs on the combat/AI tick (see CollectNpcThink); skipping it here
        // avoids O(active-region NPCs) TrackThink work inside request:enter during bot ramp.

        AnnounceClanEnter(entity);
        FriendPlayerEnteredWorld(entity);
        RiftSanitizeLogin(entity);
        SyncPunishmentsOnEnter(session, entity, enterNow);
        CheckCursedWeaponOnEnter(entity);
        TryProvisionLoadTestEconomyHammer(session, entity);
        known = entity.Knownlist.Count;
        return true;
    }

    void BindKnownlistOnEnter(ZoneEntity entity)
    {
        using var aoiTimer = Fineage.Zone.Net.EnterMetrics.MeasureTryAoiBind();
        bool selfIsGm = entity.Kind == ZoneEntityKind.Player && ZoneAdmin.IsAdminCharacter(entity.Name);
        foreach (int id in _aoi.GetVisible(entity.ObjectId))
        {
            if (!_byId.TryGetValue(id, out var other) || other.InstanceId != entity.InstanceId)
                continue;
            if (other.Kind == ZoneEntityKind.Player && other.Session is not { InWorld: true })
                continue;
            // Same hide rule as CollectVisible / LinkSeen: invisible entities stay off a non-GM knownlist.
            if (other.Invisible && !selfIsGm)
                continue;
            entity.Knownlist.Add(id);
            TrackWatcher(entity.ObjectId, id);
            if (other.Session != null)
            {
                if (!(entity.Invisible && !ZoneAdmin.IsAdminCharacter(other.Name)))
                {
                    other.Knownlist.Add(entity.ObjectId);
                    TrackWatcher(id, entity.ObjectId);
                }
            }
            else
                TrackThink(other);
        }

        // Enter bound the knownlist at this position; later move starts compare their destination cell to this
        // anchor instead of the AOI store so a boundary crossed before the first tick is not skipped.
        StampKnownlistRefreshAnchor(entity);
    }

}
