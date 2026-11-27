using System.Globalization;
using System.Text;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Inventory;
using Fineage.Zone.MonsterRace;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly MonsterRaceActor _monsterRace;
    readonly MonsterRaceSettings _monsterRaceSettings;
    PeaceZone[] _monsterRaceBroadcast = [];

    internal MonsterRaceBoard MonsterRace => _monsterRace.Board;

    /// <summary>Swap in the persisted board (host: <c>monster-race-state.json</c> in the state directory).</summary>
    public void UseMonsterRace(MonsterRaceBoard board)
    {
        using (_affinity.Step())
            _monsterRace.Use(board);
    }

    /// <summary>Production wiring for the persisted monster race sidecar.</summary>
    public void UseMonsterRaceInStateDirectory(string stateDirectory, long loadedAtUnixMs) =>
        UseMonsterRace(MonsterRaceBoard.OpenInStateDirectory(
            stateDirectory,
            loadedAtUnixMs,
            _game.Items,
            Npcs.NpcCatalog.LoadDefault(),
            () => _itemIds.Next(),
            _monsterRaceSettings));

    int AnnounceMonsterRace(IReadOnlyList<MonsterRaceEffect> batches)
    {
        if (batches.Count == 0)
            return 0;
        var watchers = DerbyWatcherIds();
        if (watchers.Length == 0)
            return batches.Count;

        foreach (var batch in batches)
        {
            foreach (var n in batch.Notices)
                NetBridge?.FanoutSystemMessage(watchers,
                    new SystemMessageMsg { Id = n.MessageId, Params = n.Params, Text = n.Text });
            if (batch.Packet is { } pkt)
                NetBridge?.FanoutMonRaceInfo(watchers, BuildMonRacePacket(pkt));
            foreach (var sound in batch.Sounds)
                NetBridge?.FanoutPlaySound(watchers, sound.Music ? 1 : 0, sound.File, 0, 0, 0, 0, 0);
            if (batch.Despawn is { } despawn)
            {
                foreach (int oid in despawn.ObjectIds)
                    NetBridge?.FanoutEntityDespawn(watchers, oid);
            }
        }

        return batches.Count;
    }

    int[] DerbyWatcherIds() =>
        Players()
            .Where(p => InDerbyBroadcast(p.X, p.Y, p.Z))
            .Select(p => p.ObjectId)
            .ToArray();

    bool InDerbyBroadcast(int x, int y, int z) =>
        ZoneRules.Query(x, y, z, race: 0).InDerby
        || (_monsterRaceBroadcast.Length > 0 && PeaceZones.Hit(_monsterRaceBroadcast, x, y, z));

    MonRaceInfoNetMsg BuildMonRacePacket(MonsterRacePacketOrder order)
    {
        var board = MonsterRace;
        var track = _monsterRaceSettings.Track;
        var runners = new MonRaceRunnerNetMsg[MonsterRaceProtocol.RunnerCount];
        for (int i = 0; i < runners.Length; i++)
        {
            var r = i < board.Runners.Count ? board.Runners[i] : new MonsterRaceRunner();
            int laneY = track.OriginYBase + track.LaneSpacing * (7 - i);
            byte[] speeds = new byte[20];
            if (order.SendSpeeds && i < board.Speeds.Length)
            {
                for (int j = 0; j < 20; j++)
                    speeds[j] = (byte)board.Speeds[i][j];
            }

            runners[i] = new MonRaceRunnerNetMsg
            {
                ObjectId = r.ObjectId,
                NpcIdPlusDisplay = r.NpcId + 1_000_000,
                OriginX = track.OriginX,
                OriginY = laneY,
                OriginZ = track.OriginZ,
                EndX = track.EndX,
                EndY = laneY,
                EndZ = track.OriginZ,
                CollisionHeight = r.CollisionHeight,
                CollisionRadius = r.CollisionRadius,
                Unknown120 = track.RunSpeedByte,
                Speeds = speeds
            };
        }

        return new MonRaceInfoNetMsg { CodeA = order.CodeA, CodeB = order.CodeB, Runners = runners };
    }

    string? MonsterRaceBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string command)
    {
        if (!_monsterRaceSettings.Enabled)
            return "bad_request";

        if (command.StartsWith("BuyTicket", StringComparison.Ordinal))
            return BuyRaceTicket(session, player, npc, ParseTail(command, "BuyTicket"));
        if (command.Equals("ShowOdds", StringComparison.Ordinal))
            return ShowRaceOdds(session, player, npc);
        if (command.Equals("ShowInfo", StringComparison.Ordinal))
            return ShowRaceInfo(session, player, npc);
        if (command.Equals("ShowTickets", StringComparison.Ordinal))
            return ShowRaceTickets(session, player, npc);
        if (command.StartsWith("ShowTicket", StringComparison.Ordinal))
            return ShowRaceTicket(session, player, npc, ParseTail(command, "ShowTicket"));
        if (command.StartsWith("CalculateWin", StringComparison.Ordinal))
            return CalculateRaceWin(session, player, npc, ParseTail(command, "CalculateWin"));
        if (command.Equals("ViewHistory", StringComparison.Ordinal))
            return ViewRaceHistory(session, player, npc);
        return "bad_request";
    }

    static int ParseTail(string command, string prefix) =>
        int.TryParse(command.AsSpan(prefix.Length), out int v) ? v : 0;

    string? BuyRaceTicket(ZoneSession session, ZoneEntity player, ZoneEntity npc, int val)
    {
        var board = MonsterRace;
        if (board.State != MonsterRaceState.AcceptingBets)
        {
            SelfLine(session, MonsterRaceProtocol.TicketsNoLongerAvailable,
                "Monster race tickets are no longer available.");
            return ShowRaceChat(session, player, npc, 0);
        }

        if (val == 0)
        {
            session.RaceTicketLane = 0;
            session.RaceTicketPriceIndex = 0;
        }

        if ((val == 10 && session.RaceTicketLane == 0) || (val == 20 && session.RaceTicketLane == 0 && session.RaceTicketPriceIndex == 0))
            val = 0;

        string html;
        if (val is >= 0 and <= 9)
        {
            html = RaceHtml(session, npc, 2);
            html = ReplaceRunners(html, board);
            html = html.Replace("No1", val == 0 ? "" : val.ToString(CultureInfo.InvariantCulture));
            if (val != 0)
                session.RaceTicketLane = val;
        }
        else if (val is >= 11 and <= 19)
        {
            if (session.RaceTicketLane == 0)
                return null;
            html = RaceHtml(session, npc, 3);
            html = html.Replace("0place", session.RaceTicketLane.ToString(CultureInfo.InvariantCulture));
            html = html.Replace("Mob1", RunnerName(board, session.RaceTicketLane - 1));
            int priceIndex = val - 10;
            html = html.Replace("0adena", val == 10 ? "" : MonsterRaceProtocol.TicketPrices[priceIndex - 1].ToString(CultureInfo.InvariantCulture));
            if (val != 10)
                session.RaceTicketPriceIndex = priceIndex;
        }
        else if (val == 20)
        {
            if (session.RaceTicketLane == 0 || session.RaceTicketPriceIndex == 0)
                return null;
            html = RaceHtml(session, npc, 4);
            int price = MonsterRaceProtocol.TicketPrices[session.RaceTicketPriceIndex - 1];
            html = html.Replace("0place", session.RaceTicketLane.ToString(CultureInfo.InvariantCulture));
            html = html.Replace("Mob1", RunnerName(board, session.RaceTicketLane - 1));
            html = html.Replace("0adena", price.ToString(CultureInfo.InvariantCulture));
            html = html.Replace("0tax", "0");
            html = html.Replace("0total", price.ToString(CultureInfo.InvariantCulture));
        }
        else if (val > 20)
        {
            if (session.RaceTicketLane == 0 || session.RaceTicketPriceIndex == 0)
                return null;
            int price = MonsterRaceProtocol.TicketPrices[session.RaceTicketPriceIndex - 1];
            string? err = RecordBagChange(
                player,
                LedgerTxKind.MonsterRaceBet,
                LedgerAccount.Of(SystemAccount.MonsterRace),
                () =>
                {
                    var inventory = player.Inventory!;
                    if (inventory.CountOf(MonsterRaceProtocol.AdenaItemId) < price)
                    {
                        SelfLine(session, LotteryProtocol.NotEnoughAdena, "You do not have enough adena.");
                        return MonsterRaceProtocol.Adena;
                    }

                    var changes = new List<InventoryUpdateEntryMsg>();
                    Take(inventory, MonsterRaceProtocol.AdenaItemId, price, changes);
                    var ticket = inventory.Add(
                        MonsterRaceProtocol.TicketItemId, 1, board.RaceNumber,
                        session.RaceTicketLane, price / 100);
                    changes.Add(InventoryMessages.ChangeRow(InventoryChange.Add, ticket));
                    FanInventory(session, Collapse(changes));
                    SaveInventory(session, inventory);
                    board.AddBet(session.RaceTicketLane, price);
                    SelfLine(session, MonsterRaceProtocol.AcquiredTicket,
                        $"Acquired {board.RaceNumber} Monster Race Ticket.",
                        board.RaceNumber.ToString(CultureInfo.InvariantCulture));
                    session.RaceTicketLane = 0;
                    session.RaceTicketPriceIndex = 0;
                    ZoneLog.Info($"[monster-race] ticket object={player.ObjectId} race={board.RaceNumber} lane={ticket.CustomType1} adena={price}");
                    return (string?)null;
                },
                _ => board.RaceNumber);
            return err ?? ShowRaceChat(session, player, npc, 0);
        }
        else
            return "bad_request";

        html = html.Replace("1race", board.RaceNumber.ToString(CultureInfo.InvariantCulture));
        SendHtml(session, player, npc, html);
        return null;
    }

    string? ShowRaceOdds(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var board = MonsterRace;
        if (board.State == MonsterRaceState.AcceptingBets)
        {
            SelfLine(session, MonsterRaceProtocol.PayoutNotWhileSelling,
                "Monster race payout information is not available while tickets are being sold.");
            return ShowRaceChat(session, player, npc, 0);
        }

        string html = RaceHtml(session, npc, 5);
        html = ReplaceRunners(html, board);
        var odds = board.Odds;
        for (int i = 0; i < 8; i++)
        {
            double odd = i < odds.Count ? odds[i] : 0;
            html = html.Replace("Odd" + (i + 1), odd > 0
                ? odd.ToString("0.0", CultureInfo.InvariantCulture)
                : "&$804;");
        }

        html = html.Replace("1race", board.RaceNumber.ToString(CultureInfo.InvariantCulture));
        SendHtml(session, player, npc, html);
        return null;
    }

    string? ShowRaceInfo(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        string html = ReplaceRunners(RaceHtml(session, npc, 6), MonsterRace);
        SendHtml(session, player, npc, html);
        return null;
    }

    string? ShowRaceTickets(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var board = MonsterRace;
        var sb = new StringBuilder();
        foreach (var ticket in player.Inventory!.Items.Where(i => i.ItemId == MonsterRaceProtocol.TicketItemId))
        {
            if (ticket.EnchantLevel == board.RaceNumber)
                continue;
            sb.Append("<tr><td><a action=\"bypass -h npc_%objectId%_ShowTicket ")
                .Append(ticket.ObjectId)
                .Append("\">").Append(ticket.EnchantLevel).Append(" Race Number</a></td><td align=right><font color=\"LEVEL\">")
                .Append(ticket.CustomType1).Append("</font> Number</td><td align=right><font color=\"LEVEL\">")
                .Append(ticket.CustomType2 * 100).Append("</font> Adena</td></tr>");
        }

        string html = RaceHtml(session, npc, 7).Replace("%tickets%", sb.ToString());
        SendHtml(session, player, npc, html);
        return null;
    }

    string? ShowRaceTicket(ZoneSession session, ZoneEntity player, ZoneEntity npc, int objectId)
    {
        if (objectId == 0)
            return ShowRaceChat(session, player, npc, 0);
        var ticket = player.Inventory!.Find(objectId);
        if (ticket == null || ticket.ItemId != MonsterRaceProtocol.TicketItemId)
            return ShowRaceChat(session, player, npc, 0);
        var info = MonsterRace.HistoryForRace(ticket.EnchantLevel);
        if (info == null)
            return ShowRaceChat(session, player, npc, 0);

        int lane = ticket.CustomType1;
        int bet = ticket.CustomType2 * 100;
        bool winner = lane == info.FirstLane + 1;
        string html = RaceHtml(session, npc, 8)
            .Replace("%raceId%", ticket.EnchantLevel.ToString(CultureInfo.InvariantCulture))
            .Replace("%lane%", lane.ToString(CultureInfo.InvariantCulture))
            .Replace("%bet%", bet.ToString(CultureInfo.InvariantCulture))
            .Replace("%firstLane%", (info.FirstLane + 1).ToString(CultureInfo.InvariantCulture))
            .Replace("%odd%", winner
                ? info.WinnerOdd.ToString("0.00", CultureInfo.InvariantCulture)
                : _monsterRaceSettings.LoserPayoutRate.ToString("0.00", CultureInfo.InvariantCulture))
            .Replace("%ticketObjectId%", objectId.ToString(CultureInfo.InvariantCulture));
        SendHtml(session, player, npc, html);
        return null;
    }

    string? CalculateRaceWin(ZoneSession session, ZoneEntity player, ZoneEntity npc, int objectId)
    {
        if (objectId == 0)
            return ShowRaceChat(session, player, npc, 0);
        var ticket = player.Inventory!.Find(objectId);
        if (ticket == null || ticket.ItemId != MonsterRaceProtocol.TicketItemId)
            return ShowRaceChat(session, player, npc, 0);
        var info = MonsterRace.HistoryForRace(ticket.EnchantLevel);
        if (info == null)
            return ShowRaceChat(session, player, npc, 0);

        int bet = ticket.CustomType2 * 100;
        bool winner = ticket.CustomType1 == info.FirstLane + 1;
        int payout = MonsterRaceLogic.PayoutAmount(bet, info.WinnerOdd, winner, _monsterRaceSettings.LoserPayoutRate);

        return RecordBagChange(
            player,
            LedgerTxKind.MonsterRacePayout,
            LedgerAccount.Of(SystemAccount.MonsterRace),
            () =>
            {
                var inventory = player.Inventory!;
                var changes = new List<InventoryUpdateEntryMsg>();
                int left = inventory.TakeOne(ticket);
                changes.Add(InventoryMessages.ChangeRow(left > 0 ? InventoryChange.Modify : InventoryChange.Remove, ticket));
                if (payout > 0)
                    GiveItem(session, inventory, MonsterRaceProtocol.AdenaItemId, payout);
                FanInventory(session, Collapse(changes));
                SaveInventory(session, inventory);
                ZoneLog.Info($"[monster-race] payout object={player.ObjectId} race={ticket.EnchantLevel} adena={payout}");
                return (string?)null;
            },
            _ => objectId) ?? ShowRaceChat(session, player, npc, 0);
    }

    string? ViewRaceHistory(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var history = MonsterRace.History;
        var sb = new StringBuilder();
        for (int i = history.Count - 1; i >= Math.Max(0, history.Count - 7); i--)
        {
            var info = history[i];
            sb.Append("<tr><td><font color=\"LEVEL\">").Append(info.RaceId)
                .Append("</font> th</td><td><font color=\"LEVEL\">").Append(info.FirstLane + 1)
                .Append("</font> Lane </td><td><font color=\"LEVEL\">").Append(info.SecondLane + 1)
                .Append("</font> Lane</td><td align=right><font color=00ffff>")
                .Append(info.WinnerOdd.ToString("0.00", CultureInfo.InvariantCulture))
                .Append("</font> Times</td></tr>");
        }

        string html = RaceHtml(session, npc, 9).Replace("%infos%", sb.ToString());
        SendHtml(session, player, npc, html);
        return null;
    }

    string RaceHtml(ZoneSession session, ZoneEntity npc, int page) =>
        _pages.ReadNpc(session.Language, npc.NpcType, npc.NpcId, page).Replace("%objectId%", npc.ObjectId.ToString(CultureInfo.InvariantCulture));

    string? ShowRaceChat(ZoneSession session, ZoneEntity player, ZoneEntity npc, int page)
    {
        SendHtml(session, player, npc, RaceHtml(session, npc, page));
        return null;
    }

    static string ReplaceRunners(string html, MonsterRaceBoard board)
    {
        for (int i = 0; i < 8; i++)
            html = html.Replace("Mob" + (i + 1), RunnerName(board, i));
        return html;
    }

    static string RunnerName(MonsterRaceBoard board, int index)
    {
        if (index < 0 || index >= board.Runners.Count)
            return "?";
        int npcId = board.Runners[index].NpcId;
        return NpcCatalog.LoadDefault().ById.TryGetValue(npcId, out var t) ? t.Name : npcId.ToString(CultureInfo.InvariantCulture);
    }
}
