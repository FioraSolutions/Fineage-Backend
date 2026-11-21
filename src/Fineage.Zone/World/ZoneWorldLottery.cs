using System.Globalization;
using System.Text;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Fineage.Zone.Lottery;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

/// <summary>
/// Lottery ticket sellers after Mobius <c>Loto</c> (<c>showLotoWindow</c>): pick five numbers on the button page,
/// buy a ticket for the running lottery, read the jackpot, and claim tickets of finished draws. The weekly draw runs
/// on the simulation tick and is announced to everyone online.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius ships the lottery pages for 30991 only; every seller uses them.</summary>
    const int LotteryPageNpc = 30991;

    readonly LotteryActor _lottery;

    internal LotteryBoard Lottery => _lottery.Board;

    internal Random LotteryRandom
    {
        get => _lottery.Random;
        set => _lottery.Random = value;
    }

    /// <summary>Swap in the persisted board (host: <c>lottery.json</c> in the state directory).</summary>
    public void UseLottery(LotteryBoard board)
    {
        using (_affinity.Step())
            _lottery.Use(board);
    }

    int AnnounceLottery(IReadOnlyList<LotteryNotice> notices)
    {
        if (notices.Count == 0)
            return 0;
        var live = Players().Where(p => p.Session != null).Select(p => p.ObjectId).ToArray();
        if (live.Length == 0)
            return notices.Count;
        foreach (var n in notices)
            NetBridge?.FanoutSystemMessage(live, new SystemMessageMsg { Id = n.MessageId, Params = n.Params, Text = n.Text });
        return notices.Count;
    }

    internal void TickLotteryForTests()
    {
        using (_affinity.Step())
            AtHookTick(tick => AnnounceLottery(_lottery.Step(tick)));
    }

    /// <summary>Test: one <c>Loto N</c> bypass against a lottery seller (spawned or a stand-in).</summary>
    internal string? LotoForTest(ZoneSession session, int value, int npcId = LotteryPageNpc)
    {
        using (_affinity.Step())
        {
            if (!_byId.TryGetValue(session.ObjectId, out var player))
                return "world";
            var npc = _byId.Values.FirstOrDefault(e => e.Kind == ZoneEntityKind.Npc && e.NpcId == npcId)
                ?? new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = npcId, ObjectId = 0 };
            return ApplyNpcService(session, player, npc, BypassReply.ServiceCall(NpcServiceIntent.Loto, "", value));
        }
    }

    string LotteryPage(ZoneEntity npc, int page)
    {
        string own = page == 0 ? $"default/{npc.NpcId}.htm" : $"default/{npc.NpcId}-{page}.htm";
        if (_html.TryGet(own, out var html) || _html.TryGet($"default/{LotteryPageNpc}-{page}.htm", out html))
            return html;
        return "<html><body>I have nothing to say to you.</body></html>";
    }

    string? ShowLoto(ZoneSession session, ZoneEntity player, ZoneEntity npc, int value)
    {
        var board = Lottery;
        long now = NowUnixMs();
        var picks = session.LotoPicks;
        string html;
        switch (value)
        {
            case LotteryProtocol.PageBuy:
                Array.Clear(picks);
                html = LotteryPage(npc, 1);
                break;
            case >= 1 and <= LotteryProtocol.PageContinue:
                if (SalesClosed(session, board, now))
                    return LotteryProtocol.Closed;
                Toggle(picks, value);
                html = LotteryPage(npc, 5);
                foreach (int pick in picks.Where(p => p > 0))
                {
                    string b = pick.ToString("00", CultureInfo.InvariantCulture);
                    html = html.Replace($"fore=\"L2UI.lottoNum{b}\" back=\"L2UI.lottoNum{b}a_check\"",
                        $"fore=\"L2UI.lottoNum{b}a_check\" back=\"L2UI.lottoNum{b}\"");
                }
                if (picks.All(p => p > 0))
                    html = html.Replace("0\">Return", "22\">Your lucky numbers have been selected above.");
                break;
            case LotteryProtocol.PageConfirm:
                if (SalesClosed(session, board, now))
                    return LotteryProtocol.Closed;
                if (BuyTicket(session, player, picks, now) is { } error)
                    return error;
                html = LotteryPage(npc, 6);
                break;
            case LotteryProtocol.PageJackpot:
                html = LotteryPage(npc, 3);
                break;
            case LotteryProtocol.PageResults:
                html = LotteryPage(npc, 4).Replace("%result%", LotteryResults(player));
                break;
            case LotteryProtocol.PageRules:
                html = LotteryPage(npc, 2);
                break;
            case > LotteryProtocol.PageRules:
                return ClaimTicket(session, player, value);
            default:
                return "bad_request";
        }

        var s = board.Settings;
        html = html
            .Replace("%race%", board.Number.ToString(CultureInfo.InvariantCulture))
            .Replace("%adena%", board.Prize.ToString(CultureInfo.InvariantCulture))
            .Replace("%ticket_price%", s.TicketPrice.ToString(CultureInfo.InvariantCulture))
            .Replace("%prize5%", (s.Rate5 * 100).ToString("0.##", CultureInfo.InvariantCulture))
            .Replace("%prize4%", (s.Rate4 * 100).ToString("0.##", CultureInfo.InvariantCulture))
            .Replace("%prize3%", (s.Rate3 * 100).ToString("0.##", CultureInfo.InvariantCulture))
            .Replace("%prize2%", s.SmallPrize.ToString(CultureInfo.InvariantCulture))
            .Replace("%enddate%", DateTimeOffset.FromUnixTimeMilliseconds(board.EndUnixMs)
                .ToOffset(TimeSpan.FromMinutes(s.UtcOffsetMinutes)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        SendHtml(session, player, npc, html);
        return null;
    }

    bool SalesClosed(ZoneSession session, LotteryBoard board, long now)
    {
        if (!board.Settings.Enabled)
        {
            SelfLine(session, LotteryProtocol.NotSelling, "Lottery tickets are not currently being sold.");
            return true;
        }
        if (!board.Selling(now))
        {
            SelfLine(session, LotteryProtocol.NoLongerAvailable, "Tickets for the current lottery are no longer available.");
            return true;
        }
        return false;
    }

    /// <summary>Mobius: a picked number is cleared again, a new one fills the first free slot (five at most).</summary>
    static void Toggle(int[] picks, int value)
    {
        int i = Array.IndexOf(picks, value);
        if (i >= 0)
        {
            picks[i] = 0;
            return;
        }
        if (value > LotteryProtocol.MaxNumber)
            return;
        int free = Array.IndexOf(picks, 0);
        if (free >= 0)
            picks[free] = value;
    }

    string? BuyTicket(ZoneSession session, ZoneEntity player, int[] picks, long now)
    {
        if (LotteryBoard.MaskOf(picks) is not { } mask)
            return "bad_request";
        var inventory = player.Inventory!;
        long price = Lottery.Settings.TicketPrice;
        if (inventory.CountOf(LotteryProtocol.AdenaItemId) < price)
        {
            SelfLine(session, LotteryProtocol.NotEnoughAdena, "You do not have enough adena.");
            return LotteryProtocol.Adena;
        }

        LotteryTicket? sold = null;
        string? error = RecordBagChange(
            player,
            LedgerTxKind.LotteryBuy,
            LedgerAccount.Of(SystemAccount.Lottery),
            () =>
            {
                var ticket = Lottery.Sell(mask, now);
                if (ticket == null)
                {
                    SelfLine(session, LotteryProtocol.NoLongerAvailable, "Tickets for the current lottery are no longer available.");
                    return LotteryProtocol.Closed;
                }

                sold = ticket;
                var changes = new List<InventoryUpdateEntryMsg>();
                Take(inventory, LotteryProtocol.AdenaItemId, (int)price, changes);
                var item = inventory.Add(LotteryProtocol.TicketItemId, 1, ticket.Serial);
                changes.Add(InventoryMessages.ChangeRow(InventoryChange.Add, item));
                FanInventory(session, Collapse(changes));
                SaveInventory(session, inventory);
                string name = item.Template.Name;
                SelfLine(session, LotteryProtocol.Earned, $"You have earned {name}.", name);
                ZoneLog.Info($"[lottery] ticket object={player.ObjectId} lottery={ticket.Number} serial={ticket.Serial} numbers={string.Join(',', LotteryBoard.NumbersOf(mask))}");
                return (string?)null;
            },
            _ => sold?.Number ?? Lottery.Number);
        return error;
    }

    string LotteryResults(ZoneEntity player)
    {
        var sb = new StringBuilder();
        foreach (var item in player.Inventory!.Items.Where(i => i.ItemId == LotteryProtocol.TicketItemId))
        {
            var ticket = Lottery.Ticket(item.EnchantLevel);
            if (ticket == null || ticket.Number >= Lottery.Number)
                continue;
            sb.Append("<a action=\"bypass -h npc_%objectId%_Loto ").Append(item.ObjectId).Append("\">")
                .Append(ticket.Number).Append(" Event Number ");
            foreach (int n in LotteryBoard.NumbersOf(ticket.Mask))
                sb.Append(n).Append(' ');
            if (Lottery.Check(ticket.Serial) is { Tier: > 0 } win)
            {
                string place = win.Tier switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => "4th" };
                sb.Append("- ").Append(place).Append(" Prize ").Append(win.Adena).Append("a.");
            }
            sb.Append("</a><br>");
        }
        return sb.Length == 0 ? "There has been no winning lottery ticket.<br>" : sb.ToString();
    }

    /// <summary>Mobius: a ticket of a finished lottery disappears and pays what it won (possibly nothing).</summary>
    string? ClaimTicket(ZoneSession session, ZoneEntity player, int objectId)
    {
        var inventory = player.Inventory!;
        var item = inventory.Find(objectId);
        if (item == null || item.ItemId != LotteryProtocol.TicketItemId)
            return LotteryProtocol.NotATicket;
        var ticket = Lottery.Ticket(item.EnchantLevel);
        if (ticket != null && ticket.Number >= Lottery.Number)
            return LotteryProtocol.NotATicket;

        int serial = ticket?.Serial ?? item.EnchantLevel;
        return RecordBagChange(
            player,
            LedgerTxKind.LotteryPrize,
            LedgerAccount.Of(SystemAccount.Lottery),
            () =>
            {
                long adena = ticket != null && Lottery.Check(ticket.Serial) is { } win ? win.Adena : 0;
                SelfLine(session, LotteryProtocol.Disappeared, $"{item.Template.Name} has disappeared.", item.Template.Name);
                int left = inventory.TakeOne(item);
                NetBridge?.FanoutInventoryUpdate([player.ObjectId],
                    InventoryMessages.Update(left > 0 ? InventoryChange.Modify : InventoryChange.Remove, item));
                if (adena > 0)
                {
                    GiveItem(session, inventory, LotteryProtocol.AdenaItemId, (int)Math.Min(int.MaxValue, adena));
                    SelfLine(session, 52, $"You have earned {adena} adena.", adena.ToString(CultureInfo.InvariantCulture));
                }
                SaveInventory(session, inventory);
                if (ticket != null)
                    Lottery.Forget(ticket.Serial);
                ZoneLog.Info($"[lottery] claim object={player.ObjectId} serial={ticket?.Serial} adena={adena}");
                return (string?)null;
            },
            _ => serial);
    }
}
