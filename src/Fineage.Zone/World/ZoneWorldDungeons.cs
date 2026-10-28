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

    public async Task<string?> TryDungeonAsync(ZoneSession session, DungeonEnterRequestMsg request, CancellationToken ct)
    {
        var toSelf = new List<Pending>();
        var toOthers = new List<Pending>();
        string? error;
        lock (_gate)
            error = ApplyDungeon(session, request, toSelf, toOthers);
        if (toSelf.Count > 0)
            await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        if (toOthers.Count > 0)
            await FlushOthersAsync(toOthers, ct).ConfigureAwait(false);
        return error;
    }

    string? ApplyDungeon(ZoneSession session, DungeonEnterRequestMsg request, List<Pending> toSelf, List<Pending> toOthers)
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
            Relocate(player, site.ExitX, site.ExitY, site.ExitZ, toSelf, toOthers);
            player.DungeonId = 0;
            toSelf.Add(Pack(session, MsgType.DungeonEntered, Entered(site, inside: false, DungeonProtocol.ActionExit)));
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
            QueueNotices(session, _dungeonAccess.RefusalLines(code, site.Id), toSelf);
            return DungeonAccess.ToWire(code);
        }

        if (site.FeeCount > 0)
        {
            var changes = new List<InventoryUpdateEntryMsg>();
            if (!Take(player.Inventory, site.FeeItemId, site.FeeCount, changes))
            {
                QueueNotices(session, _dungeonAccess.RefusalLines(DungeonAccessCode.FeeRequired, site.Id), toSelf);
                return DungeonProtocol.Fee;
            }

            if (changes.Count > 0)
            {
                toSelf.Add(Pack(session, MsgType.InventoryUpdate, new InventoryUpdateMsg { Changes = changes.ToArray() }));
                SaveInventory(session, player.Inventory);
            }
        }

        Relocate(player, site.EntranceX, site.EntranceY, site.EntranceZ, toSelf, toOthers);
        player.DungeonId = site.Id;
        toSelf.Add(Pack(session, MsgType.DungeonEntered, Entered(site, inside: true, DungeonProtocol.ActionEnter)));
        return null;
    }

    void EvictDungeonLosers(List<Pending> outbound)
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

            var toSelf = new List<Pending>();
            var toOthers = new List<Pending>();
            Relocate(player, site.ExitX, site.ExitY, site.ExitZ, toSelf, toOthers);
            player.DungeonId = 0;
            toSelf.Add(Pack(player.Session, MsgType.SystemMessage, new SystemMessageMsg
            {
                Id = notice.Id,
                Params = [],
                Text = notice.Text
            }));
            toSelf.Add(Pack(player.Session, MsgType.DungeonEntered, Entered(site, inside: false, DungeonProtocol.ActionKick)));
            outbound.AddRange(toSelf);
            outbound.AddRange(toOthers);
            ZoneLog.Info($"dungeon kick object={player.ObjectId} dungeon={site.Id} -> {site.ExitX},{site.ExitY},{site.ExitZ}");
        }
    }

    void Relocate(ZoneEntity entity, int x, int y, int z, List<Pending> toSelf, List<Pending> toOthers)
    {
        var session = entity.Session;
        if (session == null)
            return;

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
        entity.X = x;
        entity.Y = y;
        entity.Z = z;
        session.X = x;
        session.Y = y;
        session.Z = z;
        _aoi.Move(entity.ObjectId, x, y);
        SavePresence(session, x, y, z, touchAccess: false);

        var after = new HashSet<int>();
        foreach (int id in _aoi.GetVisible(entity.ObjectId))
            after.Add(id);

        toSelf.Add(Pack(session, MsgType.MoveToAck, ack));
        foreach (int id in SortedExcept(after, before))
            LinkSeen(entity, id, toSelf, toOthers);
        foreach (int id in SortedShared(after, before))
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
                toOthers.Add(Pack(other.Session, MsgType.MoveToAck, ack));
        }

        foreach (int id in SortedExcept(before, after))
            Forget(entity, id, toSelf, toOthers);
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

    static void QueueNotices(ZoneSession session, IReadOnlyList<DungeonNotice> notices, List<Pending> toSelf)
    {
        foreach (var notice in notices)
        {
            toSelf.Add(Pack(session, MsgType.SystemMessage, new SystemMessageMsg
            {
                Id = notice.Id,
                Params = [],
                Text = notice.Text
            }));
        }
    }
}
