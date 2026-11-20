using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Zone.Drops;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Ground stacks on the auditable economy ledger (step 11c): every stack is its own account, filled from a bag or
/// the drop source and emptied into a bag or the destroyed sink, and a piece keeps its object id through the ground.
/// </summary>
public sealed class GroundLedgerTests
{
    const int Adena = 57;
    const int Harvester = 5125;
    const int WoodenArrow = 17;
    const int HerbOfLife = 8600;

    static readonly LedgerAccount Destroyed = LedgerAccount.Of(SystemAccount.Destroyed);

    static List<LedgerTransaction> Moves(NetTestKit kit, LedgerTxKind kind) =>
        kit.Ledger.Entries.Select(entry => entry.Transaction).Where(tx => tx.Kind == kind).ToList();

    static List<string> Lines(LedgerTransaction tx) => tx.Postings
        .Select(posting => $"{posting.Account} {posting.ItemId} {posting.Amount} [{string.Join(",", posting.InstanceIds)}]")
        .Order()
        .ToList();

    static string Line(LedgerAccount account, int itemId, long amount, params int[] pieces) =>
        $"{account} {itemId} {amount} [{string.Join(",", pieces)}]";

    [Fact]
    public async Task A_dropped_piece_keeps_its_id_and_enchant_through_the_ground()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        var piece = player.Inventory!.Add(Harvester, 1);
        player.Inventory.SetEnchantLevel(piece, 3);
        int id = piece.ObjectId;
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var ground = LedgerAccount.Ground(id);

        Assert.Null(await kit.World.TryDropItemAsync(session, id, 1, 0, 0, 0, default));
        Assert.True(kit.World.TryGetEntityById(id, out var stack));
        Assert.Equal((Harvester, 3), (stack.GroundItemId, stack.GroundEnchant));
        Assert.Null(await kit.World.TryPickupAsync(session, id, default));

        var back = Assert.IsType<Fineage.Game.ItemInstance>(player.Inventory.Find(id));
        Assert.Equal(3, back.EnchantLevel);
        var drop = Assert.Single(Moves(kit, LedgerTxKind.Drop));
        Assert.Equal([Line(bag, Harvester, -1, id), Line(ground, Harvester, 1, id)], Lines(drop));
        var pickup = Assert.Single(Moves(kit, LedgerTxKind.Pickup));
        Assert.Equal((player.ObjectId, id), (pickup.CausedBy, pickup.Reference));
        Assert.Equal([Line(bag, Harvester, 1, id), Line(ground, Harvester, -1, id)], Lines(pickup));
        Assert.Equal(bag, kit.World.LedgerForTests.OwnerOf(id));
        Assert.True(kit.World.LedgerForTests.HoldingsOf(ground).SameAs(new LedgerHoldings()));
    }

    [Fact]
    public async Task Party_adena_is_shared_and_the_remainder_is_destroyed()
    {
        var kit = new NetTestKit();
        var (session, picker) = await kit.EnterAsync();
        var (_, second) = await kit.EnterAsync();
        var (_, third) = await kit.EnterAsync();
        kit.World.JoinPartyForTest(picker, second, third);
        Assert.Null(await kit.World.TryGiveItemAsync(session, Adena, 500, default));
        var adena = picker.Inventory!.Items.Single(item => item.ItemId == Adena);

        Assert.Null(await kit.World.TryDropItemAsync(session, adena.ObjectId, 100, 0, 0, 0, default));
        var drop = Assert.Single(Moves(kit, LedgerTxKind.Drop));
        var ground = drop.Postings.Single(posting => posting.Account.Container == LedgerContainer.Ground).Account;
        Assert.Null(await kit.World.TryPickupAsync(session, ground.OwnerId, default));

        int share = PartyLootRules.AdenaShare(100, 3);
        Assert.Equal(33, share);
        var pickup = Assert.Single(Moves(kit, LedgerTxKind.Pickup));
        Assert.Equal(
            new[]
            {
                Line(ground, Adena, -100),
                Line(LedgerAccount.Inventory(picker.ObjectId), Adena, share),
                Line(LedgerAccount.Inventory(second.ObjectId), Adena, share),
                Line(LedgerAccount.Inventory(third.ObjectId), Adena, share),
                Line(Destroyed, Adena, 1)
            }.Order(),
            Lines(pickup));
        Assert.Equal(400 + share, picker.Inventory.CountOf(Adena));
    }

    [Fact]
    public async Task Monster_loot_comes_from_the_drop_source_and_expires_into_the_destroyed_sink()
    {
        var kit = new NetTestKit();
        var (_, killer) = await kit.EnterAsync();
        kit.World.DropDeathLootForTest(MobiusDropTestSeed.TrainingHallGremlinNpcId, killer.X, killer.Y, killer.Z, killer);

        var loot = Assert.Single(Moves(kit, LedgerTxKind.Loot));
        Assert.Equal((killer.ObjectId, MobiusDropTestSeed.TrainingHallGremlinNpcId), (loot.CausedBy, loot.Reference));
        var landed = loot.Postings.Single(posting => posting.Account.Container == LedgerContainer.Ground);
        Assert.Equal(
            new[]
            {
                Line(LedgerAccount.Of(SystemAccount.DropSource), landed.ItemId, -landed.Amount),
                Line(landed.Account, landed.ItemId, landed.Amount, [.. landed.InstanceIds])
            }.Order(),
            Lines(loot));

        kit.Now = kit.Now.AddMilliseconds(ZoneWorld.GroundItemAutoDestroyMs + 1000);
        await kit.World.RunPlayerCombatTickAsync(default);

        Assert.False(kit.World.TryGetEntityById(landed.Account.OwnerId, out _));
        var expire = Assert.Single(Moves(kit, LedgerTxKind.Expire));
        Assert.Equal(landed.Account.OwnerId, expire.Reference);
        Assert.Equal(
            new[]
            {
                Line(landed.Account, landed.ItemId, -landed.Amount, [.. landed.InstanceIds]),
                Line(Destroyed, landed.ItemId, landed.Amount)
            }.Order(),
            Lines(expire));
    }

    [Fact]
    public async Task Destroying_from_the_bag_and_using_a_herb_end_in_the_destroyed_sink()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var arrows = player.Inventory!.Add(WoodenArrow, 10);
        var piece = player.Inventory.Add(Harvester, 1);

        Assert.Null(await kit.World.TryDestroyItemAsync(session, arrows.ObjectId, 3, default));
        Assert.Null(await kit.World.TryDestroyItemAsync(session, piece.ObjectId, 1, default));
        var destroyed = Moves(kit, LedgerTxKind.Destroy);
        Assert.Equal(2, destroyed.Count);
        Assert.Equal([Line(bag, WoodenArrow, -3), Line(Destroyed, WoodenArrow, 3)], Lines(destroyed[0]));
        Assert.Equal([Line(bag, Harvester, -1, piece.ObjectId), Line(Destroyed, Harvester, 1)], Lines(destroyed[1]));

        int herb = kit.World.PlaceGroundForTest(player, HerbOfLife);
        Assert.Null(await kit.World.TryPickupAsync(session, herb, default));
        var used = Assert.Single(Moves(kit, LedgerTxKind.Pickup));
        Assert.Contains(Line(Destroyed, HerbOfLife, 1), Lines(used));
        Assert.True(kit.World.LedgerForTests.HoldingsOf(LedgerAccount.Ground(herb)).SameAs(new LedgerHoldings()));
        Assert.True(kit.World.LedgerForTests.HoldingsOf(bag).SameAs(kit.World.LedgerHoldingsForTests(bag)));
    }
}
