using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Lottery;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Lucky lottery after Mobius CT_0 (<c>Loto</c> bypass, <c>LotteryManager</c>): pick five of twenty numbers, buy a
/// ticket for 2000 adena (the price joins the jackpot), the weekly draw pays by matched numbers and carries the rest
/// over, and a ticket of a finished lottery is turned in for its prize.
/// </summary>
public sealed class NetLotteryTests
{
    const int Ticket = LotteryProtocol.TicketItemId;
    // Wednesday 2027-01-13 12:00 UTC: three and a half days before the Saturday 19:00 draw.
    static readonly DateTimeOffset Start = new(2027, 1, 13, 12, 0, 0, TimeSpan.Zero);

    sealed class Seq(params int[] values) : Random
    {
        readonly Queue<int> _q = new(values);
        public override int Next(int maxValue) => _q.Count > 0 ? _q.Dequeue() : 0;
    }

    sealed class Rig
    {
        public required WorldBench Bench { get; init; }
        public required MutableClock Clock { get; init; }
        public required LotteryBoard Board { get; init; }
        public void At(DateTimeOffset when) { Clock.Now = when; Bench.World.TickLotteryForTests(); }
    }

    static Rig Open(ILotteryStore? store = null, LotterySettings? settings = null)
    {
        var clock = new MutableClock { Now = Start };
        var bench = new WorldBench(clock: () => clock.Now);
        var board = new LotteryBoard(Start.ToUnixTimeMilliseconds(), settings, store ?? new MemoryLotteryStore());
        bench.World.UseLottery(board);
        return new Rig { Bench = bench, Clock = clock, Board = board };
    }

    static Task<BenchPlayer> EnterAsync(Rig rig, int adena = 10_000, string name = "Gambler") =>
        rig.Bench.EnterAsync(name.ToLowerInvariant(), name, [new StoredItem(57, adena, -1)]);

    static string LastHtml(WorldBench bench) =>
        Assert.IsType<NpcHtmlMsg>(bench.Rec.Of("FanoutNpcHtml").Last()[1]).Html;

    static List<int> Lines(WorldBench bench, int objectId) =>
        bench.Rec.Of("FanoutSystemMessage")
            .Where(a => ((IReadOnlyList<int>)a[0]!).Contains(objectId))
            .Select(a => ((SystemMessageMsg)a[1]!).Id).ToList();

    static void Pick(Rig rig, BenchPlayer p, params int[] numbers)
    {
        foreach (int n in numbers)
            Assert.Null(rig.Bench.World.LotoForTest(p.Session, n));
    }

    static void Buy(Rig rig, BenchPlayer p, params int[] numbers)
    {
        Assert.Null(rig.Bench.World.LotoForTest(p.Session, 0));
        Pick(rig, p, numbers);
        Assert.Null(rig.Bench.World.LotoForTest(p.Session, LotteryProtocol.PageConfirm));
    }

