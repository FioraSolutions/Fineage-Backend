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
    /// Sanity clamp for one client move request (L2 units). This is a "is the packet plausible" guard,
    /// not a region boundary: click-to-move crosses a whole village, and every spawn map shares it.
    /// See <see cref="MoveLimits"/>.
    /// </summary>
    static bool WithinMoveRequest(int fromX, int fromY, int toX, int toY) =>
        MoveLimits.WithinStep(fromX, fromY, toX, toY);

    string? TryMove(ZoneSession session, int destX, int destY, int destZ)
    {
        if (!IsLive(session))
            return "world";

        var entity = _byId[session.ObjectId];
        if (entity.Dead)
            return InventoryProtocol.Dead;
        if (IsImmobilized(entity))
            return ActionProtocol.Immobilized;
        if (entity.Sitting)
            return ActionProtocol.Sitting;

        // Origin/ObjectId filled here so Net and tests only pass destination; MoveToMsg remains the internal/ack shape.
        var move = new MoveToMsg
        {
            ObjectId = session.ObjectId,
            DestX = destX,
            DestY = destY,
            DestZ = destZ,
            OriginX = session.X,
            OriginY = session.Y,
            OriginZ = session.Z
        };

        if (entity.CastingUntilMs > _clock().ToUnixTimeMilliseconds())
        {
            // Mobius PlayerAI.onIntentionMoveTo: casting → ActionFailed + saveNextIntention(MOVE_TO); runs after the cast.
            session.QueuedMove = move;
            return null;
        }
        session.QueuedMove = null;
        // Sanity clamp for a single client move request, not a region gate: a click can be far away
        // (click-to-move across a village) and the same code runs for every spawn map.
        if (!WithinMoveRequest(entity.X, entity.Y, move.DestX, move.DestY))
            return "range";

        if (IsApproachSync(session, move))
        {
            // Client reporting its run along our moveToPawn path: keep the sticky approach and server dest.
            return null;
        }
        CancelAttackIntentionOnMove(session, move);
        if (NetBridge != null)
            return StartTimedMove(entity, session, move);

        // No Net client attached (unit-level world / selftest): keep the old snap so the caller's position is final.
        entity.X = move.DestX;
        entity.Y = move.DestY;
        entity.Z = move.DestZ;
        session.X = entity.X;
        session.Y = entity.Y;
        session.Z = entity.Z;
        ReconcileKnownlist(entity, ack: null);
        return null;
    }

    /// <summary>
    /// After <paramref name="entity"/> moved: AOI move, CharInfo to newly visible, MoveToAck to self + stayed,
    /// DeleteObject only past the forget radius and never for a pair locked in combat (Mobius knownlist).
    /// </summary>
    void ReconcileKnownlist(ZoneEntity entity, MoveToMsg? ack)
    {
        var before = new HashSet<int>(entity.Knownlist);
        _aoi.Move(entity.ObjectId, entity.X, entity.Y);

        var after = new HashSet<int>();
        CollectVisible(entity, after);
        foreach (int id in before)
        {
            if (!after.Contains(id) && _byId.TryGetValue(id, out var other) && other.InstanceId == entity.InstanceId &&
                (_aoi.StillKnown(entity.ObjectId, id) || CombatPinned(entity, other)))
                after.Add(id);
        }

        List<int>? netWatchers = null;
        if (NetBridge != null && entity.Session != null && ack != null)
        {
            netWatchers = [entity.ObjectId];
        }

        foreach (int id in SortedExcept(after, before))
            LinkSeen(entity, id);
        foreach (int id in SortedShared(after, before))
        {
            if (ack != null && _byId.TryGetValue(id, out var other) && other.Session != null)
                netWatchers?.Add(other.ObjectId);
        }

        foreach (int id in SortedExcept(before, after))
            Forget(entity, id);

        if (netWatchers is { Count: > 0 } && NetBridge != null && ack != null)
        {
            NetBridge.FanoutMoveState(
                netWatchers,
                entity.ObjectId,
                ack.OriginX, ack.OriginY, ack.OriginZ,
                ack.DestX, ack.DestY, ack.DestZ,
                entity.Running);
        }
    }

    static bool CombatPinned(ZoneEntity a, ZoneEntity b) =>
        (a.Session != null && (a.Session.AutoAttackTargetId == b.ObjectId || a.Session.SelectedTargetId == b.ObjectId)) ||
        (b.Session != null && (b.Session.AutoAttackTargetId == a.ObjectId || b.Session.SelectedTargetId == a.ObjectId)) ||
        a.AggroTargetId == b.ObjectId || b.AggroTargetId == a.ObjectId;

    void LinkSeen(ZoneEntity self, int otherId)
    {
        if (self.Session == null || !_byId.TryGetValue(otherId, out var other))
            return;

        self.Knownlist.Add(otherId);
        if (other.Session != null)
            other.Knownlist.Add(self.ObjectId);
    }

    void Forget(ZoneEntity self, int otherId)
    {
        if (self.Session == null)
            return;

        self.Knownlist.Remove(otherId);
        if (_byId.TryGetValue(otherId, out var other))
            other.Knownlist.Remove(self.ObjectId);
    }

    /// <returns>False when nothing was detached (stale session / object id).</returns>
    bool TryDetachPlayer(
        ZoneSession session,
        bool capturePersist,
        out ZoneEntity? detached,
        out UnlinkPersistSnapshot? snapshot)
    {
        detached = null;
        snapshot = null;
        if (session.ObjectId == 0 || !_byId.TryGetValue(session.ObjectId, out var entity))
        {
            session.InWorld = false;
            return false;
        }

        if (!ReferenceEquals(entity.Session, session))
        {
            session.InWorld = false;
            return false;
        }

        foreach (int id in entity.Knownlist)
        {
            if (!_byId.TryGetValue(id, out var other))
                continue;
            other.Knownlist.Remove(session.ObjectId);
        }

        NoteOlympiadDisconnect(entity);

        entity.Knownlist.Clear();
        _aoi.Leave(session.ObjectId);
        _byId.Remove(session.ObjectId);
        ForgetActiveSets(session.ObjectId);
        session.InWorld = false;

        PartyPlayerLeftWorld(session.ObjectId);
        TradePlayerLeftWorld(session.ObjectId);
        PrivateStorePlayerLeftWorld(session.ObjectId);
        FriendPlayerLeftWorld(session.ObjectId);
        ClanPlayerLeftWorld(session.ObjectId, session.Name);

        detached = entity;
        if (capturePersist)
            snapshot = new UnlinkPersistSnapshot(session, entity);

        // The Net despawn for the leaver is owned by NetZoneServer.LeaveNetAsync (it knows the connection's
        // EntityId and unregisters the presence); fanning it here too would double-remove the same entity.
        return true;
    }

    /// <summary>Inventory / warehouse / vitals / quests / presence — must run outside <c>_gate</c>.</summary>
    void PersistUnlinkSnapshot(UnlinkPersistSnapshot snapshot)
    {
        var session = snapshot.Session;
        var entity = snapshot.Entity;
        if (entity.Inventory != null)
            _persist.ReplaceItemsFromInventory(session.Account, session.ObjectId, entity.Inventory);
        SaveWarehouse(session, entity);
        SaveVitals(session, entity);
        SaveQuestProgress(session, entity);
        SaveEffects(session, entity);
        SavePresence(session, entity.X, entity.Y, entity.Z, touchAccess: false);
    }

    ClassSpawn.Point SpawnFor(ZoneSession session, CharacterRecord? stored = null)
    {
        if (stored is { HasPosition: true })
            return new ClassSpawn.Point(stored.X, stored.Y, stored.Z);

        if (!PlayerClasses.TryGet(session.ClassId, out var template) || template == null || template.Spawns.Count == 0)
            return new ClassSpawn.Point(TalkingIslandSpawn.X, TalkingIslandSpawn.Y, TalkingIslandSpawn.Z);
        var picked = template.Spawns[_spawnRandom.Next(template.Spawns.Count)];
        return new ClassSpawn.Point(picked.X, picked.Y, picked.Z);
    }

    ClassCatalog PlayerClasses => _game.Classes ?? ClassTemplates.Catalog;

    (float run, float walk) MoveSpeed(int classId)
    {
        if (PlayerClasses.TryGet(classId, out var template) && template != null)
            return (template.RunSpeed, template.WalkSpeed);
        return (120f, 80f);
    }

    /// <summary>Mobius <c>getDEX()</c> from the class template — drives the <c>FuncMoveSpeed</c> speed multiplier.</summary>
    int DexOf(int classId) =>
        PlayerClasses.TryGet(classId, out var template) && template != null ? template.Dex : 30;

    const long PresenceSaveIntervalMs = 3000;
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _lastPresenceSaveMs = new();

    /// <summary>Throttled save of the move destination so a Zone restart mid-session keeps the player's spot.</summary>
    void MaybeSavePresenceOnMove(ZoneSession session)
    {
        if (!_persist.Enabled || session.ObjectId <= 0)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        long last = _lastPresenceSaveMs.GetValueOrDefault(session.ObjectId, long.MinValue / 2);
        if (now - last < PresenceSaveIntervalMs)
            return;
        _lastPresenceSaveMs[session.ObjectId] = now;
        SavePresence(session, session.X, session.Y, session.Z, touchAccess: false);
    }

    void SavePresence(ZoneSession session, int x, int y, int z, bool touchAccess) =>
        _persist.SavePresence(
            session.Account,
            session.ObjectId,
            x,
            y,
            z,
            touchAccess ? DateTimeOffset.UtcNow : null);

    PlayerInventory LoadInventory(ZoneSession session, CharacterRecord? stored = null)
    {
        if (stored == null)
            return _game.CreateStarterInventory(session.ClassId, _itemIds);
        // Items == null was backfilled off-lock in TryEnterAsync; treat missing as starter in-memory only.
        if (stored.Items == null)
            return _game.CreateStarterInventory(session.ClassId, _itemIds);

        return CharacterInventory.Restore(_game.Items, _itemIds, stored.Items);
    }

    void SaveInventory(ZoneSession session, PlayerInventory inventory)
    {
        // Mobius PlayerInventory.refreshWeight() → _owner.refreshOverloaded(): every inventory write funnels through
        // here, so the overload level is recomputed exactly when the load changes (pickup, drop, shop, trade,
        // warehouse, enchant, equip/unequip, quest reward, sweep, shots).
        RefreshWeightPenaltyAfterInventoryChange(session);
        if (_deferInventoryPersist)
        {
            _dirtyInventories[session.ObjectId] = (session, inventory);
            return;
        }

        _persist.ReplaceItemsFromInventory(session.Account, session.ObjectId, inventory);
    }

    /// <summary>Flush inventory/warehouse rows queued while <see cref="_deferInventoryPersist"/> was set. Call outside <c>_gate</c>.</summary>
    void FlushDirtyInventories()
    {
        if (_dirtyInventories.Count > 0)
        {
            foreach (var (_, pair) in _dirtyInventories)
                _persist.ReplaceItemsFromInventory(pair.Session.Account, pair.Session.ObjectId, pair.Inventory);
            _dirtyInventories.Clear();
        }

        if (_dirtyWarehouses.Count == 0)
            return;
        foreach (var player in _dirtyWarehouses.Values)
        {
            if (player.Session == null)
                continue;
            ReplaceWarehouseNow(player.Session, player);
        }

        _dirtyWarehouses.Clear();
    }

    /// <summary>Run <paramref name="underGate"/> under <c>_gate</c> with inventory/warehouse Postgres deferred until after unlock.</summary>
    T WithDeferredInventoryPersist<T>(Func<T> underGate)
    {
        T result;
        lock (_gate)
        {
            _deferInventoryPersist = true;
            try
            {
                result = underGate();
            }
            finally
            {
                _deferInventoryPersist = false;
            }
        }

        FlushDirtyInventories();
        return result;
    }

    bool IsLive(ZoneSession session) =>
        session.InWorld &&
        session.ObjectId != 0 &&
        _byId.TryGetValue(session.ObjectId, out var entity) &&
        ReferenceEquals(entity.Session, session);

    List<ZoneEntity> OrderedVisible(int objectId)
    {
        var visible = new List<ZoneEntity>();
        if (!_byId.TryGetValue(objectId, out var self))
            return visible;
        foreach (int id in _aoi.GetVisible(objectId))
        {
            if (_byId.TryGetValue(id, out var entity) && entity.InstanceId == self.InstanceId)
                visible.Add(entity);
        }

        visible.Sort(static (a, b) =>
        {
            int kind = a.Kind.CompareTo(b.Kind);
            return kind != 0 ? kind : a.ObjectId.CompareTo(b.ObjectId);
        });
        return visible;
    }

    List<ZoneEntity> Players()
    {
        var players = new List<ZoneEntity>();
        foreach (var entity in _byId.Values)
        {
            if (entity.Kind == ZoneEntityKind.Player && entity.Session is { InWorld: true })
                players.Add(entity);
        }

        return players;
    }

    static List<int> SortedExcept(HashSet<int> source, HashSet<int> other)
    {
        var ids = new List<int>();
        foreach (int id in source)
        {
            if (!other.Contains(id))
                ids.Add(id);
        }

        ids.Sort();
        return ids;
    }

    static List<int> SortedShared(HashSet<int> source, HashSet<int> other)
    {
        var ids = new List<int>();
        foreach (int id in source)
        {
            if (other.Contains(id))
                ids.Add(id);
        }

        ids.Sort();
        return ids;
    }

    InventoryMessages.UserInfoVitals BuildUserInfoVitals(ZoneEntity entity)
    {
        int level = Math.Max(1, entity.Level);
        int maxCp = 0;
        int str = 0, dex = 0, con = 0, intel = 0, wit = 0, men = 0;
        int pAtk = 0, mAtk = 0, pDef = 0, mDef = 0, pAtkSpd = 0, mAtkSpd = 0, accuracy = 0, crit = 0, evasion = 0;
        if (PlayerClasses.TryGet(entity.ClassId, out var template) && template != null)
        {
            try { maxCp = template.ModifiedCp(level); }
            catch (ArgumentOutOfRangeException) { maxCp = template.MaxCp; }
            str = template.Str + entity.HennaStr;
            dex = template.Dex + entity.HennaDex;
            con = template.Con + entity.HennaCon;
            intel = template.Int + entity.HennaInt;
            wit = template.Wit + entity.HennaWit;
            men = template.Men + entity.HennaMen;
            // Same pipeline combat uses (Mobius stat funcs), so the client status window matches the server.
            var stats = PlayerStats(entity);
            pAtk = stats.PAtk;
            mAtk = stats.MAtk;
            pDef = stats.PDef;
            mDef = stats.MDef;
            pAtkSpd = stats.PAtkSpd;
            mAtkSpd = stats.MAtkSpd;
            accuracy = stats.Accuracy;
            crit = stats.CritRate;
            evasion = stats.Evasion;
        }

        return new InventoryMessages.UserInfoVitals(
            level,
            entity.CurHp,
            entity.MaxHp,
            entity.CurMp,
            entity.MaxMp,
            maxCp,
            maxCp,
            entity.Exp,
            entity.Sp,
            str, dex, con, intel, wit, men,
            pAtk, mAtk, pDef, mDef, pAtkSpd, mAtkSpd,
            accuracy, crit, evasion);
    }

    static NpcInfoMsg NpcInfo(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        NpcId = entity.NpcId,
        Name = entity.Name,
        X = entity.X,
        Y = entity.Y,
        Z = entity.Z,
        Heading = entity.Heading,
        Attackable = entity.Attackable,
        RunSpeed = entity.RunSpeed,
        WalkSpeed = entity.WalkSpeed,
        Title = entity.Title,
        NpcType = entity.NpcType
    };

    /// <summary>
    /// Moves a live player to <paramref name="level"/> and runs the same reward-skills refresh a level up does, so a
    /// test can assert the book and the SkillList fanout without grinding exp.
    /// </summary>
    internal bool TrySetPlayerLevelForTests(ZoneSession session, int level)
    {
        ZoneEntity player;
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var entity) || entity.Inventory == null)
                return false;
            entity.Level = Math.Max(1, level);
            RefreshKnownSkills(entity);
            player = entity;
        }

        // The client only learns the new book from a SkillList, exactly as a level up sends it.
        NetBridge?.FanoutSkillList([session.ObjectId],
            Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
        return true;
    }

    internal NpcInfoMsg NpcInfoByTemplateForTests(int npcId)
    {
        lock (_gate)
        {
            ZoneEntity? found = null;
            foreach (var entity in _byId.Values)
            {
                if (entity.Kind != ZoneEntityKind.Npc || entity.NpcId != npcId)
                    continue;
                if (found == null || entity.ObjectId < found.ObjectId)
                    found = entity;
            }

            if (found == null)
                throw new KeyNotFoundException($"NPC template {npcId} is not spawned.");
            return NpcInfo(found);
        }
    }

    internal static SpawnItemMsg SpawnItem(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        ItemId = entity.GroundItemId,
        X = entity.X,
        Y = entity.Y,
        Z = entity.Z,
        Stackable = entity.GroundStackable,
        Count = entity.GroundCount
    };

}
