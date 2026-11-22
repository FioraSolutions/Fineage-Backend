using Fineage.Accounts;
using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Interest;
using Fineage.Zone.Manor;
using Fineage.Zone.Net;
using Fineage.Game.Economy;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Castle manor after Mobius: the lord sets next period seed sales and crop purchases, the day cycle pays for them and
/// opens them, a farmer buys seeds from the manor manager, sows a monster (the party and watchers see the cast), harvests
/// the corpse and sells the crop for reward items; leftover crops mature in the clan warehouse at the next refresh.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetManorTests
{
    const int Gludio = 1;
    const int DarkCodaSeed = 5016, DarkCoda = 5073, MatureDarkCoda = 5103, Stem = 1864;
    const int Lirein = 20036; // level 18, canBeSown
    static readonly CancellationToken Ct = CancellationToken.None;
    // Gludio castle town region (map-regions castle 1).
    const int Gx = -14288, Gy = 122752, Gz = -3000;

    sealed class FixedRandom(int value) : Random
    {
        public override int Next(int maxValue) => Math.Min(value, maxValue - 1);
        public override int Next() => value;
        public override int Next(int minValue, int maxValue) => Math.Clamp(value, minValue, maxValue - 1);
        public override double NextDouble() => value / 100d;
    }

    static (WorldBench, MutableClock) Open(int roll = 0)
    {
        // 21:00 UTC: the next period is editable (Mobius MODIFIABLE between 20:06 and 04:30).
        var clock = new MutableClock { Now = new DateTimeOffset(2027, 1, 15, 21, 0, 0, TimeSpan.Zero) };
        var bench = new WorldBench(clock: () => clock.Now, manor: ManorSettings.Mobius, manorRandom: new FixedRandom(roll));
        return (bench, clock);
    }

    static List<SystemMessageMsg> Lines(WorldBench bench, int objectId) =>
        bench.Rec.Of("FanoutSystemMessage")
            .Where(a => ((IReadOnlyList<int>)a[0]!).Contains(objectId))
            .Select(a => (SystemMessageMsg)a[1]!).ToList();

    static ManorInfoNetMsg LastInfo(WorldBench bench) =>
        Assert.IsType<ManorInfoNetMsg>(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutManorInfo)).Last()[1]);

    static Task<string?> Cmd(WorldBench bench, BenchPlayer p, int kind, int castleId = Gludio, bool next = false, params ManorRowNet[] rows) =>
        bench.World.TryManorCommandAsync(p.Session,
            NetCodec.DecodeManorCommand(NetCodec.EncodeManorCommand(new ManorCommandNetMsg
            {
                Kind = kind,
                CastleId = castleId,
                NpcObjectId = Manager(bench, p),
                Next = next,
                Rows = rows
            })), Ct);

    /// <summary>Stand next to the Gludio manor manager when the world spawned one; its object id, else 0.</summary>
    static int Manager(WorldBench bench, BenchPlayer p)
    {
        var npc = bench.World.FindNpcTemplateForTest(35103);
        if (npc == null)
            return 0;
        p.MoveTo(npc.X + 20, npc.Y, npc.Z);
        return npc.ObjectId;
    }

    static async Task<BenchPlayer> LordAsync(WorldBench bench, long treasury)
    {
        var lord = await bench.EnterAsync("lord", "LordL", level: 40, x: Gx, y: Gy, z: Gz, clearRecorder: false);
        int characterId = lord.Ticket.CharacterId;
        var clan = bench.Store.InsertClan(
            new ClanRecord(0, "Farmers", 5, characterId, 0, 0, 0,
                [new ClanMemberRecord(characterId, "lord", "LordL", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("lord", characterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(bench.World.ReloadClansForTests());
        lord.ClanId = clan!.Id;
        var castle = bench.World.FindCastle(Gludio)!;
        castle.OwnerClanId = clan.Id;
        castle.Treasury = treasury;
        var chamberlain = bench.World.FindNpcTemplateForTest(35100);
        if (chamberlain != null)
            lord.MoveTo(chamberlain.X + 20, chamberlain.Y, chamberlain.Z);
        return lord;
    }

    static Task<string?> Lord(WorldBench bench, BenchPlayer lord, int kind, params ManorRowNet[] rows) =>
        bench.World.TryManorCommandAsync(lord.Session, new ManorCommandNetMsg { Kind = kind, CastleId = Gludio, Next = true, Rows = rows }, Ct);

    /// <summary>Lord settings tonight, approve at 04:30, open at 20:00 / 20:06: the rows become this period's.</summary>
    static async Task<BenchPlayer> OpenGludioManorAsync(WorldBench bench, MutableClock clock, long treasury = 1_000_000)
    {
        var lord = await LordAsync(bench, treasury);
        Assert.Null(await Lord(bench, lord, ManorCommandKind.SetSeeds, new ManorRowNet { ItemId = DarkCodaSeed, Count = 100, Price = 10 }));
        Assert.Null(await Lord(bench, lord, ManorCommandKind.SetCrops,
            new ManorRowNet { ItemId = DarkCoda, Count = 100, Price = 500, RewardType = 1 }));
        bench.World.ChangeManorModeForTests(); // → approved: pays seeds 5 × 100 + crops 500 × 100
        Assert.Equal(ManorProtocol.ModeApproved, bench.World.Manor.Mode);
        Assert.Equal(treasury - 50_500, bench.World.FindCastle(Gludio)!.Treasury);
        bench.World.ChangeManorModeForTests(); // → maintenance: next becomes current
        Assert.Equal(ManorProtocol.ModeMaintenance, bench.World.Manor.Mode);
        bench.World.ChangeManorModeForTests(); // → editable again, shops open
        Assert.Equal(ManorProtocol.ModeModifiable, bench.World.Manor.Mode);
        return lord;
    }

    [Fact]
    public async Task Buy_sow_harvest_and_sell_with_a_party_and_a_watcher()
    {
        var (bench, clock) = Open();
        var lord = await OpenGludioManorAsync(bench, clock);
        long treasury = bench.World.FindCastle(Gludio)!.Treasury;

        var farmer = await bench.EnterAsync("farmer", "Farmer", [new StoredItem(57, 10_000, -1), new StoredItem(ManorProtocol.HarvesterItemId, 1, -1)],
            level: 18, x: Gx, y: Gy, z: Gz, clearRecorder: false);
        var mate = await bench.EnterAsync("mate", "Mate", level: 18, x: Gx + 30, y: Gy, z: Gz, clearRecorder: false);
        var watcher = await bench.EnterAsync("watch", "Watcher", level: 18, x: Gx - 30, y: Gy, z: Gz, clearRecorder: false);
        bench.World.JoinPartyForTest(farmer.Player, mate.Player);

        // Seeds: 2 × 10 adena into the treasury, 672 to the buyer.
        Assert.Null(await Cmd(bench, farmer, ManorCommandKind.BuyList));
        var list = LastInfo(bench);
        var row = Assert.Single(list.Seeds);
        Assert.Equal((DarkCodaSeed, 100L, 10L), (row.SeedId, row.Amount, row.Price));
        Assert.Null(await Cmd(bench, farmer, ManorCommandKind.BuySeeds, rows: new ManorRowNet { ItemId = DarkCodaSeed, Count = 2 }));
        Assert.Equal(2, farmer.Inventory.CountOf(DarkCodaSeed));
        Assert.Equal(10_000 - 20, farmer.Inventory.CountOf(57));
        Assert.Equal(treasury + 20, bench.World.FindCastle(Gludio)!.Treasury);
        Assert.Equal(98, bench.World.Manor.Product(Gludio, DarkCodaSeed, next: false)!.Amount);
        Assert.Contains(Lines(bench, farmer.ObjectId), l => l.Id == ManorProtocol.AdenaDisappeared);

        // Sow: everyone around sees the Sowing cast; the party hears 889; one seed is used.
        farmer.MoveTo(Gx, Gy, Gz);
        mate.MoveTo(Gx + 30, Gy, Gz);
        watcher.MoveTo(Gx - 30, Gy, Gz);
        var mob = bench.World.SpawnMonsterForTests(Lirein, Gx + 20, Gy, Gz);
        farmer.Session.SelectedTargetId = mob.ObjectId;
        farmer.Player.CurMp = farmer.Player.MaxMp;
        Assert.Null(await farmer.Use(farmer.Slot(DarkCodaSeed)));
        var cast = bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)).Last();
        Assert.Equal(ManorProtocol.SowingSkillId, ((MagicSkillUseMsg)cast[1]!).SkillId);
        Assert.Contains(watcher.ObjectId, (IReadOnlyList<int>)cast[0]!);
        Assert.Contains(mate.ObjectId, (IReadOnlyList<int>)cast[0]!);
        bench.World.ResolvePendingHitForTest(farmer.Player);
        Assert.True(mob.Seeded);
        Assert.Equal(1, farmer.Inventory.CountOf(DarkCodaSeed));
        Assert.Contains(Lines(bench, mate.ObjectId), l => l.Id == ManorProtocol.SowSuccess);
        Assert.Contains(Lines(bench, farmer.ObjectId), l => l.Id == ManorProtocol.SowSuccess);
        Assert.DoesNotContain(Lines(bench, watcher.ObjectId), l => l.Id == ManorProtocol.SowSuccess);
        Assert.Contains(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutPlaySound)),
            a => ((IReadOnlyList<int>)a[0]!).SequenceEqual([farmer.ObjectId]) && (string)a[2]! == "ItemSound.quest_itemget");
        // Level 18 on a level 10 seed: 1 + (18 − 10 − 5) crops.
        Assert.Equal((DarkCoda, 4), (mob.HarvestItemId, mob.HarvestCount));
        clock.Now = clock.Now.AddSeconds(2); // the Sowing cast is over

        // The corpse: the party mate has no harvest rights (891), the sower takes the crop, the mate hears 1137.
        bench.World.KillForTest(mob, farmer.Player);
        var harvester = await bench.EnterAsync("mate2", "MateTwo", [new StoredItem(ManorProtocol.HarvesterItemId, 1, -1)],
            level: 18, x: Gx + 10, y: Gy, z: Gz, clearRecorder: false);
        harvester.Session.SelectedTargetId = mob.ObjectId;
        harvester.Player.CurMp = harvester.Player.MaxMp;
        Assert.Null(await harvester.Use(harvester.Slot(ManorProtocol.HarvesterItemId)));
        bench.World.ResolvePendingHitForTest(harvester.Player);
        clock.Now = clock.Now.AddSeconds(1);
        Assert.Equal(ManorProtocol.NoHarvestRights, Lines(bench, harvester.ObjectId).Last().Id);

        farmer.Session.SelectedTargetId = mob.ObjectId;
        Assert.Null(await farmer.Use(farmer.Slot(ManorProtocol.HarvesterItemId)));
        Assert.Equal(ManorProtocol.HarvestingSkillId,
            ((MagicSkillUseMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)).Last()[1]!).SkillId);
        bench.World.ResolvePendingHitForTest(farmer.Player);
        Assert.Equal(4, farmer.Inventory.CountOf(DarkCoda));
        Assert.Contains(Lines(bench, farmer.ObjectId), l => l.Id == ManorProtocol.ObtainedMany);
        Assert.Contains(Lines(bench, mate.ObjectId), l => l.Id == ManorProtocol.PartyHarvestedMany);
        // Only once.
        clock.Now = clock.Now.AddSeconds(5);
        Assert.Null(await farmer.Use(farmer.Slot(ManorProtocol.HarvesterItemId)));
        bench.World.ResolvePendingHitForTest(farmer.Player);
        Assert.Equal(4, farmer.Inventory.CountOf(DarkCoda));

        // Sell 3 of the 4 crops to Gludio: 3 × 500 adena worth / 100 (Stem price) = 15 Stems, no fee at home.
        Assert.Null(await Cmd(bench, farmer, ManorCommandKind.SellList));
        var sell = LastInfo(bench);
        var crop = Assert.Single(sell.Crops);
        Assert.Equal((DarkCoda, Gludio, 4L, 100L), (crop.CropId, crop.ManorId, crop.InventoryCount, crop.Amount));
        int adena = farmer.Inventory.CountOf(57);
        Assert.Null(await Cmd(bench, farmer, ManorCommandKind.SellCrops, rows: new ManorRowNet
        {
            ObjectId = crop.InventoryObjectId,
            ItemId = DarkCoda,
            ManorId = Gludio,
            Count = 3
        }));
        Assert.Equal(15, farmer.Inventory.CountOf(Stem));
        Assert.Equal(1, farmer.Inventory.CountOf(DarkCoda));
        Assert.Equal(adena, farmer.Inventory.CountOf(57));
        Assert.Equal(97, bench.World.Manor.CropProcure(Gludio, DarkCoda, next: false)!.Amount);
        Assert.Contains(Lines(bench, farmer.ObjectId), l => l.Id == ManorProtocol.TradedCrop);

        // Next refresh (approve, then 20:00): 3 bought crops → 2 mature in the clan warehouse; 97 × 500 back to the treasury.
        long before = bench.World.FindCastle(Gludio)!.Treasury;
        bench.World.ChangeManorModeForTests(); // approved: Mobius carried the settings over, 50 500 paid again
        bench.World.ChangeManorModeForTests(); // maintenance
        Assert.Equal(2, bench.World.ClanWarehouseForTest(lord.ClanId).GetValueOrDefault(MatureDarkCoda));
        Assert.Equal(before - 50_500 + 97 * 500, bench.World.FindCastle(Gludio)!.Treasury);
        // Shops close during maintenance.
        Assert.Equal(ManorProtocol.Maintenance, await Cmd(bench, farmer, ManorCommandKind.BuyList));
        Assert.Contains(Lines(bench, farmer.ObjectId), l => l.Id == ManorProtocol.UnderMaintenance);
    }

    [Fact]
    public async Task Seeds_only_grow_in_their_castle_territory_on_monsters_that_take_them()
    {
        var (bench, _) = Open();
        var farmer = await bench.EnterAsync("far", "Far", [new StoredItem(DarkCodaSeed, 5, -1)], level: 18, x: Gx, y: Gy, z: Gz);

        // A Dion seed does not grow on Gludio land (872).
        var mob = bench.World.SpawnMonsterForTests(Lirein, Gx + 20, Gy - 10, Gz);
        farmer.Session.SelectedTargetId = mob.ObjectId;
        var dion = await bench.EnterAsync("dion", "DionFarmer", [new StoredItem(5024, 1, -1)], level: 18, x: Gx, y: Gy, z: Gz);
        dion.Session.SelectedTargetId = mob.ObjectId;
        Assert.Equal(ManorProtocol.Castle, await dion.Use(dion.Slot(5024)));
        Assert.Equal(ManorProtocol.SeedNotHere, Lines(bench, dion.ObjectId).Last().Id);
        Assert.False(mob.Seeded);

        // A raid boss is not a seeding target (1516).
        farmer.MoveTo(Gx, Gy, Gz);
        var raid = bench.World.SpawnMonsterForTests(25001, Gx + 20, Gy, Gz);
        farmer.Session.SelectedTargetId = raid.ObjectId;
        Assert.Equal(ManorProtocol.Target, await farmer.Use(farmer.Slot(DarkCodaSeed)));
        Assert.Equal(ManorProtocol.TargetNotSeedable, Lines(bench, farmer.ObjectId).Last().Id);

        // A monster without canBeSown takes the cast but keeps the seed (Mobius Sow returns early).
        var frog = bench.World.SpawnMonsterForTests(18319, Gx + 20, Gy + 5, Gz);
        farmer.Session.SelectedTargetId = frog.ObjectId;
        farmer.Player.CurMp = farmer.Player.MaxMp;
        Assert.Null(await farmer.Use(farmer.Slot(DarkCodaSeed)));
        bench.World.ResolvePendingHitForTest(farmer.Player);
        Assert.False(frog.Seeded);
        Assert.Equal(5, farmer.Inventory.CountOf(DarkCodaSeed));

        // An unsown corpse: 893.
        var bag = await bench.EnterAsync("harv", "Harv", [new StoredItem(ManorProtocol.HarvesterItemId, 1, -1)], level: 18, x: Gx, y: Gy, z: Gz);
        var plain = bench.World.SpawnMonsterForTests(Lirein, Gx + 15, Gy - 5, Gz);
        plain.SeederObjectId = bag.ObjectId;
        bench.World.KillForTest(plain, bag.Player);
        bag.Session.SelectedTargetId = plain.ObjectId;
        bag.Player.CurMp = bag.Player.MaxMp;
        Assert.Null(await bag.Use(bag.Slot(ManorProtocol.HarvesterItemId)));
        bench.World.ResolvePendingHitForTest(bag.Player);
        Assert.Equal(ManorProtocol.HarvestNotSown, Lines(bench, bag.ObjectId).Last().Id);

        // A living monster is not a harvest target (109).
        bag.Session.SelectedTargetId = frog.ObjectId;
        Assert.Equal(ManorProtocol.Target, await bag.Use(bag.Slot(ManorProtocol.HarvesterItemId)));
        Assert.Equal(ManorProtocol.InvalidTarget, Lines(bench, bag.ObjectId).Last().Id);
    }

    [Fact]
    public async Task A_failed_sow_uses_the_seed_and_tells_the_sower()
    {
        var (bench, _) = Open(roll: 98);
        var farmer = await bench.EnterAsync("unlucky", "Unlucky", [new StoredItem(DarkCodaSeed, 2, -1)], level: 18, x: Gx, y: Gy, z: Gz);
        var mob = bench.World.SpawnMonsterForTests(Lirein, Gx + 20, Gy, Gz);
        farmer.Session.SelectedTargetId = mob.ObjectId;
        farmer.Player.CurMp = farmer.Player.MaxMp;
        Assert.Null(await farmer.Use(farmer.Slot(DarkCodaSeed)));
        bench.World.ResolvePendingHitForTest(farmer.Player);
        Assert.False(mob.Seeded);
        Assert.Equal(1, farmer.Inventory.CountOf(DarkCodaSeed));
        Assert.Equal(ManorProtocol.SowFailed, Lines(bench, farmer.ObjectId).Last().Id);
    }

    [Fact]
    public async Task Lord_settings_respect_limits_rights_period_and_persist()
    {
        var (bench, _) = Open();
        var lord = await LordAsync(bench, 1_000_000);
        var stranger = await bench.EnterAsync("str", "Stranger", level: 40, x: Gx, y: Gy, z: Gz, clearRecorder: false);

        Assert.Equal(SiegeProtocol.Rights, await Lord(bench, stranger, ManorCommandKind.SetSeeds,
            new ManorRowNet { ItemId = DarkCodaSeed, Count = 10, Price = 10 }));

        // Over the limit (8100) and under 60% of the price (3) are dropped; the valid row stays.
        Assert.Null(await Lord(bench, lord, ManorCommandKind.SetSeeds,
            new ManorRowNet { ItemId = DarkCodaSeed, Count = 9000, Price = 10 },
            new ManorRowNet { ItemId = 5017, Count = 10, Price = 1 },
            new ManorRowNet { ItemId = 5018, Count = 50, Price = 20 }));
        var next = bench.World.Manor.Production(Gludio, next: true);
        Assert.Equal([5018], next.Select(p => p.SeedId));
        var page = LastInfo(bench);
        Assert.Equal(ManorCommandKind.SeedSetting, page.View);
        Assert.Equal(1_000_000, page.Treasury);
        Assert.Contains(page.Seeds, s => s.SeedId == 5018 && s.StartAmount == 50 && s.Price == 20 && s.Limit == 8100);

        var saved = Assert.Single(bench.Store.ListManorProduction());
        Assert.Equal(new ManorProductionSave(Gludio, 5018, 50, 50, 20, true), saved);

        // A fresh world on the same store reads the rows back.
        var again = new ZoneWorld(new RangeAoi(1_000), bench.Game, accounts: bench.Store, clock: bench.Now, manor: ManorSettings.Mobius,
        ledgerJournal: new MemoryLedgerJournal());
        Assert.Equal(50, again.Manor.Product(Gludio, 5018, next: true)!.StartAmount);

        // Approved period: no edits.
        bench.World.ChangeManorModeForTests();
        Assert.Equal(ManorProtocol.Period, await Lord(bench, lord, ManorCommandKind.SetSeeds,
            new ManorRowNet { ItemId = 5018, Count = 5, Price = 20 }));
    }

    [Fact]
    public async Task An_empty_treasury_stops_the_manor_and_tells_the_lord()
    {
        var (bench, _) = Open();
        var lord = await LordAsync(bench, 100);
        Assert.Null(await Lord(bench, lord, ManorCommandKind.SetCrops,
            new ManorRowNet { ItemId = DarkCoda, Count = 10, Price = 50, RewardType = 2 }));
        bench.World.ChangeManorModeForTests();
        Assert.Empty(bench.World.Manor.Procure(Gludio, next: true));
        Assert.Equal(100, bench.World.FindCastle(Gludio)!.Treasury);
        Assert.Equal(ManorProtocol.ManorNotOperating, Lines(bench, lord.ObjectId).Last().Id);
    }

    [Fact]
    public async Task Selling_to_another_castle_costs_a_five_percent_fee()
    {
        var (bench, _) = Open();
        await OpenGludioManorAsync(bench, null!);
        // Dion (2) buys Dark Coda too in our data? Use Gludio's list from a Dion manager position: the fee applies when
        // the manager's castle differs from the procure castle.
        var seller = await bench.EnterAsync("sell", "Seller", [new StoredItem(57, 1_000, -1), new StoredItem(DarkCoda, 2, -1)],
            level: 20, x: Gx, y: Gy, z: Gz);
        var dionManager = bench.World.FindNpcTemplateForTest(35145);
        int npcObject = 0;
        if (dionManager != null)
        {
            seller.MoveTo(dionManager.X + 20, dionManager.Y, dionManager.Z);
            npcObject = dionManager.ObjectId;
        }

        string? error = await bench.World.TryManorCommandAsync(seller.Session, new ManorCommandNetMsg
        {
            Kind = ManorCommandKind.SellCrops,
            CastleId = 2,
            NpcObjectId = npcObject,
            Rows = [new ManorRowNet { ObjectId = seller.Slot(DarkCoda), ItemId = DarkCoda, ManorId = Gludio, Count = 2 }]
        }, Ct);
        Assert.Null(error);
        Assert.Equal(10, seller.Inventory.CountOf(Stem));
        Assert.Equal(1_000 - 50, seller.Inventory.CountOf(57));
    }

    [Fact]
    public void Day_cycle_follows_the_mobius_clock()
    {
        long At(int h, int m) => new DateTimeOffset(2027, 1, 15, h, m, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(ManorProtocol.ModeApproved, ManorRules.ModeAt(At(12, 0), 0));
        Assert.Equal(ManorProtocol.ModeMaintenance, ManorRules.ModeAt(At(20, 3), 0));
        Assert.Equal(ManorProtocol.ModeModifiable, ManorRules.ModeAt(At(20, 6), 0));
        Assert.Equal(ManorProtocol.ModeModifiable, ManorRules.ModeAt(At(3, 0), 0));
        Assert.Equal(ManorProtocol.ModeApproved, ManorRules.ModeAt(At(4, 30), 0));
        // UTC+3 server clock: 17:00 UTC is 20:00 local.
        Assert.Equal(ManorProtocol.ModeMaintenance, ManorRules.ModeAt(At(17, 1), 180));

        Assert.Equal(At(20, 0), ManorRules.NextChange(ManorProtocol.ModeApproved, At(12, 0), 0));
        Assert.Equal(At(20, 6), ManorRules.NextChange(ManorProtocol.ModeMaintenance, At(20, 0), 0));
        Assert.Equal(At(4, 30) + 86_400_000, ManorRules.NextChange(ManorProtocol.ModeModifiable, At(21, 0), 0));
    }

    [Fact]
    public async Task The_tick_advances_the_mode_on_time()
    {
        var (bench, clock) = Open();
        Assert.Equal(ManorProtocol.ModeModifiable, bench.World.Manor.Mode);
        clock.Now = new DateTimeOffset(2027, 1, 16, 4, 30, 0, TimeSpan.Zero);
        bench.World.TickManorForTests();
        Assert.Equal(ManorProtocol.ModeApproved, bench.World.Manor.Mode);
        Assert.Equal(new DateTimeOffset(2027, 1, 16, 20, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), bench.World.Manor.NextChangeMs);
        await Task.CompletedTask;
    }

    [Fact]
    public void Sow_and_harvest_odds_match_mobius()
    {
        Assert.Equal(90, ManorRules.SowChance(false, 10, 10, 10));
        Assert.Equal(20, ManorRules.SowChance(true, 10, 10, 10));
        Assert.Equal(75, ManorRules.SowChance(false, 10, 18, 18));      // 3 levels above seed + 5
        Assert.Equal(65, ManorRules.SowChance(false, 10, 25, 15));      // 10 levels between sower and monster
        Assert.Equal(100, ManorRules.HarvestChance(20, 25));
        Assert.Equal(75, ManorRules.HarvestChance(20, 30));
        Assert.Equal(1, ManorRules.HarvestChance(1, 80));
        Assert.Equal(1, ManorRules.HarvestCount(15, 10, 1));
        Assert.Equal(12, ManorRules.HarvestCount(20, 10, 2)); // (1 + 5) × rate
    }

    [Fact]
    public void Manor_data_covers_the_nine_castles()
    {
        var data = ManorData.Default;
        Assert.Equal(Enumerable.Range(1, 9), data.Seeds.Select(s => s.CastleId).Distinct().OrderBy(i => i));
        Assert.Equal(13, data.ManagerNpcIds.Count());
        Assert.Equal(Gludio, data.ManagerCastle(35103));
        Assert.True(data.CanBeSown(Lirein));
        Assert.True(data.SowableCount > 1000);
        var items = GameData.LoadDefault().Items;
        Assert.All(data.Seeds, s =>
        {
            Assert.True(items.TryGet(s.SeedId, out _), $"seed {s.SeedId}");
            Assert.True(items.TryGet(s.CropId, out _), $"crop {s.CropId}");
            Assert.True(items.TryGet(s.MatureId, out _), $"mature {s.MatureId}");
            Assert.True(items.TryGet(s.Reward1, out _) && items.TryGet(s.Reward2, out _), $"rewards {s.SeedId}");
        });
        var skills = GameData.LoadDefault().Skills;
        Assert.NotNull(skills.Find(ManorProtocol.SowingSkillId));
        Assert.NotNull(skills.Find(ManorProtocol.HarvestingSkillId));
        var dark = data.Seed(DarkCodaSeed)!;
        Assert.Equal((DarkCoda, MatureDarkCoda, 10, 5, 50), (dark.CropId, dark.MatureId, dark.Level, dark.SeedPrice, dark.CropPrice));
    }

    [Fact]
    public void Manor_packets_round_trip()
    {
        var cmd = NetCodec.DecodeManorCommand(NetCodec.EncodeManorCommand(new ManorCommandNetMsg
        {
            Kind = ManorCommandKind.SellCrops,
            CastleId = 3,
            NpcObjectId = 77,
            Next = true,
            Rows = [new ManorRowNet { ItemId = 5073, ObjectId = 9, ManorId = 2, Count = 1L << 33, Price = 5, RewardType = 2 }]
        }));
        Assert.Equal((ManorCommandKind.SellCrops, 3, 77, true), (cmd.Kind, cmd.CastleId, cmd.NpcObjectId, cmd.Next));
        var r = Assert.Single(cmd.Rows);
        Assert.Equal((5073, 9, 2, 1L << 33, 5L, 2), (r.ItemId, r.ObjectId, r.ManorId, r.Count, r.Price, r.RewardType));

        var info = NetCodec.DecodeManorInfo(NetCodec.EncodeManorInfo(new ManorInfoNetMsg
        {
            View = 9,
            CastleId = 1,
            Next = true,
            Mode = 1,
            NextChangeUnixMs = 5,
            Treasury = 6,
            ManorCost = 7,
            Adena = 8,
            Seeds = [new ManorSeedNet { SeedId = 1, CropId = 2, Level = 3, Reward1 = 4, Reward2 = 5, Alternative = true, Limit = 6, ReferencePrice = 7, MinPrice = 8, MaxPrice = 9, Amount = 10, StartAmount = 11, Price = 12, CurrentStartAmount = 13, CurrentPrice = 14 }],
            Crops = [new ManorCropNet { CropId = 1, SeedId = 2, Level = 3, Reward1 = 4, Reward2 = 5, Limit = 6, ReferencePrice = 7, MinPrice = 8, MaxPrice = 9, ManorId = 10, Amount = 11, StartAmount = 12, Price = 13, RewardType = 2, InventoryObjectId = 15, InventoryCount = 16, CurrentStartAmount = 17, CurrentPrice = 18, CurrentRewardType = 1 }]
        }));
        Assert.Equal((9, 1, true, 1, 5L, 6L, 7L, 8L), (info.View, info.CastleId, info.Next, info.Mode, info.NextChangeUnixMs, info.Treasury, info.ManorCost, info.Adena));
        var s = Assert.Single(info.Seeds);
        Assert.Equal((1, 2, 3, 4, 5, true, 6L, 7L, 8L, 9L, 10L, 11L, 12L, 13L, 14L),
            (s.SeedId, s.CropId, s.Level, s.Reward1, s.Reward2, s.Alternative, s.Limit, s.ReferencePrice, s.MinPrice, s.MaxPrice, s.Amount, s.StartAmount, s.Price, s.CurrentStartAmount, s.CurrentPrice));
        var c = Assert.Single(info.Crops);
        Assert.Equal((10, 2, 15, 16L, 17L, 18L, 1), (c.ManorId, c.RewardType, c.InventoryObjectId, c.InventoryCount, c.CurrentStartAmount, c.CurrentPrice, c.CurrentRewardType));
    }
}
