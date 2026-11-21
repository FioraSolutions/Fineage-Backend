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
        const int radius = 1200;
        int found = 0;
        long best = long.MaxValue;
        foreach (var door in Doors.All.Where(d => d.ClanHallId > 0))
        {
            long dx = door.X - x;
            long dy = door.Y - y;
            long dist = dx * dx + dy * dy;
            if (dist > (long)radius * radius || dist >= best)
                continue;
            best = dist;
            found = door.ClanHallId;
        }

        return found;
    }

    AgitDecoInfoNetMsg BuildAgitDecoInfo(ClanHallSave hall)
    {
        HallCatalog.TryGet(hall.Id, out var info);
        return new AgitDecoInfoNetMsg
        {
            HallId = hall.Id,
            DecoBytes = Clan.AgitDecoInfo.Encode(hall, info?.Grade ?? 0)
        };
    }

    void SendAgitDecoInfo(ZoneSession session, ClanHallSave hall) =>
        NetBridge?.FanoutAgitDecoInfo([session.ObjectId], BuildAgitDecoInfo(hall));

    void BroadcastAgitDecoInHall(ClanHallSave hall)
    {
        var watchers = PlayersInsideHall(hall.Id);
        if (watchers.Count == 0 || NetBridge == null)
            return;
        NetBridge.FanoutAgitDecoInfo(watchers, BuildAgitDecoInfo(hall));
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