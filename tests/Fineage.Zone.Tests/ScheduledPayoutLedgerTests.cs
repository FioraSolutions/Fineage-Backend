using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Lottery;
using Fineage.Zone.SevenSigns;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-5b: lottery, Seven Signs and related scheduled payouts on the auditable economy ledger.</summary>
public sealed class ScheduledPayoutLedgerTests
{
    const int Adena = LotteryProtocol.AdenaItemId;
    const int Ticket = LotteryProtocol.TicketItemId;
    static readonly DateTimeOffset LotteryStart = new(2027, 1, 13, 12, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Recruiting = new(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);

    static readonly LedgerAccount LotteryPool = LedgerAccount.Of(SystemAccount.Lottery);
    static readonly LedgerAccount SevenSignsPool = LedgerAccount.Of(SystemAccount.SevenSigns);

    static LedgerTransaction Only(WorldBench bench, LedgerTxKind kind) =>
        Assert.Single(bench.Ledger.Entries, e => e.Transaction.Kind == kind).Transaction;

    static string Line(LedgerAccount account, int itemId, long amount, params int[] pieces) =>
        $"{account} {itemId} {amount} [{string.Join(",", pieces)}]";

    [Fact]
    public async Task Lottery_ticket_purchase_is_journaled_against_the_lottery_pool()
    {
        var clock = new MutableClock { Now = LotteryStart };
        var bench = new WorldBench(clock: () => clock.Now);
        var board = new LotteryBoard(LotteryStart.ToUnixTimeMilliseconds(), store: new MemoryLotteryStore());
        bench.World.UseLottery(board);
        var player = await bench.EnterAsync("lotto", "Gambler", [new StoredItem(Adena, 10_000, -1)]);

        Assert.Null(bench.World.LotoForTest(player.Session, 0));
        foreach (int n in new[] { 1, 2, 3, 4, 5 })
            Assert.Null(bench.World.LotoForTest(player.Session, n));
        Assert.Null(bench.World.LotoForTest(player.Session, LotteryProtocol.PageConfirm));

        var buy = Only(bench, LedgerTxKind.LotteryBuy);
        Assert.Equal(1, buy.Reference);
        var bag = LedgerAccount.Inventory(player.ObjectId);
        Assert.Contains(buy.Postings, p => p.Account == bag && p.ItemId == Adena && p.Amount < 0);
        Assert.Contains(buy.Postings, p => p.Account == LotteryPool && p.ItemId == Adena && p.Amount > 0);
        Assert.Contains(buy.Postings, p => p.Account == bag && p.ItemId == Ticket && p.Amount > 0);
        Assert.Contains(buy.Postings, p => p.Account == LotteryPool && p.ItemId == Ticket && p.Amount < 0);
    }

    [Fact]
    public async Task Seven_signs_contribution_destroys_stones_on_the_ledger()
    {
        var clock = new MutableGameClock(Recruiting);
        var engine = new SevenSignsEngine(clock.UtcNow);
        var bench = new WorldBench(clock: () => clock.UtcNow, sevenSigns: engine);
        var player = await bench.EnterAsync("ssq", "Priest", classId: 1);
        clock.UtcNow = engine.NextChange;
        Assert.True(await bench.World.AdvanceSevenSignsAsync(CancellationToken.None));

        Assert.Null(await bench.World.TryGrantItemAsync(player.Session, SevenSignsProtocol.RedSealStoneId, 2, CancellationToken.None));
        Assert.Null(await bench.World.TrySevenSignsActionAsync(player.Session, new SevenSignsActionRequestMsg
        {
            Action = SevenSignsProtocol.ActionJoin,
            Cabal = SevenSignsProtocol.CabalDusk,
            Seal = SevenSignsProtocol.SealStrife
        }, CancellationToken.None));
        Assert.Null(await bench.World.TrySevenSignsActionAsync(player.Session, new SevenSignsActionRequestMsg
        {
            Action = SevenSignsProtocol.ActionContribute,
            RedStones = 2
        }, CancellationToken.None));

        var contribute = Only(bench, LedgerTxKind.SevenSignsContribute);
        var bag = LedgerAccount.Inventory(player.ObjectId);
        Assert.Contains(contribute.Postings, p => p.Account == bag && p.ItemId == SevenSignsProtocol.RedSealStoneId && p.Amount == -2);
        Assert.Contains(contribute.Postings, p => p.Account == SevenSignsPool && p.ItemId == SevenSignsProtocol.RedSealStoneId && p.Amount == 2);
    }

    [Fact]
    public async Task Stone_exchange_for_ancient_adena_is_journaled()
    {
        var clock = new MutableGameClock(Recruiting);
        var engine = new SevenSignsEngine(clock.UtcNow);
        var bench = new WorldBench(clock: () => clock.UtcNow, sevenSigns: engine);
        var player = await bench.EnterAsync("ssqex", "Trader", classId: 1);
        Assert.Null(await bench.World.TryGrantItemAsync(player.Session, SevenSignsProtocol.BlueSealStoneId, 3, CancellationToken.None));
        int reward = SevenSignsEngine.StoneAncientAdena(3, 0, 0);
        Assert.Null(await bench.World.TrySevenSignsActionAsync(player.Session, new SevenSignsActionRequestMsg
        {
            Action = SevenSignsProtocol.ActionExchangeStones,
            BlueStones = 3
        }, CancellationToken.None));

        var exchange = Only(bench, LedgerTxKind.SevenSignsExchange);
        Assert.Equal(reward, exchange.Reference);
        var bag = LedgerAccount.Inventory(player.ObjectId);
        Assert.Contains(exchange.Postings, p => p.Account == bag && p.ItemId == SevenSignsProtocol.BlueSealStoneId && p.Amount == -3);
        Assert.Contains(exchange.Postings, p => p.Account == SevenSignsPool && p.ItemId == SevenSignsProtocol.BlueSealStoneId && p.Amount == 3);
        Assert.Contains(exchange.Postings, p => p.Account == bag && p.ItemId == SevenSignsProtocol.AncientAdenaItemId && p.Amount == reward);
        Assert.Contains(exchange.Postings, p => p.Account == SevenSignsPool && p.ItemId == SevenSignsProtocol.AncientAdenaItemId && p.Amount == -reward);
    }
}