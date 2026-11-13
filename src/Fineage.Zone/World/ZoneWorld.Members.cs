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
        else
            TrackThink(other);
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

        var seen = entity.Knownlist.ToArray();
        foreach (int id in seen)
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
        foreach (int id in seen)
        {
            if (_byId.TryGetValue(id, out var other))
                ReleaseThink(other);
        }
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
}
