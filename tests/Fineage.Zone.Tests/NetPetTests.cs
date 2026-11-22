using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using System.Linq;
using Fineage.Zone.Geo;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Pets after Mobius CT 0: collar summon (refusals, 5 s cast), stats per level, meal meter and food, exp share and
/// level-up shown on the collar, pet bag (give / take / pick up), death penalty, resurrection, decay, persistence.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetPetTests
{
    const int WolfCollar = 2375, WolfFood = 2515, Wolf = 12077, PetScroll = 6387;
    const int WindFlute = 3500, WindHatchling = 12311, StriderBugle = 4422, WindStrider = 12526;
    const int BabyPanpipe = 6648, BabySpice = 7582, BabyBuffalo = 12780;
    const int HatchlingFood = 4038;
    const int Gremlin = 20001;
    const int X = 46045, Y = 41251, Z = -3440;
    static readonly CancellationToken Ct = CancellationToken.None;

    static (WorldBench Bench, MutableClock Clock) Open()
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        return (new WorldBench(clock: () => clock.Now, geo: GeoEngine.Empty), clock);
    }

    static List<SystemMessageMsg> Lines(WorldBench bench) =>
        bench.Rec.Of("FanoutSystemMessage").Select(args => (SystemMessageMsg)args[1]!).ToList();

    static async Task<BenchPlayer> OwnerAsync(WorldBench bench, string name, params StoredItem[] items)
    {
        var all = new List<StoredItem> { new(57, 1000, -1), new(WolfCollar, 1, -1) };
        all.AddRange(items);
        return await bench.EnterAsync(name.ToLowerInvariant(), name, [.. all], level: 20, x: X, y: Y, z: Z);
    }

    static int CollarOf(BenchPlayer owner) => CollarOf(owner, WolfCollar);

    static int CollarOf(BenchPlayer owner, int itemId) => owner.Inventory.Items.Single(i => i.ItemId == itemId).ObjectId;

    static async Task<ZoneEntity> SummonAsync(WorldBench bench, BenchPlayer owner, int collarItemId = WolfCollar)
    {
        owner.Player.CastingUntilMs = 0;
        owner.Player.CombatUntilUnixMs = 0;
        Assert.Null(await bench.World.TryUseItemAsync(owner.Session, CollarOf(owner, collarItemId), Ct));
        Assert.Null(bench.World.SummonOfForTest(owner.Player)); // 5 s cast
        bench.World.ResolvePendingHitForTest(owner.Player);
        var pet = bench.World.SummonOfForTest(owner.Player);
        Assert.NotNull(pet);
        return pet!;
    }

    static Task<string?> Order(WorldBench bench, BenchPlayer owner, SummonCommandKind kind, int target = 0, int item = 0, int count = 0) =>
        bench.World.TrySummonCommandAsync(owner.Session,
            new SummonCommandNetMsg { Kind = kind, TargetObjectId = target, ItemObjectId = item, Count = count }, Ct);

    static async Task RunAsync(WorldBench bench, MutableClock clock, int steps, int stepMs = 200)
    {
        for (int i = 0; i < steps; i++)
        {
            clock.Now = clock.Now.AddMilliseconds(stepMs);
            await bench.World.RunCombatTickAsync(Ct);
        }
    }

    [Fact]
    public async Task Hatchling_flute_summons_at_pet_template_level_without_ride_flag()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Hatch", new StoredItem(WindFlute, 1, -1));
        var hatch = await SummonAsync(bench, owner, WindFlute);
        Assert.Equal((WindHatchling, 35, SummonKindNet.Pet), (hatch.NpcId, hatch.Level, hatch.Summon!.Kind));
        Assert.Equal(35, owner.Inventory.Find(CollarOf(owner, WindFlute))!.EnchantLevel);
        var info = (SummonInfoNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSummonInfo)).Last()[1]!;
        Assert.False(info.Mountable);
    }

    [Fact]
    public async Task Pet_exchange_ticket_multisell_gives_the_matching_baby_collar()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Ticket", new StoredItem(7583, 1, -1));
        const int PetManagerNpc = 30828;
        var npc = bench.World.SpawnFolkForTests(PetManagerNpc, X + 80, Y, Z);
        Assert.Null(await owner.BypassAsync(npc.ObjectId, "Multisell 101"));
        var multi = (MultiSellListMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMultiSellList)).Last()[1]!;
        int buffalo = multi.Entries.Single(e => e.Ingredients.Any(i => i.ItemId == 7583)).EntryId;
        Assert.Null(await owner.MultiSell(npc.ObjectId, 101, buffalo, 1));
        Assert.Equal(0, owner.Inventory.CountOf(7583));
        Assert.Equal(1, owner.Inventory.CountOf(BabyPanpipe));
    }

    [Fact]
    public async Task Hatchling_flute_can_evolve_to_strider_bugle_at_level_55()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Grow", new StoredItem(WindFlute, 1, -1, 55));
        var hatch = await SummonAsync(bench, owner, WindFlute);
        bench.World.AddPetExpForTest(owner.Player, 0);
        Assert.Equal(55, hatch.Level);
        Assert.True(bench.World.TryEvolvePetCollar(owner.Session, owner.Player, WindFlute, StriderBugle, 55));
        Assert.Null(bench.World.SummonOfForTest(owner.Player));
        Assert.Equal(1, owner.Inventory.CountOf(StriderBugle));
        Assert.Equal(0, owner.Inventory.CountOf(WindFlute));
        var strider = await SummonAsync(bench, owner, StriderBugle);
        Assert.Equal(WindStrider, strider.NpcId);
        Assert.Equal(55, strider.Level);
    }

    [Fact]
    public async Task Baby_pet_heals_the_owner_when_it_eats_baby_spice_while_well_fed()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Baby", new StoredItem(BabyPanpipe, 1, -1), new StoredItem(BabySpice, 2, -1));
        owner.Player.CurHp = owner.Player.MaxHp / 2;
        var baby = await SummonAsync(bench, owner, BabyPanpipe);
        Assert.Equal(BabyBuffalo, baby.NpcId);
        int food = owner.Inventory.Items.Single(i => i.ItemId == BabySpice).ObjectId;
        Assert.Null(await Order(bench, owner, SummonCommandKind.GiveItem, item: food, count: 2));
        baby.Summon!.Pet!.Fed = baby.Summon.Pet.Template.At(baby.Level).MaxMeal;
        int hpBefore = owner.Player.CurHp;
        Assert.Null(await Order(bench, owner, SummonCommandKind.UseItem, item: baby.Summon.Pet.Inventory.Items.Single(i => i.ItemId == BabySpice).ObjectId));
        Assert.True(owner.Player.CurHp > hpBefore);
        Assert.Contains(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)),
            a => (int)((MagicSkillUseMsg)a[1]!).SkillId is 4717 or 4718);
    }

    [Fact]
    public async Task Strider_bugle_summon_info_allows_ride_from_pet_data()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Rider", new StoredItem(StriderBugle, 1, -1, 55));
        var strider = await SummonAsync(bench, owner, StriderBugle);
        Assert.Equal((WindStrider, 55, SummonKindNet.Pet), (strider.NpcId, strider.Level, strider.Summon!.Kind));
        var info = (SummonInfoNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSummonInfo)).Last()[1]!;
        Assert.True(info.Mountable);
    }

    [Fact]
    public async Task Evolved_collar_keeps_pet_progress_when_npc_template_changes()
    {
        const int oid = 880_001;
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Evolve", new StoredItem(StriderBugle, 1, -1, 56, oid));
        bench.Store.SavePet(new StoredPet(oid, owner.ObjectId, WindHatchling, "", 56, 500_000, 400, 200, 800,
            [new StoredItem(HatchlingFood, 2, -1, 0, 880_002)]));
        var strider = await SummonAsync(bench, owner, StriderBugle);
        Assert.Equal(WindStrider, strider.NpcId);
        Assert.Equal(56, strider.Level);
        Assert.Equal(500_000L, strider.Summon!.Pet!.Exp);
        Assert.Equal(800, strider.Summon.Pet.Fed);
        Assert.Equal(2, strider.Summon.Pet.Inventory.CountOf(HatchlingFood));
        Assert.Equal((WindStrider, 56), (bench.Store.FindPet(oid)!.NpcId, bench.Store.FindPet(oid)!.Level));
    }

    [Fact]
    public async Task Collar_summons_the_wolf_at_its_template_level_and_marks_the_collar()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Tamer");
        var watcher = await bench.EnterAsync("eyes", "Eyes", x: X + 100, y: Y, z: Z, clearRecorder: false);
        bench.Rec.Calls.Clear();

        var wolf = await SummonAsync(bench, owner);
        Assert.Equal((Wolf, 15, SummonKindNet.Pet), (wolf.NpcId, wolf.Level, wolf.Summon!.Kind));
        Assert.Equal(247, wolf.MaxHp); // npc template at its own level
        Assert.Equal(wolf.MaxHp, wolf.CurHp);
        Assert.Equal(15, owner.Inventory.Find(CollarOf(owner))!.EnchantLevel);

        var info = (SummonInfoNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSummonInfo)).Last()[1]!;
        Assert.Equal(SummonKindNet.Pet, info.Kind);
        Assert.Equal((1160, 1160), (info.Feed, info.MaxFeed));
        Assert.Equal(54510, info.MaxLoad);
        Assert.True(info.ExpLevelEnd > info.ExpLevelStart && info.Exp == info.ExpLevelStart);
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutPetInventory)));
        var spawn = NetEnterWorldProjector.BuildNpcSpawn(wolf, new EntityId(1, 1));
        Assert.Equal((SummonKindNet.Pet, owner.ObjectId), (spawn.SummonKind, spawn.OwnerObjectId));
        Assert.Contains(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutKnownlistDiff)),
            a => (int)a[0]! == watcher.ObjectId && ((IReadOnlyList<int>)a[1]!).Contains(wolf.ObjectId));

        var row = bench.Store.FindPet(CollarOf(owner));
        Assert.NotNull(row);
        Assert.Equal((Wolf, 15, owner.ObjectId), (row!.NpcId, row.Level, row.OwnerCharacterId));

        owner.Player.CastingUntilMs = 0;
        Assert.Null(await bench.World.TryUseItemAsync(owner.Session, CollarOf(owner), Ct));
        Assert.Equal(ZoneWorld.SysMsgAlreadyHavePet, Lines(bench).Last().Id);
    }

    [Fact]
    public async Task Collar_is_refused_while_sitting_or_in_combat()
    {
        var (bench, clock) = Open();
        var owner = await OwnerAsync(bench, "Sitter");
        owner.Player.Sitting = true;
        Assert.Null(await bench.World.TryUseItemAsync(owner.Session, CollarOf(owner), Ct));
        Assert.Equal(ZoneWorld.SysMsgCannotMoveSitting, Lines(bench).Last().Id);
        owner.Player.Sitting = false;
        owner.Player.CombatUntilUnixMs = clock.Now.ToUnixTimeMilliseconds() + 10_000;
        Assert.Null(await bench.World.TryUseItemAsync(owner.Session, CollarOf(owner), Ct));
        Assert.Equal(ZoneWorld.SysMsgCannotSummonInCombat, Lines(bench).Last().Id);
        Assert.Null(bench.World.SummonOfForTest(owner.Player));
    }

    [Fact]
    public async Task Unsummon_keeps_level_exp_and_meal_for_the_next_summon()
    {
        var (bench, clock) = Open();
        var owner = await OwnerAsync(bench, "Keeper");
        var wolf = await SummonAsync(bench, owner);
        long need = bench.World.Pets.ByItem(WolfCollar)!.ExpForLevel(17) - wolf.Summon!.Pet!.Exp;
        bench.World.AddPetExpForTest(owner.Player, need + 5);
        Assert.Equal(17, wolf.Level);
        Assert.True(wolf.MaxHp > 247);
        Assert.Equal(17, owner.Inventory.Find(CollarOf(owner))!.EnchantLevel);
        long exp = wolf.Summon.Pet.Exp;

        wolf.CombatUntilUnixMs = clock.Now.ToUnixTimeMilliseconds() + 10_000;
        Assert.Null(await Order(bench, owner, SummonCommandKind.Unsummon));
        Assert.Equal(ZoneWorld.SysMsgPetInBattle, Lines(bench).Last().Id);
        wolf.CombatUntilUnixMs = 0;
        wolf.Summon.Pet.Fed = 100; // < 40 %
        Assert.Null(await Order(bench, owner, SummonCommandKind.Unsummon));
        Assert.Equal(ZoneWorld.SysMsgHungryPetNoReturn, Lines(bench).Last().Id);
        wolf.Summon.Pet.Fed = 900;
        wolf.CurHp = 100;
        Assert.Null(await Order(bench, owner, SummonCommandKind.Unsummon));
        Assert.Null(bench.World.SummonOfForTest(owner.Player));

        var again = await SummonAsync(bench, owner);
        Assert.Equal((17, exp, 900, 100), (again.Level, again.Summon!.Pet!.Exp, again.Summon.Pet.Fed, again.CurHp));
    }

    [Fact]
    public async Task Kill_exp_is_shared_with_the_pet_by_its_level_row()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Sharer");
        var wolf = await SummonAsync(bench, owner);
        var solo = await bench.EnterAsync("solo", "Solo", level: 20, x: X, y: Y + 300, z: Z);

        var mob = bench.World.SpawnMonsterForTests(Gremlin, X + 50, Y, Z);
        mob.DamageBy[owner.ObjectId] = mob.MaxHp;
        long ownerExp0 = owner.Player.Exp, petExp0 = wolf.Summon!.Pet!.Exp;
        bench.World.GrantKillRewardForTest(owner.Player, mob);

        var mob2 = bench.World.SpawnMonsterForTests(Gremlin, X, Y + 350, Z);
        mob2.DamageBy[solo.ObjectId] = mob2.MaxHp;
        long soloExp0 = solo.Player.Exp;
        bench.World.GrantKillRewardForTest(solo.Player, mob2);
        long full = solo.Player.Exp - soloExp0;

        Assert.True(full > 0);
        Assert.Equal(Math.Round(full * 0.73), owner.Player.Exp - ownerExp0, 1d);
        Assert.Equal(Math.Round(full * 0.27), wolf.Summon.Pet.Exp - petExp0, 1d);
        Assert.Contains(Lines(bench), l => l.Id == ZoneWorld.SysMsgPetGainedExp);
    }

    [Fact]
    public async Task Meal_drops_every_tick_the_pet_eats_from_its_bag_and_starves_without_food()
    {
        var (bench, clock) = Open();
        var owner = await OwnerAsync(bench, "Feeder", new StoredItem(WolfFood, 5, -1));
        var wolf = await SummonAsync(bench, owner);
        var pet = wolf.Summon!.Pet!;

        bench.World.TickPetFeedForTest(owner.Player);
        Assert.Equal(1158, pet.Fed); // −2 out of battle

        int food = owner.Inventory.Items.Single(i => i.ItemId == WolfFood).ObjectId;
        Assert.Null(await Order(bench, owner, SummonCommandKind.GiveItem, item: food, count: 2));
        Assert.Equal(3, owner.Inventory.CountOf(WolfFood));
        Assert.Equal(2, pet.Inventory.CountOf(WolfFood));

        pet.Fed = 500; // under 55 % of 1160
        bench.World.TickPetFeedForTest(owner.Player);
        Assert.Equal(598, pet.Fed); // −2, then +100 from one food
        Assert.Equal(1, pet.Inventory.CountOf(WolfFood));
        Assert.Contains(Lines(bench), l => l.Id == ZoneWorld.SysMsgPetAteFood);

        Assert.Null(await Order(bench, owner, SummonCommandKind.UseItem, item: pet.Inventory.Items.Single().ObjectId));
        Assert.Equal(698, pet.Fed);
        Assert.Equal(0, pet.Inventory.CountOf(WolfFood));

        pet.Fed = 1;
        bench.World.TickPetFeedForTest(owner.Player);
        if (bench.World.SummonOfForTest(owner.Player) != null)
        {
            Assert.Equal(0, pet.Fed);
            Assert.Equal(ZoneWorld.SysMsgPetUnresponsive, Lines(bench).Last().Id);
            Assert.Null(await Order(bench, owner, SummonCommandKind.Stay));
            Assert.Equal(ZoneWorld.SysMsgPetUnresponsive, Lines(bench).Last().Id);
            for (int i = 0; i < 200 && bench.World.SummonOfForTest(owner.Player) != null; i++)
                bench.World.TickPetFeedForTest(owner.Player);
        }
        Assert.Null(bench.World.SummonOfForTest(owner.Player));
        Assert.Equal(ZoneWorld.SysMsgPetLeftStarving, Lines(bench).Last().Id);
    }

    [Fact]
    public async Task Pet_bag_give_take_and_pick_up()
    {
        var (bench, clock) = Open();
        var owner = await OwnerAsync(bench, "Porter", new StoredItem(WolfFood, 3, -1));
        var wolf = await SummonAsync(bench, owner);
        var pet = wolf.Summon!.Pet!;

        Assert.Null(await Order(bench, owner, SummonCommandKind.GiveItem, item: CollarOf(owner), count: 1));
        Assert.Equal(ZoneWorld.SysMsgPetCannotCarry, Lines(bench).Last().Id);

        int food = owner.Inventory.Items.Single(i => i.ItemId == WolfFood).ObjectId;
        Assert.Null(await Order(bench, owner, SummonCommandKind.GiveItem, item: food, count: 3));
        Assert.Equal(0, owner.Inventory.CountOf(WolfFood));
        var stack = pet.Inventory.Items.Single();
        Assert.Equal(food, stack.ObjectId); // whole stack keeps its object id
        Assert.Null(await Order(bench, owner, SummonCommandKind.TakeItem, item: stack.ObjectId, count: 1));
        Assert.Equal((1, 2), (owner.Inventory.CountOf(WolfFood), pet.Inventory.CountOf(WolfFood)));
        Assert.Equal(2, bench.Store.FindPet(CollarOf(owner))!.Items.Single().Count);

        int ground = bench.World.PlaceGroundForTest(owner.Player, 57, 30);
        Assert.Null(await Order(bench, owner, SummonCommandKind.PickUp, target: ground));
        await RunAsync(bench, clock, 30);
        Assert.Equal(30, pet.Inventory.CountOf(57));
        Assert.False(bench.World.ExistsForTest(ground));
        Assert.Contains(Lines(bench), l => l.Id == ZoneWorld.SysMsgPetPickedUpAdena);
        var pickup = bench.Ledger.Entries.Single(e => e.Transaction.Kind == Fineage.Game.Economy.LedgerTxKind.Pickup).Transaction;
        Assert.Equal(
            [
                (Fineage.Game.Economy.LedgerAccount.Ground(ground), -30L),
                (Fineage.Game.Economy.LedgerAccount.Pet(CollarOf(owner)), 30L)
            ],
            pickup.Postings.Select(p => (p.Account, p.Amount)));
    }

    [Fact]
    public async Task Dead_pet_loses_exp_comes_back_with_the_scroll_and_otherwise_decays_with_its_collar()
    {
        var (bench, clock) = Open();
        var owner = await OwnerAsync(bench, "Mourner", new StoredItem(PetScroll, 1, -1), new StoredItem(WolfFood, 2, -1));
        var wolf = await SummonAsync(bench, owner);
        var pet = wolf.Summon!.Pet!;
        int food = owner.Inventory.Items.Single(i => i.ItemId == WolfFood).ObjectId;
        Assert.Null(await Order(bench, owner, SummonCommandKind.GiveItem, item: food, count: 2));
        bench.World.AddPetExpForTest(owner.Player, 10_000);
        long exp = pet.Exp;

        var mob = bench.World.SpawnMonsterForTests(20218, X + 120, Y, Z); // Premo lv45
        wolf.CurHp = 1;
        Assert.Null(await Order(bench, owner, SummonCommandKind.Attack, target: mob.ObjectId));
        for (int i = 0; i < 300 && !wolf.Dead; i++)
            await RunAsync(bench, clock, 1);
        Assert.True(wolf.Dead);
        Assert.Contains(Lines(bench), l => l.Id == ZoneWorld.SysMsgPetKilled);
        Assert.True(pet.Exp < exp);
        bench.World.DespawnNpcForTests(mob.ObjectId);

        Assert.Null(await Order(bench, owner, SummonCommandKind.Unsummon));
        Assert.Equal(ZoneWorld.SysMsgDeadPetCannotReturn, Lines(bench).Last().Id);

        owner.Session.SelectedTargetId = wolf.ObjectId;
        owner.Player.CastingUntilMs = 0;
        int scroll = owner.Inventory.Items.Single(i => i.ItemId == PetScroll).ObjectId;
        Assert.Null(await bench.World.TryUseItemAsync(owner.Session, scroll, Ct));
        bench.World.ResolvePendingHitForTest(owner.Player);
        Assert.False(wolf.Dead);
        Assert.Equal(exp, pet.Exp); // 100 % restored
        Assert.Equal(0, owner.Inventory.CountOf(PetScroll));

        // Second death, no scroll: 20 minutes later the pet and its collar are gone; its bag goes to the owner.
        bench.World.KillSummonForTest(wolf);
        Assert.True(wolf.Dead);
        clock.Now = clock.Now.AddMilliseconds(ZoneWorld.PetCorpseMs + 1000);
        bench.World.TickSummonsForTest();
        Assert.Null(bench.World.SummonOfForTest(owner.Player));
        Assert.Equal(0, owner.Inventory.CountOf(WolfCollar));
        Assert.Equal(2, owner.Inventory.CountOf(WolfFood));
        Assert.Null(bench.Store.FindPet(pet.ControlObjectId));
    }

    [Fact]
    public void Memory_store_keeps_pets_across_a_snapshot_reload()
    {
        var store = MemoryAccountStore.InMemory();
        Assert.True(store.SavePet(new StoredPet(700_001, 5, Wolf, "Rex", 20, 12345, 300, 100, 900,
            [new StoredItem(WolfFood, 4, -1, 0, 700_002)])));
        var reloaded = MemoryAccountStore.FromSnapshot(store.ExportSnapshot());
        var pet = reloaded.FindPet(700_001);
        Assert.NotNull(pet);
        Assert.Equal(("Rex", 20, 12345L, 900), (pet!.Name, pet.Level, pet.Exp, pet.Fed));
        Assert.Equal((WolfFood, 4, 700_002), (pet.Items[0].ItemId, pet.Items[0].Count, pet.Items[0].ObjectId));
        Assert.True(reloaded.DeletePet(700_001));
        Assert.Null(reloaded.FindPet(700_001));
        Assert.False(store.SavePet(new StoredPet(0, 5, Wolf, "", 1, 0, 0, 0, 0, [])));
    }
}
