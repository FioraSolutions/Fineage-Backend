using Fineage.Accounts;
using Fineage.Net.Contract;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void TickHallDecoEnter()
    {
        if (HallCatalog.Halls.Count == 0)
            return;
        long now = NowUnixMs();
        var halls = Halls(now);
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            int hallId = HallAtPosition(player.X, player.Y);
            if (hallId == player.InClanHallZoneId)
                continue;
            player.InClanHallZoneId = hallId;
            if (hallId <= 0 || !halls.TryGetValue(hallId, out var hall) || hall.OwnerClanId <= 0)
                continue;
            SendAgitDecoInfo(player.Session, hall);
        }
    }

    int HallAtPosition(int x, int y)
    {
        int found = 0;
        long best = long.MaxValue;
        long limit = (long)HallDoorAreaRadius * HallDoorAreaRadius;
        foreach (var door in Doors.All)
        {
            if (door.ClanHallId <= 0)
                continue;
            long dx = door.X - x;
            long dy = door.Y - y;
            long dist = dx * dx + dy * dy;
            if (dist > limit || dist >= best)
                continue;
            best = dist;
            found = door.ClanHallId;
        }

        return found;
    }

    bool TryBuildAgitDecoInfo(ClanHallSave hall, out AgitDecoInfoNetMsg deco)
    {
        if (!HallCatalog.TryGet(hall.Id, out var info))
        {
            ZoneLog.Info($"[agit-deco] clan hall id={hall.Id} missing from data/clan-halls.json");
            deco = null!;
            return false;
        }

        deco = new AgitDecoInfoNetMsg
        {
            HallId = hall.Id,
            DecoBytes = Clan.AgitDecoInfo.Encode(hall, info.Grade)
        };
        return true;
    }

    void SendAgitDecoInfo(ZoneSession session, ClanHallSave hall)
    {
        if (NetBridge == null || !TryBuildAgitDecoInfo(hall, out var deco))
            return;
        NetBridge.FanoutAgitDecoInfo([session.ObjectId], deco);
    }

    void BroadcastAgitDecoInHall(ClanHallSave hall)
    {
        var watchers = PlayersInsideHall(hall.Id);
        if (watchers.Count == 0 || NetBridge == null || !TryBuildAgitDecoInfo(hall, out var deco))
            return;
        NetBridge.FanoutAgitDecoInfo(watchers, deco);
    }

    List<int> PlayersInsideHall(int hallId)
    {
        var ids = new List<int>();
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (HallAtPosition(player.X, player.Y) == hallId)
                ids.Add(player.ObjectId);
        }

        return ids;
    }

    internal void TickHallDecoEnterForTest()
    {
        using (_affinity.Step())
            TickHallDecoEnter();
    }
}