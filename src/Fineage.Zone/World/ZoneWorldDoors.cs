using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Siege;

namespace Fineage.Zone.World;

/// <summary>
/// Shared door pipeline: doorman bypasses, agit-wide open/close, auto-close timers, and clan-hall siege door respawn.
/// </summary>
public sealed partial class ZoneWorld
{
    const int DoormanRange = 250;

    /// <summary>Opens or closes one live door by Mobius id when geo + entity exist.</summary>
    internal bool TrySetDoorOpen(int doorId, bool open)
    {
        if (Doors.Get(doorId) == null)
        {
            ZoneLog.Info($"[door] missing data/doors.json row id={doorId}");
            return false;
        }

        SetGeoDoorOpen(doorId, open);
        if (_liveDoors.TryGetValue(doorId, out int objectId) && _byId.TryGetValue(objectId, out var door))
            FanoutDoorStatus(door);
        return true;
    }

    void OperateDoors(IEnumerable<int> doorIds, bool open)
    {
        foreach (int doorId in doorIds)
            TrySetDoorOpen(doorId, open);
    }

    void OperateHallDoors(int hallId, bool open)
    {
        foreach (var geo in Doors.ForClanHall(hallId))
            TrySetDoorOpen(geo.Id, open);
    }

    void RespawnHallDoors(int hallId, bool weak)
    {
        foreach (var (doorId, objectId) in _liveDoors)
        {
            if (!_byId.TryGetValue(objectId, out var door) || door.SiegeNpc is not { } info || info.HallId != hallId)
                continue;
            door.MaxHp = info.BaseHp;
            door.CurHp = weak ? (int)(door.MaxHp * SiegeProtocol.WeakDoorHpRatio) : door.MaxHp;
            door.Dead = false;
            SetGeoDoorOpen(doorId, false);
            FanoutDoorStatus(door);
        }
    }

    void TickDoorAutoClose(long now)
    {
        if (_doorAutoCloseAtMs.Count > 0)
        {
            foreach (var (doorId, when) in _doorAutoCloseAtMs.ToArray())
            {
                if (now < when)
                    continue;
                _doorAutoCloseAtMs.Remove(doorId);
                if (Doors.Get(doorId)?.Open == true)
                    TrySetDoorOpen(doorId, false);
            }
        }

        if (_doorCycleNextMs.Count == 0)
            return;
        foreach (var (doorId, when) in _doorCycleNextMs.ToArray())
        {
            if (now < when)
                continue;
            var geo = Doors.Get(doorId);
            if (geo == null || !IsCyclicDoor(geo))
            {
                _doorCycleNextMs.Remove(doorId);
                continue;
            }

            TrySetDoorOpen(doorId, !geo.Open);
        }
    }

    int HallNearNpc(int npcX, int npcY)
    {
        int found = 0;
        long best = long.MaxValue;
        foreach (var geo in Doors.All.Where(d => d.ClanHallId > 0))
        {
            long dx = geo.X - npcX;
            long dy = geo.Y - npcY;
            long dist = dx * dx + dy * dy;
            if (dist > 500L * 500 || dist >= best)
                continue;
            best = dist;
            found = geo.ClanHallId;
        }

        return found;
    }

    int CastleOfDoor(int doorId) => CastleTable.DoorCastle(doorId);

    bool CastleSiegeBlocksDoors(int castleId) =>
        castleId > 0 && _siege.Board.Find(castleId)?.InProgress == true;

    bool HallSiegeBlocksDoors(int hallId) =>
        hallId > 0 && _chSiege.Board.Find(hallId)?.InProgress == true;

    /// <summary>Mobius <c>Doorman</c> / <c>ClanHallDoorman</c> / <c>CastleDoorman</c> <c>open_doors</c> / <c>close_doors</c>.</summary>
    internal string? TryDoormanDoors(ZoneSession session, ZoneEntity player, ZoneEntity npc, string bypass, bool open)
    {
        if (!IsLive(session))
            return "world";
        if (!ZoneCombat.Within(player.X, player.Y, npc.X, npc.Y, DoormanRange))
            return NpcServiceProtocol.NotWarehouseText;

        var clan = ClanOf(player.ObjectId);
        if (clan == null)
            return ClanProtocol.None;

        bool clanHall = string.Equals(npc.NpcType, "ClanHallDoorman", StringComparison.Ordinal);
        if (clanHall)
        {
            int hallId = HallNearNpc(npc.X, npc.Y);
            if (hallId <= 0)
            {
                ZoneLog.Info($"[door] ClanHallDoorman npc={npc.NpcId} has no clanHallId door within 500 units");
                return SiegeProtocol.Door;
            }

            if (HallOwnerClanId(hallId) != clan.Id)
                return SiegeProtocol.Rights;
            if (HallSiegeBlocksDoors(hallId))
                return SiegeProtocol.Progress;
            if (clan.LeaderId != player.ObjectId && !HasPriv(clan, player.ObjectId, PledgeProtocol.PrivHallOpenDoor))
                return SiegeProtocol.Rights;
            OperateHallDoors(hallId, open);
            return null;
        }

        // CastleDoorman and generic Doorman list explicit door ids in the bypass.
        if (clan.LeaderId != player.ObjectId && !HasPriv(clan, player.ObjectId, PledgeProtocol.PrivCastleOpenDoor))
            return SiegeProtocol.Rights;

        var doorIds = ParseDoorIdsFromBypass(bypass);
        if (doorIds.Count == 0)
            return SiegeProtocol.Door;

        int? castleId = null;
        foreach (int doorId in doorIds)
        {
            int castle = CastleOfDoor(doorId);
            int hall = Doors.Get(doorId)?.ClanHallId ?? 0;
            if (castle <= 0 && hall <= 0)
            {
                ZoneLog.Info($"[door] bypass door id={doorId} is not linked to a castle or clan hall");
                return SiegeProtocol.Door;
            }

            if (castle > 0)
            {
                if (clan.CastleId != castle)
                    return SiegeProtocol.Rights;
                if (CastleSiegeBlocksDoors(castle))
                    return SiegeProtocol.Progress;
                castleId ??= castle;
            }
            else if (hall > 0)
            {
                if (HallOwnerClanId(hall) != clan.Id)
                    return SiegeProtocol.Rights;
                if (HallSiegeBlocksDoors(hall))
                    return SiegeProtocol.Progress;
            }
        }

        OperateDoors(doorIds, open);
        return null;
    }

    internal void SetClanHallOwnerForTests(int hallId, int clanId)
    {
        using (_affinity.Step())
        {
            long now = NowUnixMs();
            var halls = Halls(now);
            if (!halls.TryGetValue(hallId, out var hall))
            {
                hall = new Accounts.ClanHallSave { Id = hallId };
                halls[hallId] = hall;
            }

            hall.OwnerClanId = clanId;
            SaveHalls();
            if (_clan.TryGet(clanId, out var clan))
            {
                clan.D.HallId = hallId;
                SaveClan(clan);
                SyncHallRegenForClan(clan);
            }
        }
    }

    internal void TickDoorAutoCloseForTests(long now)
    {
        using (_affinity.Step())
            TickDoorAutoClose(now);
    }

    static List<int> ParseDoorIdsFromBypass(string bypass)
    {
        var list = new List<int>();
        foreach (var tok in bypass.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (tok.Equals("open_doors", StringComparison.OrdinalIgnoreCase) ||
                tok.Equals("close_doors", StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(tok, out int id) && id > 0)
                list.Add(id);
        }

        return list;
    }
}