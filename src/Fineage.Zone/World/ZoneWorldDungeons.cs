using Fineage.Protocol;
using Fineage.Zone.Dungeons;
using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public int PlayerDungeonId(int objectId)
    {
        lock (_gate)
            return _byId.TryGetValue(objectId, out var entity) ? entity.DungeonId : 0;
    }

    public Task<string?> TryDungeonAsync(ZoneSession session, DungeonEnterRequestMsg request, CancellationToken ct)
    {
        // Enter fee / key consume → SaveInventory after unlock (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() => ApplyDungeon(session, request)));
    }

    string? ApplyDungeon(ZoneSession session, DungeonEnterRequestMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (request.Action is not (DungeonProtocol.ActionEnter or DungeonProtocol.ActionExit))
            return "bad_request";
        if (!_dungeons.TryGet(request.DungeonId, out var site))
            return DungeonProtocol.Unknown;

        if (request.Action == DungeonProtocol.ActionExit)
        {
            if (player.Dead)
                return InventoryProtocol.Dead;
            Relocate(player, site.ExitX, site.ExitY, site.ExitZ);
            player.DungeonId = 0;
            var left = Entered(site, inside: false, DungeonProtocol.ActionExit);
            NetBridge?.FanoutDungeonEntered([session.ObjectId], left);
            return null;
        }

        _dungeonAccess.Observe(_sevenSigns);
        var character = new DungeonCharacter(
            (SevenSignsCabal)_sevenSigns.PlayerCabal(session.ObjectId),
            player.Dead,
            itemId => player.Inventory.FindByItemId(itemId)?.Count ?? 0);
        var code = _dungeonAccess.CanEnter(character, site.Id);
        if (code != DungeonAccessCode.Allowed)
        {
            QueueNotices(session, _dungeonAccess.RefusalLines(code, site.Id));
            return DungeonAccess.ToWire(code);
        }

        if (site.FeeCount > 0)
        {
            var changes = new List<InventoryUpdateEntryMsg>();
            if (!Take(player.Inventory, site.FeeItemId, site.FeeCount, changes))
            {
                QueueNotices(session, _dungeonAccess.RefusalLines(DungeonAccessCode.FeeRequired, site.Id));
                return DungeonProtocol.Fee;
            }

            if (changes.Count > 0)
            {
                var update = new InventoryUpdateMsg { Changes = changes.ToArray() };
                NetBridge?.FanoutInventoryUpdate([session.ObjectId], update);
                SaveInventory(session, player.Inventory);
            }
        }

        Relocate(player, site.EntranceX, site.EntranceY, site.EntranceZ);
        player.DungeonId = site.Id;
        var inside = Entered(site, inside: true, DungeonProtocol.ActionEnter);
        NetBridge?.FanoutDungeonEntered([session.ObjectId], inside);
        return null;
    }

    void EvictDungeonLosers()
    {
        _dungeonAccess.Observe(_sevenSigns);
        var period = _sevenSigns.Period;
        var notice = DungeonAccess.KickNotice(period);
        foreach (var player in Players())
        {
            if (player.DungeonId == 0 || player.Session == null)
                continue;
            var cabal = (SevenSignsCabal)_sevenSigns.PlayerCabal(player.ObjectId);
            if (_dungeonAccess.MayRemain(cabal, player.DungeonId))
                continue;
            if (!_dungeons.TryGet(player.DungeonId, out var site))
            {
                player.DungeonId = 0;
                continue;
            }

            Relocate(player, site.ExitX, site.ExitY, site.ExitZ);
            player.DungeonId = 0;
            var line = new SystemMessageMsg
            {
                Id = notice.Id,
                Params = [],
                Text = notice.Text
            };
            var kicked = Entered(site, inside: false, DungeonProtocol.ActionKick);
            NetBridge?.FanoutSystemMessage([player.ObjectId], line);
            NetBridge?.FanoutDungeonEntered([player.ObjectId], kicked);
            ZoneLog.Info($"dungeon kick object={player.ObjectId} dungeon={site.Id} -> {site.ExitX},{site.ExitY},{site.ExitZ}");
        }
    }

    /// <summary>
    /// Mobius teleToLocation + knownlist reconcile. <paramref name="netTeleport"/> is false only when the
    /// caller fans the same relocation itself (the revive path already emits a TeleportToLocation event).
    /// </summary>
    void Relocate(ZoneEntity entity, int x, int y, int z, int? instanceId = null, bool persist = true, int? heading = null, bool netTeleport = true)
    {
        var session = entity.Session;
        if (session == null)
            return;
        // Mobius teleToLocation: abortCast + intention IDLE — no hit lands across a teleport, queued cast/pickup dropped.
        AbortCast(entity, "teleport", notify: false);
        if (heading is int face)
            entity.Heading = face;
        session.PendingPickupId = 0;
        session.PendingPickupAtUnixMs = 0;
        session.AutoAttackTargetId = 0;
        session.ClearApproach();
        // A running path must not keep ticking from the old cell after the snap.
        SetMoveActive(entity, false);
        entity.MovePath.Clear();

        var ack = new MoveToMsg
        {
            ObjectId = entity.ObjectId,
            DestX = x,
            DestY = y,
            DestZ = z,
            OriginX = entity.X,
            OriginY = entity.Y,
            OriginZ = entity.Z
        };

        var before = new HashSet<int>(entity.Knownlist);
        if (instanceId.HasValue)
            entity.InstanceId = instanceId.Value;
        entity.X = x;
        entity.Y = y;
        entity.Z = z;
        session.X = x;
        session.Y = y;
        session.Z = z;
        _aoi.Move(entity.ObjectId, x, y);
        if (persist)
            SavePresence(session, x, y, z, touchAccess: false);

        var after = new HashSet<int>();
        CollectVisible(entity, after);

        // Net: Mobius TeleportToLocation to the owner and every peer that held it in view.
        // MoveToAck above only reaches the (dormant) V2 stream, so Net needed its own relocation.
        var netWatchers = new List<int> { entity.ObjectId };
        var added = new List<int>();
        var removed = new List<int>();
        foreach (int id in SortedExcept(after, before))
        {
            LinkSeen(entity, id);
            added.Add(id);
        }
        foreach (int id in SortedShared(after, before))
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
            {
                netWatchers.Add(id);
            }
        }

        foreach (int id in SortedExcept(before, after))
        {
            Forget(entity, id);
            removed.Add(id);
        }

        // Mobius onTeleported rebuilds the knownlist. Without this the client keeps the old
        // square's bodies (or drops them with the tile) and never receives them again on the way back.
        if (added.Count > 0 || removed.Count > 0)
            NetBridge?.FanoutKnownlistDiff(entity.ObjectId, added, removed);

        if (netTeleport)
        {
            NetBridge?.FanoutTeleportToLocation(netWatchers, entity.ObjectId, x, y, z, entity.Heading);
        }
    }

    static DungeonEnteredMsg Entered(DungeonSite site, bool inside, int action) => new()
    {
        DungeonId = site.Id,
        X = inside ? site.EntranceX : site.ExitX,
        Y = inside ? site.EntranceY : site.ExitY,
        Z = inside ? site.EntranceZ : site.ExitZ,
        Inside = inside,
        Action = action
    };

    void QueueNotices(ZoneSession session, IReadOnlyList<DungeonNotice> notices)
    {
        foreach (var notice in notices)
        {
            var line = new SystemMessageMsg
            {
                Id = notice.Id,
                Params = [],
                Text = notice.Text
            };
            NetBridge?.FanoutSystemMessage([session.ObjectId], line);
        }
    }
}
