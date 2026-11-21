using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Fishing;
using Fineage.Zone.NpcHtml;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class FishingChampionshipTests
{
    sealed class SeqRandom(params int[] values) : Random
    {
        readonly Queue<int> _ints = new(values);
        public override int Next(int minValue, int maxValue) =>
            _ints.Count > 0 ? Math.Clamp(_ints.Dequeue(), minValue, maxValue - 1) : minValue;
        public override int Next(int maxValue) => Next(0, maxValue);
    }

    static FishingChampionshipBoard Board(long now, IFishingChampionshipStore? store = null) =>
        new(now, new FishingChampionshipSettings
        {
            Enabled = true,
            PeriodMs = 86_400_000,
            Rewards = FishingChampionshipSettings.MobiusRewards
        }, store ?? new MemoryFishingChampionshipStore());

    [Fact]
    public void Board_keeps_top_five_by_length()
    {
        var board = Board(1_000);
        var rng = new SeqRandom(60, 500, 70, 400, 80, 300, 90, 200, 65, 100, 50, 0);
        board.RecordCatch("Alice", 0, rng);
        board.RecordCatch("Bob", 0, rng);
        board.RecordCatch("Carol", 0, rng);
        board.RecordCatch("Dave", 0, rng);
        board.RecordCatch("Eve", 0, rng);
        board.RecordCatch("Frank", 0, rng);
        Assert.Equal(5, board.CurrentRanking.Count);
        Assert.Equal("Dave", board.CurrentRanking[0].Name);
        Assert.DoesNotContain(board.CurrentRanking, e => e.Name == "Frank");
    }

    [Fact]
    public void Weekly_tick_promotes_current_leaders()
    {
        var store = new MemoryFishingChampionshipStore();
        var board = Board(0, store);
        board.RecordCatch("Winner", 0, new SeqRandom(80, 0));
        Assert.True(board.Tick(board.PeriodEndUnixMs));
        Assert.Empty(board.CurrentRanking);
        Assert.Equal("Winner", board.LastWinners[0].Name);
    }

    [Fact]
    public async Task Claim_posts_ledger_prize()
    {
        var board = Board(0, new MemoryFishingChampionshipStore());
        board.RecordCatch("Champ", 0, new SeqRandom(88, 0));
        board.Tick(board.PeriodEndUnixMs);
        var bench = new WorldBench();
        bench.World.UseFishingChampionship(board);
        var p = await bench.EnterAsync("fish1", "Champ");
        Assert.Null(bench.World.FishingChampionshipForTest(p.Session, claim: true));
        Assert.Equal(800_000, p.Inventory.CountOf(57));
        var tx = Assert.Single(bench.Ledger.Entries, e => e.Transaction.Kind == LedgerTxKind.FishingChampionshipPrize).Transaction;
        Assert.Equal(800_000, tx.Postings.Single(p => p.ItemId == 57 && p.Amount > 0).Amount);
    }

    [Fact]
    public async Task Championship_bypass_renders_winner_rows()
    {
        var board = Board(0, new MemoryFishingChampionshipStore());
        board.RecordCatch("Angler", 0, new SeqRandom(77, 0));
        board.Tick(board.PeriodEndUnixMs);
        var bench = new WorldBench();
        bench.World.UseFishingChampionship(board);
        Assert.Equal(NpcServiceIntent.FishingChampionship,
            bench.World.Bypasses.Find("FishingChampionship")!.Handle(new BypassCall
            {
                NpcObjectId = 1,
                NpcId = 31562,
                NpcName = "Fisher",
                NpcType = "Fisherman",
                PlayerName = "Visitor",
                Command = "FishingChampionship"
            }).Service);
        var p = await bench.EnterAsync("fish2", "Visitor");
        Assert.Null(bench.World.FishingChampionshipForTest(p.Session));
        string html = Assert.IsType<NpcHtmlMsg>(bench.Rec.Of("FanoutNpcHtml").Last()[1]).Html;
        Assert.Contains("Angler", html);
        Assert.Contains("77.00", html);
    }
}