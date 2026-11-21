using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-5c: sweep spoil loot and pet feeding / transfer on the economy ledger.</summary>
public sealed class GatheringLedgerTests
{
    const int WolfCollar = 2375;
    const int WolfFood = 2515;
    const int X = 46045, Y = 41251, Z = -3440;
    static readonly CancellationToken Ct = CancellationToken.None;

    static LedgerTransaction Only(MemoryLedgerJournal ledger, LedgerTxKind kind) =>
        Assert.Single(ledger.Entries, e => e.Transaction.Kind == kind).Transaction;

    static LedgerTransaction Last(MemoryLedgerJournal ledger, LedgerTxKind kind) =>
        ledger.Entries[^1].Transaction.Kind == kind
            ? ledger.Entries[^1].Transaction
            : Only(ledger, kind);

    static WorldBench OpenPetBench()
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        return new WorldBench(clock: () => clock.Now, geo: GeoEngine.Empty);
    }

    static async Task<BenchPlayer> OwnerAsync(WorldBench bench, string name, params StoredItem[] extra)
    {
        var items = new List<StoredItem> { new(57, 1000, -1), new(WolfCollar, 1, -1) };
        items.AddRange(extra);
        return await bench.EnterAsync(name.ToLowerInvariant(), name, [.. items], level: 20, x: X, y: Y, z: Z);
    }

    static int CollarOf(BenchPlayer owner) => owner.Inventory.Items.Single(i => i.ItemId == WolfCollar).ObjectId;

    static async Task SummonAsync(WorldBench bench, BenchPlayer owner)
    {
        owner.Player.CastingUntilMs = 0;
        owner.Player.CombatUntilUnixMs = 0;
        Assert.Null(await bench.World.TryUseItemAsync(owner.Session, CollarOf(owner), Ct));
        bench.World.ResolvePendingHitForTest(owner.Player);
        Assert.NotNull(bench.World.SummonOfForTest(owner.Player));
    }

    static Task<string?> Order(WorldBench bench, BenchPlayer owner, SummonCommandKind kind, int item = 0, int count = 0) =>
        bench.World.TrySummonCommandAsync(owner.Session,
            new SummonCommandNetMsg { Kind = kind, ItemObjectId = item, Count = count }, Ct);

    [Fact]
    public async Task Sweep_loot_is_journaled_from_the_drop_source()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var (session, player) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        Assert.Null(await kit.World.TryAdminSpoilAsync(session, new AdminSpoilTargetMsg { TargetObjectId = mob.ObjectId }, default));
        kit.World.KillForTest(mob, player);
        const int Stem = 1864;
        mob.SweepItems.Clear();
        mob.SweepItems.Add(new Fineage.Zone.Drops.DropStack(Stem, 3));
        Assert.Null(await kit.World.TryAdminSweepAsync(session, new AdminSweepCorpseMsg { TargetObjectId = mob.ObjectId }, default));

        var sweep = Last(kit.Ledger, LedgerTxKind.SweepLoot);
        Assert.Equal(mob.NpcId, sweep.Reference);
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var source = LedgerAccount.Of(SystemAccount.DropSource);
        Assert.Contains(sweep.Postings, p => p.Account == bag && p.ItemId == Stem && p.Amount == 3);
        Assert.Contains(sweep.Postings, p => p.Account == source && p.ItemId == Stem && p.Amount == -3);
    }

    [Fact]
    public async Task Pet_feed_consumes_food_on_the_ledger()
    {
        var bench = OpenPetBench();
        var owner = await OwnerAsync(bench, "feeder", new StoredItem(WolfFood, 5, -1));
        await SummonAsync(bench, owner);
        var pet = bench.World.SummonOfForTest(owner.Player)!.Summon!.Pet!;
        int foodObj = owner.Inventory.Items.Single(i => i.ItemId == WolfFood).ObjectId;
        Assert.Null(await Order(bench, owner, SummonCommandKind.GiveItem, foodObj, 1));
        int before = bench.Ledger.Entries.Count;
        pet.Fed = 500;
        bench.World.TickPetFeedForTest(owner.Player);
        var feed = Assert.Single(bench.Ledger.Entries.Skip(before), e => e.Transaction.Kind == LedgerTxKind.PetFeed).Transaction;
        var petAcct = LedgerAccount.Pet(pet.ControlObjectId);
        Assert.Contains(feed.Postings, p => p.Account == petAcct && p.ItemId == WolfFood && p.Amount == -1);
        Assert.Contains(feed.Postings, p => p.Account == LedgerAccount.Of(SystemAccount.Destroyed) && p.ItemId == WolfFood && p.Amount == 1);
    }

    [Fact]
    public async Task Pet_item_transfer_posts_between_owner_and_pet()
    {
        var bench = OpenPetBench();
        var owner = await OwnerAsync(bench, "xfer", new StoredItem(WolfFood, 4, -1));
        await SummonAsync(bench, owner);
        var pet = bench.World.SummonOfForTest(owner.Player)!.Summon!.Pet!;
        int foodObj = owner.Inventory.Items.Single(i => i.ItemId == WolfFood).ObjectId;
        int before = bench.Ledger.Entries.Count;
        Assert.Null(await Order(bench, owner, SummonCommandKind.GiveItem, foodObj, 2));
        var xfer = Assert.Single(bench.Ledger.Entries.Skip(before), e => e.Transaction.Kind == LedgerTxKind.PetItemTransfer).Transaction;
        Assert.Equal(pet.ControlObjectId, xfer.Reference);
        var bag = LedgerAccount.Inventory(owner.ObjectId);
        var petAcct = LedgerAccount.Pet(pet.ControlObjectId);
        Assert.Contains(xfer.Postings, p => p.Account == bag && p.ItemId == WolfFood && p.Amount == -2);
        Assert.Contains(xfer.Postings, p => p.Account == petAcct && p.ItemId == WolfFood && p.Amount == 2);
    }
}