using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-5d: fee sinks post to the Fees system account.</summary>
public sealed class FeeSinkLedgerTests
{
    const int Adena = 57;
    const int Dye1Item = 4445;
    static readonly LedgerAccount Fees = LedgerAccount.Of(SystemAccount.Fees);

    static LedgerTransaction Only(WorldBench bench, LedgerTxKind kind) =>
        Assert.Single(bench.Ledger.Entries, e => e.Transaction.Kind == kind).Transaction;

    [Fact]
    public async Task Henna_draw_charges_adena_into_fees()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(
            "henna",
            "Artist",
            [new StoredItem(Adena, 100_000, -1), new StoredItem(Dye1Item, 20, -1)],
            classId: 1);
        const int dyeId = 1;
        var catalog = HennaCatalog.LoadDefault();
        Assert.True(catalog.TryGet(dyeId, out var dye));
        int before = bench.Ledger.Entries.Count;
        Assert.Equal(ZoneWorld.HennaWearResult.Ok,
            bench.World.TryWearHennaPaying(player.Player, dyeId, out int slot, out _, out _));
        Assert.InRange(slot, 0, 2);
        var draw = Assert.Single(bench.Ledger.Entries.Skip(before), e => e.Transaction.Kind == LedgerTxKind.HennaDraw).Transaction;
        Assert.Equal(dyeId, draw.Reference);
        var bag = LedgerAccount.Inventory(player.ObjectId);
        Assert.Contains(draw.Postings, p => p.Account == bag && p.ItemId == Adena && p.Amount == -dye.WearFee);
        Assert.Contains(draw.Postings, p => p.Account == Fees && p.ItemId == Adena && p.Amount == dye.WearFee);
    }

    [Fact]
    public async Task Henna_remove_charges_cancel_fee_and_returns_dye()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(
            "henna2",
            "Artist2",
            [new StoredItem(Adena, 100_000, -1), new StoredItem(Dye1Item, 20, -1)],
            classId: 1);
        Assert.Equal(ZoneWorld.HennaWearResult.Ok,
            bench.World.TryWearHennaPaying(player.Player, 1, out int slot, out _, out _));
        int before = bench.Ledger.Entries.Count;
        Assert.Equal(ZoneWorld.HennaCancelResult.Ok, bench.World.TryCancelHennaPaying(player.Player, slot));
        var remove = Assert.Single(bench.Ledger.Entries.Skip(before), e => e.Transaction.Kind == LedgerTxKind.HennaRemove).Transaction;
        var catalog = HennaCatalog.LoadDefault();
        Assert.True(catalog.TryGet(1, out var dye));
        var bag = LedgerAccount.Inventory(player.ObjectId);
        Assert.Contains(remove.Postings, p => p.Account == bag && p.ItemId == Adena && p.Amount == -dye.CancelFee);
        Assert.Contains(remove.Postings, p => p.Account == Fees && p.ItemId == Adena && p.Amount == dye.CancelFee);
        Assert.Contains(remove.Postings, p => p.Account == bag && p.ItemId == Dye1Item && p.Amount == dye.CancelCount);
        Assert.Contains(remove.Postings, p => p.Account == Fees && p.ItemId == Dye1Item && p.Amount == -dye.CancelCount);
    }
}