    static DateTimeOffset Draw => new(2027, 1, 16, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Draw_times_fall_on_saturday_evening_and_sales_close_ten_minutes_before()
    {
        var board = new LotteryBoard(Start.ToUnixTimeMilliseconds());
        Assert.Equal(Draw.ToUnixTimeMilliseconds(), board.EndUnixMs);
        Assert.True(board.Selling(Draw.AddMinutes(-11).ToUnixTimeMilliseconds()));
        Assert.False(board.Selling(Draw.AddMinutes(-10).ToUnixTimeMilliseconds()));
        Assert.Equal(Draw.AddDays(7).ToUnixTimeMilliseconds(), board.NextDraw(Draw.ToUnixTimeMilliseconds()));
        Assert.Equal(50_000, board.Prize);
        Assert.Null(LotteryBoard.MaskOf([1, 2, 3, 4]));
        Assert.Null(LotteryBoard.MaskOf([1, 2, 3, 4, 4]));
        Assert.Null(LotteryBoard.MaskOf([1, 2, 3, 4, 21]));
        Assert.Equal([2, 5, 7, 11, 20], LotteryBoard.NumbersOf(LotteryBoard.MaskOf([20, 2, 11, 5, 7])!.Value));
    }

    [Fact]
    public async Task Picking_numbers_marks_the_buttons_and_a_sixth_pick_is_ignored()
    {
        var rig = Open();
        var p = await EnterAsync(rig);
        Assert.Null(rig.Bench.World.LotoForTest(p.Session, 0));
        Assert.Contains("Loto 21", LastHtml(rig.Bench));

        Pick(rig, p, 3);
        string html = LastHtml(rig.Bench);
        Assert.Contains("fore=\"L2UI.lottoNum03a_check\" back=\"L2UI.lottoNum03\"", html);
        Assert.Contains("fore=\"L2UI.lottoNum04\" back=\"L2UI.lottoNum04a_check\"", html);
        Assert.DoesNotContain("Your lucky numbers", html);

        Pick(rig, p, 3); // toggled off again
        Assert.Contains("fore=\"L2UI.lottoNum03\" back=\"L2UI.lottoNum03a_check\"", LastHtml(rig.Bench));

        Pick(rig, p, 1, 2, 3, 4, 5, 6);
        Assert.Equal([1, 2, 3, 4, 5], p.Session.LotoPicks);
        html = LastHtml(rig.Bench);
        Assert.Contains("Your lucky numbers have been selected above.", html);
        Assert.Contains("fore=\"L2UI.lottoNum06\" back=\"L2UI.lottoNum06a_check\"", html);
    }

    [Fact]
    public async Task Buying_takes_the_price_feeds_the_jackpot_and_gives_a_numbered_ticket()
    {
        var rig = Open();
        var p = await EnterAsync(rig);
        Buy(rig, p, 1, 2, 3, 4, 5);
        Assert.Equal(8_000, p.Inventory.CountOf(57));
        var item = Assert.Single(p.Inventory.Items, i => i.ItemId == Ticket);
        var ticket = rig.Board.Ticket(item.EnchantLevel);
        Assert.NotNull(ticket);
        Assert.Equal(1, ticket!.Number);
        Assert.Equal([1, 2, 3, 4, 5], LotteryBoard.NumbersOf(ticket.Mask));
        Assert.Equal(52_000, rig.Board.Prize);
        Assert.Contains(LotteryProtocol.Earned, Lines(rig.Bench, p.ObjectId));
        Assert.Contains("Thank you for playing the lottery.", LastHtml(rig.Bench)); // -6: bought page

        Assert.Null(rig.Bench.World.LotoForTest(p.Session, LotteryProtocol.PageJackpot));
        Assert.Contains("52000", LastHtml(rig.Bench));
        Assert.Null(rig.Bench.World.LotoForTest(p.Session, LotteryProtocol.PageRules));
        Assert.Contains("60", LastHtml(rig.Bench));
    }

    [Fact]
    public async Task Buying_needs_five_numbers_and_the_adena()
    {
        var rig = Open();
        var p = await EnterAsync(rig, adena: 1_999);
        Assert.Null(rig.Bench.World.LotoForTest(p.Session, 0));
        Pick(rig, p, 1, 2, 3, 4);
        Assert.Equal("bad_request", rig.Bench.World.LotoForTest(p.Session, LotteryProtocol.PageConfirm));
        Pick(rig, p, 5);
        Assert.Equal(LotteryProtocol.Adena, rig.Bench.World.LotoForTest(p.Session, LotteryProtocol.PageConfirm));
        Assert.Contains(LotteryProtocol.NotEnoughAdena, Lines(rig.Bench, p.ObjectId));
        Assert.Equal(0, p.Inventory.CountOf(Ticket));
        Assert.Equal(50_000, rig.Board.Prize);
    }

    [Fact]
    public async Task Sales_stop_ten_minutes_before_the_draw_and_when_disabled()
    {
        var rig = Open();
        var p = await EnterAsync(rig);
        rig.At(Draw.AddMinutes(-5));
        Assert.Equal(LotteryProtocol.Closed, rig.Bench.World.LotoForTest(p.Session, 21));
        Assert.Contains(LotteryProtocol.NoLongerAvailable, Lines(rig.Bench, p.ObjectId));
        Assert.Contains(LotteryProtocol.SalesSuspended, Lines(rig.Bench, p.ObjectId));

        var off = Open(settings: new LotterySettings { Enabled = false });
        var q = await EnterAsync(off, name: "Other");
        Assert.Equal(LotteryProtocol.Closed, off.Bench.World.LotoForTest(q.Session, 21));
        Assert.Contains(LotteryProtocol.NotSelling, Lines(off.Bench, q.ObjectId));
    }

    [Fact]
    public async Task The_draw_pays_by_matches_carries_the_rest_and_claimed_tickets_vanish()
    {
        var rig = Open();
        var a = await EnterAsync(rig, name: "Alpha");
        var b = await EnterAsync(rig, name: "Bravo");
        var c = await EnterAsync(rig, name: "Charlie");
        Buy(rig, a, 1, 2, 3, 4, 5);       // all five
        Buy(rig, b, 1, 2, 3, 10, 11);     // three
        Buy(rig, c, 16, 17, 18, 19, 20);  // none
        Assert.Equal(56_000, rig.Board.Prize);

        // Draw 1..5: Next(20) + 1.
        rig.Bench.World.LotteryRandom = new Seq(0, 1, 2, 3, 4);
        rig.At(Draw);
        Assert.Equal(2, rig.Board.Number);
        var draw = rig.Board.Draw(1)!;
        Assert.Equal([1, 2, 3, 4, 5], LotteryBoard.NumbersOf(draw.Mask));
        Assert.Equal(33_600, draw.Prize1);
        Assert.Equal(11_200, draw.Prize3);
        Assert.Equal(56_000 - 33_600 - 11_200, rig.Board.Prize);
        Assert.Equal(Draw.AddDays(7).ToUnixTimeMilliseconds(), rig.Board.EndUnixMs);
        Assert.Contains(LotteryProtocol.FirstPrizeWinners, Lines(rig.Bench, a.ObjectId));
        Assert.True(rig.Board.Selling(Draw.ToUnixTimeMilliseconds()));

        Assert.Null(rig.Bench.World.LotoForTest(a.Session, LotteryProtocol.PageResults));
        Assert.Contains("1st Prize 33600a.", LastHtml(rig.Bench));
        Assert.Null(rig.Bench.World.LotoForTest(c.Session, LotteryProtocol.PageResults));
        Assert.Contains("Event Number 16 17 18 19 20 </a>", LastHtml(rig.Bench));

        long before = a.Inventory.CountOf(57);
        int objectId = a.Inventory.Items.Single(i => i.ItemId == Ticket).ObjectId;
        Assert.Null(rig.Bench.World.LotoForTest(a.Session, objectId));
        Assert.Equal(before + 33_600, a.Inventory.CountOf(57));
        Assert.Equal(0, a.Inventory.CountOf(Ticket));
        Assert.Contains(LotteryProtocol.Disappeared, Lines(rig.Bench, a.ObjectId));
        Assert.Equal(LotteryProtocol.NotATicket, rig.Bench.World.LotoForTest(a.Session, objectId));

        // A losing ticket disappears for nothing.
        long cBefore = c.Inventory.CountOf(57);
        Assert.Null(rig.Bench.World.LotoForTest(c.Session, c.Inventory.Items.Single(i => i.ItemId == Ticket).ObjectId));
        Assert.Equal(cBefore, c.Inventory.CountOf(57));
        Assert.Equal(0, c.Inventory.CountOf(Ticket));
    }

    [Fact]
    public async Task A_ticket_of_the_running_lottery_cannot_be_claimed_and_no_winner_carries_everything()
    {
        var rig = Open();
        var p = await EnterAsync(rig);
        Buy(rig, p, 16, 17, 18, 19, 20);
        int objectId = p.Inventory.Items.Single(i => i.ItemId == Ticket).ObjectId;
        Assert.Equal(LotteryProtocol.NotATicket, rig.Bench.World.LotoForTest(p.Session, objectId));
        Assert.Null(rig.Bench.World.LotoForTest(p.Session, LotteryProtocol.PageResults));
        Assert.Contains("There has been no winning lottery ticket.", LastHtml(rig.Bench));

        rig.Bench.World.LotteryRandom = new Seq(0, 1, 2, 3, 4);
        rig.At(Draw);
        Assert.Equal(52_000, rig.Board.Prize);
        Assert.Contains(LotteryProtocol.NoFirstPrize, Lines(rig.Bench, p.ObjectId));
    }

    [Fact]
    public void The_board_survives_a_restart_through_the_json_store()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fineage-lottery-" + Guid.NewGuid().ToString("N"));
        try
        {
            long now = Start.ToUnixTimeMilliseconds();
            var board = LotteryBoard.OpenInStateDirectory(dir, now);
            int end = 0;
            var t = board.Sell(LotteryBoard.MaskOf([1, 2, 3, 4, 5])!.Value, now);
            Assert.NotNull(t);
            board.Tick(board.EndUnixMs, new Seq(0, 1, 2, 3, 4), []);
            end++;

            var again = LotteryBoard.OpenInStateDirectory(dir, now);
            Assert.Equal(2, again.Number);
            Assert.Equal(board.Prize, again.Prize);
            Assert.Equal(board.EndUnixMs, again.EndUnixMs);
            Assert.Equal((1, board.Draw(1)!.Prize1), again.Check(t!.Serial));
            again.Forget(t.Serial);
            Assert.Null(LotteryBoard.OpenInStateDirectory(dir, now).Ticket(t.Serial));
            Assert.Equal(1, end);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
