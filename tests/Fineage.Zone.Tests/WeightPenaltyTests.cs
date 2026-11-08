using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.Combat;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Pure overload maths from Mobius <c>Player.refreshOverloaded()</c> (<c>Player.java:1970-2015</c>) and the load limit
/// it is measured against (<c>Creature.getMaxLoad()</c>, <c>Creature.java:6278-6288</c>):
/// <c>weightproc = (load - bonusWeightPenalty) * 1000 / maxLoad</c> with Java integer division, then
/// <c>&lt;500 / &lt;666 / &lt;800 / &lt;1000 / else</c> → levels 0/1/2/3/4.
/// </summary>
public sealed class WeightPenaltyMathTests
{
    /// <summary>A 100000 limit makes every threshold land on a whole load: 49999 → 499‰, 50000 → 500‰, …</summary>
    const int Limit = 100_000;

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(49_999L, 0)]   // 499‰ — one unit under the first threshold
    [InlineData(50_000L, 1)]   // 500‰ — exactly 50% of the limit
    [InlineData(66_599L, 1)]   // 665‰
    [InlineData(66_600L, 2)]   // 666‰
    [InlineData(79_999L, 2)]   // 799‰
    [InlineData(80_000L, 3)]   // 800‰
    [InlineData(99_999L, 3)]   // 999‰
    [InlineData(100_000L, 4)]  // 1000‰ — level 4 starts once the bonus-adjusted load reaches the limit
    [InlineData(250_000L, 4)]  // over the limit stays 4
    public void Level_follows_the_mobius_thresholds(long load, int expected) =>
        Assert.Equal(expected, WeightPenalty.Level(load, Limit));

    [Fact]
    public void Java_integer_division_truncates_before_the_threshold_compare()
    {
        // 49999 * 1000 / 100000 = 499, not 499.99 — so it stays level 0 exactly like Java.
        Assert.Equal(499L, WeightPenalty.WeightPercent(49_999, Limit));
        Assert.Equal(0, WeightPenalty.Level(49_999, Limit));

        // Uneven limit still truncates: 66 598 of 133 198 is 499.992‰ → 499 (66 599 would be exactly 500).
        Assert.Equal(499L, WeightPenalty.WeightPercent(66_598, 133_198));
        Assert.Equal(0, WeightPenalty.Level(66_598, 133_198));
        Assert.Equal(500L, WeightPenalty.WeightPercent(66_599, 133_198));
        Assert.Equal(1, WeightPenalty.Level(66_599, 133_198));
    }

    [Fact]
    public void Bonus_weight_penalty_is_subtracted_before_the_percentage()
    {
        // Stat.WEIGHT_PENALTY defaults to 1 (Creature.java:6294), so the raw load is nudged down by one unit.
        Assert.Equal(501L, WeightPenalty.WeightPercent(50_100, Limit));
        Assert.Equal(500L, WeightPenalty.WeightPercent(50_100, Limit, bonusWeightPenalty: 1));
        Assert.Equal(1, WeightPenalty.Level(50_100, Limit));

        // A heavy armour set (+5795) pushes the same bag a whole level down.
        Assert.Equal(0, WeightPenalty.Level(50_100, Limit, bonusWeightPenalty: 5_796));

        // A bonus larger than the load is level 0, never negative.
        Assert.Equal(0, WeightPenalty.Level(1_000, Limit, bonusWeightPenalty: 6_000));
    }

    [Fact]
    public void Zero_or_negative_limit_skips_the_check()
    {
        // Player.refreshOverloaded wraps the whole block in `if (maxLoad > 0)` (Player.java:1973).
        Assert.Equal(0, WeightPenalty.Level(999_999, 0));
        Assert.Equal(0, WeightPenalty.Level(999_999, -1));
        Assert.Equal(0L, WeightPenalty.WeightPercent(999_999, 0));
    }

    [Fact]
    public void IsOverloaded_is_strictly_more_than_the_limit()
    {
        // Player.java:2004 setOverloaded(getCurrentLoad() > maxLoad) — equality is not "overloaded".
        Assert.False(WeightPenalty.IsOverloaded(Limit, Limit));
        Assert.True(WeightPenalty.IsOverloaded(Limit + 1, Limit));
        Assert.False(WeightPenalty.IsOverloaded(0, 0));
    }

    [Fact]
    public void Base_max_load_is_con_bonus_times_69000_times_alt_weight_limit()
    {
        // Elven Fighter CON 36 → stat bonus 1.28 (data/stat-bonuses.json): 1.28 * 69000 = 88320.
        Assert.Equal(88_320, WeightPenalty.BaseMaxLoad(1.28));
        Assert.Equal(88_320, WeightPenalty.BaseMaxLoad(1.28, 1.0));
        Assert.Equal(88_320, WeightPenalty.BaseMaxLoad(1.28, WeightPenalty.DefaultAltWeightLimit));

        // Config.ALT_WEIGHT_LIMIT multiplies before the floor (Creature.java:6284).
        Assert.Equal(176_640, WeightPenalty.BaseMaxLoad(1.28, 2.0));
        Assert.Equal(88_322, WeightPenalty.BaseMaxLoad(1.28, 1.00003)); // 88322.64 floored
        Assert.Equal(0, WeightPenalty.BaseMaxLoad(0));
    }

    [Fact]
    public void Base_max_load_rejects_negative_inputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WeightPenalty.BaseMaxLoad(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => WeightPenalty.BaseMaxLoad(1, -1));
    }

    [Fact]
    public void Skill_ids_and_constants_match_the_mobius_data()
    {
        Assert.Equal(4270, WeightPenalty.SkillId);
        Assert.Equal(4, WeightPenalty.MaxLevel);
        Assert.Equal(1, WeightPenalty.DefaultAltWeightLimit);

        // WarehouseRules/ShopTrade used to own their own 69000 literal; they now delegate here.
        Assert.Equal(WeightPenalty.BaseMaxLoad(1.28), Fineage.Game.Services.WarehouseRules.MaxLoad(1.28));
        Assert.Equal(69_000, Fineage.Game.Services.WarehouseRules.BaseMaxLoad);
    }

    [Theory]
    [InlineData(null, 1d)]
    [InlineData("", 1d)]
    [InlineData("  ", 1d)]
    [InlineData("2.5", 2.5d)]
    [InlineData("0", 1d)]
    [InlineData("-3", 1d)]
    [InlineData("nope", 1d)]
    public void Alt_weight_limit_setting_parses_like_the_ini(string? text, double expected) =>
        Assert.Equal(expected, ZoneWeightSettings.ParseAltWeightLimit(text), 6);
}

/// <summary>
/// Skill 4270 (<c>data/stats/skills/04200-04299.xml:1329-1346</c>) folded through the ordinary
/// <see cref="CombatStatMods"/> pipeline: <c>regHp</c>/<c>regMp</c> ×0.5 at every level and
/// <c>runSpd</c> ×1 / ×0.5 / ×0.5 / ×0 on top of the DEX factor and the player-only 250 cap.
/// </summary>
public sealed class WeightPenaltySkillTests
{
    /// <summary>Elven Fighter 18: base run 125 / walk 90, DEX 35 → stat bonus 1.15.</summary>
    const float ElfDexBonus = 1.15f;

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, session, player, rec);
    }

    /// <summary>Folds the live player's stats with the weight level forced to <paramref name="level"/>.</summary>
    static (float Run, float Walk, double RegHp, double RegMp) FoldedAt(NetTestKit kit, ZoneEntity player, int level)
    {
        int before = player.WeightPenaltyLevel;
        player.WeightPenaltyLevel = level;
        try
        {
            var mods = kit.World.PlayerStatMods(player);
            return (kit.World.RunSpeedStat(player, mods), kit.World.WalkSpeedStat(player, mods),
                mods.Apply(CombatStat.RegHp, 10d), mods.Apply(CombatStat.RegMp, 10d));
        }
        finally
        {
            player.WeightPenaltyLevel = before;
        }
    }

    [Fact]
    public void Catalog_carries_the_4270_funcs()
    {
        var passives = SkillStatEffectCatalog.LoadPassives();
        Assert.True(passives.TryGet(WeightPenalty.SkillId, out var effect),
            "Weight Penalty 4270 must be in data/skill-passives.json");
        Assert.Equal(4, effect.Levels);

        var runSpd = effect.Funcs.Single(f => f.Stat == CombatStat.RunSpd);
        Assert.Equal('m', runSpd.Op);
        Assert.Equal([1d, 0.5d, 0.5d, 0d], runSpd.Values);

        // regHp/regMp are halved at every level, including level 1 where runSpd stays ×1.
        Assert.Equal([0.5d, 0.5d, 0.5d, 0.5d], effect.Funcs.Single(f => f.Stat == CombatStat.RegHp).Values);
        Assert.Equal([0.5d, 0.5d, 0.5d, 0.5d], effect.Funcs.Single(f => f.Stat == CombatStat.RegMp).Values);
    }

    [Fact]
    public async Task Level_one_keeps_run_speed_but_halves_regeneration()
    {
        var (kit, _, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;
        float baseWalk = player.BaseWalkSpeed * ElfDexBonus;

        var at1 = FoldedAt(kit, player, 1);
        Assert.Equal(baseRun, at1.Run, 3);   // runSpd ×1
        Assert.Equal(baseWalk, at1.Walk, 3);
        Assert.Equal(5d, at1.RegHp, 6);      // regHp ×0.5
        Assert.Equal(5d, at1.RegMp, 6);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Levels_two_and_three_halve_every_speed(int level)
    {
        var (kit, _, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;

        var at = FoldedAt(kit, player, level);
        Assert.Equal(baseRun * 0.5f, at.Run, 3);
        Assert.Equal(player.BaseWalkSpeed * ElfDexBonus * 0.5f, at.Walk, 3);
        Assert.Equal(5d, at.RegHp, 6);
        Assert.Equal(5d, at.RegMp, 6);
    }

    [Fact]
    public async Task Level_four_is_a_legal_zero_speed()
    {
        var (kit, _, player, _) = await SetupAsync();

        var at4 = FoldedAt(kit, player, 4);
        // runSpd ×0: the pawn stands still. There is no lower floor on MOVE_SPEED, so 0 is the wire value.
        Assert.Equal(0f, at4.Run);
        Assert.Equal(0f, at4.Walk);
        Assert.Equal(5d, at4.RegHp, 6);
        Assert.Equal(5d, at4.RegMp, 6);

        // Swim rides the same stat and stops too.
        player.InWater = true;
        try
        {
            var swim = FoldedAt(kit, player, 4);
            Assert.Equal(0f, swim.Run);
            Assert.Equal(0f, swim.Walk);
        }
        finally
        {
            player.InWater = false;
        }
    }

    [Fact]
    public async Task Level_zero_leaves_every_stat_untouched()
    {
        var (kit, _, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;

        var at0 = FoldedAt(kit, player, 0);
        Assert.Equal(baseRun, at0.Run, 3);
        Assert.Equal(10d, at0.RegHp, 6);
        Assert.Equal(10d, at0.RegMp, 6);
    }

    [Fact]
    public async Task Penalty_composes_with_a_speed_buff_instead_of_replacing_it()
    {
        var (kit, _, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;

        // Mobius order is MUL 20 then ADD 30, so Wind Walk's +20 lands after the ×0.5.
        ZoneAbnormals.Apply(player.Abnormals, 1204, 1, 1200, kit.Now.ToUnixTimeMilliseconds(), 1);
        try
        {
            int before = player.WeightPenaltyLevel;
            player.WeightPenaltyLevel = 2;
            var mods = kit.World.PlayerStatMods(player);
            Assert.Equal(baseRun * 0.5f + 20f, kit.World.RunSpeedStat(player, mods), 3);
            player.WeightPenaltyLevel = before;
        }
        finally
        {
            player.Abnormals.Clear();
        }
    }

    [Fact]
    public async Task Npc_entities_never_get_a_weight_penalty()
    {
        var (kit, _, _, _) = await SetupAsync();
        var npc = new ZoneEntity
        {
            ObjectId = 90_010, Kind = ZoneEntityKind.Npc, NpcId = 20001, Name = "Wolf",
            RunSpeed = 120f, Dex = 30
        };
        Assert.False(kit.World.RefreshWeightPenalty(npc));
        Assert.Equal(0, npc.WeightPenaltyLevel);
    }
}

/// <summary>
/// Live <see cref="ZoneWorld"/> behaviour: the level is derived from the bag, recomputed on every inventory write,
/// published as <c>ChangeMoveType</c> + <c>EtcStatusUpdate</c>, survives a relog, and reverts when the load drops.
/// </summary>
public sealed class WeightPenaltyWorldTests
{
    /// <summary>Blessed Scroll of Escape: stackable, weight 120 — the bulk filler for an exact load.</summary>
    const int ScrollId = 1538;
    /// <summary>Soulshot: No Grade for Beginners: stackable, weight 1 — the exact remainder.</summary>
    const int SingleId = 5789;

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        // A fresh Mobius character is not at level 0: initialEquipment.xml:33-45 hands every fighter 5000 soulshots at
        // weight 4 (item 1835) plus scrolls and potions, which is 46080 of the 88320 limit — 52%, i.e. already
        // weight-penalty level 1. Strike the bag down to the paperdoll (14650) for a clean baseline.
        StripToPaperdoll(player);
        Assert.True(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(0, player.WeightPenaltyLevel);
        rec.Calls.Clear();
        return (kit, session, player, rec);
    }

    /// <summary>Removes every unequipped item; the paperdoll keeps contributing its weight, as in Mobius.</summary>
    internal static void StripToPaperdoll(ZoneEntity player)
    {
        foreach (var item in player.Inventory!.Items.ToList())
        {
            if (!item.IsEquipped)
                player.Inventory.Remove(item);
        }
    }

    /// <summary>Loads exactly <paramref name="target"/> into the bag: keep the paperdoll, fill with weight-120/1 stacks.</summary>
    static void SetLoad(ZoneEntity player, long target)
    {
        var inv = player.Inventory!;
        foreach (var item in inv.Items.ToList())
        {
            if (!item.IsEquipped)
                inv.Remove(item);
        }

        long delta = target - inv.TotalWeight;
        Assert.True(delta >= 0, $"SetLoad only grows the bag (target {target}, equipped base {inv.TotalWeight})");
        if (delta / 120 > 0)
            inv.Add(ScrollId, (int)(delta / 120));
        if (delta % 120 > 0)
            inv.Add(SingleId, (int)(delta % 120));
        Assert.Equal(target, inv.TotalWeight);
    }

    /// <summary>Smallest load whose <c>weightproc</c> reaches <paramref name="permille"/> for this character.</summary>
    static long MinLoadFor(NetTestKit kit, ZoneEntity player, int permille)
    {
        int max = kit.World.MaxLoadOf(player);
        long bonus = kit.World.BonusWeightPenaltyOf(player);
        long load = bonus + (long)permille * max / 1000;
        while (WeightPenalty.WeightPercent(load, max, bonus) < permille)
            load++;
        return load;
    }

    [Fact]
    public async Task Enter_derives_the_penalty_from_the_saved_bag()
    {
        // Mobius GameClient:493 → refreshOverloaded() runs before the first UserInfo, so a character that logs in
        // already overloaded carries the level (and its slowed speeds) from the very first packet.
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);

        Assert.Equal(
            WeightPenalty.Level(player.Inventory!.TotalWeight, kit.World.MaxLoadOf(player), kit.World.BonusWeightPenaltyOf(player)),
            player.WeightPenaltyLevel);

        // The shipped Mobius starter kit (initialEquipment.xml:33-45) is 5000 soulshots at weight 4 + scrolls + potions
        // = 46080 of an 88320 limit, i.e. 52%: a brand-new elven fighter already sits at level 1 and regenerates at half
        // rate. That is real Mobius behaviour, not a bug — checked here so nobody "fixes" it into level 0 later.
        Assert.Equal(46_080L, player.Inventory.TotalWeight);
        Assert.Equal(1, player.WeightPenaltyLevel);
        Assert.NotNull(session);
    }

    [Fact]
    public async Task Overloaded_level_survives_a_relog_and_is_recomputed_on_enter()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);

        long target = MinLoadFor(kit, player, 800);
        SetLoad(player, target);
        Assert.True(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(3, player.WeightPenaltyLevel);

        await kit.World.LeaveAsync(session, CancellationToken.None);

        // Re-entering rebuilds the inventory from the store and runs refreshOverloaded again after RefreshKnownSkills.
        Assert.True(await kit.World.TryEnterAsync(session, CancellationToken.None));
        Assert.True(kit.World.TryGetLivePlayer(session, out var back));
        Assert.Equal(target, back.Inventory!.TotalWeight);
        Assert.Equal(3, back.WeightPenaltyLevel);
        Assert.True(back.RunSpeed < back.BaseRunSpeed * 1.15f);
    }

    [Fact]
    public async Task Crossing_into_level_one_sends_etc_status_but_keeps_the_speed()
    {
        var (kit, _, player, rec) = await SetupAsync();
        SetLoad(player, MinLoadFor(kit, player, 500) - 1);
        Assert.False(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(0, player.WeightPenaltyLevel);

        float cleanRun = player.RunSpeed;
        rec.Calls.Clear();

        SetLoad(player, MinLoadFor(kit, player, 500));
        Assert.True(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(1, player.WeightPenaltyLevel);

        // Level 1 is runSpd ×1 (only regen halves), so no ChangeMoveType is due — Mobius sends just EtcStatusUpdate.
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutChangeMoveType)));
        Assert.Equal(cleanRun, player.RunSpeed, 3);

        var etcArgs = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutEtcStatusUpdate)));
        var status = Assert.IsType<Fineage.Protocol.EtcStatusUpdateMsg>(etcArgs[1]);
        Assert.Equal(1, status.WeightPenalty);
        // The packet is self-only, exactly like Mobius (Player.java:2012).
        Assert.Equal([player.ObjectId], Assert.IsAssignableFrom<IReadOnlyList<int>>(etcArgs[0]));
    }

    [Fact]
    public async Task Crossing_into_level_two_sends_the_halved_speed_to_watchers()
    {
        var (kit, _, player, rec) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * 1.15f;

        SetLoad(player, MinLoadFor(kit, player, 666));
        Assert.True(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(2, player.WeightPenaltyLevel);
        Assert.Equal(baseRun * 0.5f, player.RunSpeed, 2);

        var move = rec.Of(nameof(IWorldEventPublisher.FanoutChangeMoveType)).Last();
        Assert.Equal(player.ObjectId, (int)move[1]!);
        Assert.Equal(player.Running, (bool)move[2]!);
        Assert.Equal(player.RunSpeed, (float)move[3]!, 2);
        Assert.Equal(player.WalkSpeed, (float)move[4]!, 2);
        Assert.False((bool)move[5]!); // dry land
    }

    [Fact]
    public async Task Dropping_the_load_restores_the_speed_and_clears_the_level()
    {
        var (kit, _, player, rec) = await SetupAsync();
        float clean = player.RunSpeed;

        SetLoad(player, MinLoadFor(kit, player, 900));
        Assert.True(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(3, player.WeightPenaltyLevel);
        Assert.True(player.RunSpeed < clean);
        rec.Calls.Clear();

        // Remove the padding again: the same recompute must walk the level all the way back to 0.
        foreach (var item in player.Inventory!.Items.ToList())
        {
            if (!item.IsEquipped)
                player.Inventory.Remove(item);
        }

        Assert.True(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(0, player.WeightPenaltyLevel);
        Assert.Equal(clean, player.RunSpeed, 3);

        var statuses = rec.Of(nameof(IWorldEventPublisher.FanoutEtcStatusUpdate))
            .Select(args => ((Fineage.Protocol.EtcStatusUpdateMsg)args[1]!).WeightPenalty)
            .ToList();
        Assert.Equal(0, statuses[^1]); // recovery publishes the cleared level last
    }

    [Fact]
    public async Task Level_four_stops_the_pawn_and_frees_it_on_recovery()
    {
        var (kit, _, player, _) = await SetupAsync();

        SetLoad(player, MinLoadFor(kit, player, 1000));
        Assert.True(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(4, player.WeightPenaltyLevel);
        Assert.Equal(0f, player.RunSpeed);
        Assert.Equal(0f, player.WalkSpeed);
        Assert.Equal(0f, ZoneWorld.MoveSpeedOf(player));

        foreach (var item in player.Inventory!.Items.ToList())
        {
            if (!item.IsEquipped)
                player.Inventory.Remove(item);
        }

        Assert.True(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(0, player.WeightPenaltyLevel);
        Assert.True(player.RunSpeed > 0f);
        Assert.True(ZoneWorld.MoveSpeedOf(player) > 0f);
    }

    [Fact]
    public async Task The_give_and_grant_paths_recompute_the_level()
    {
        var (kit, session, player, rec) = await SetupAsync();

        // Every inventory write funnels through SaveInventory (Mobius PlayerInventory.refreshWeight):
        // a real give must therefore move the level without any explicit refresh call.
        long need = MinLoadFor(kit, player, 666) - player.Inventory!.TotalWeight;
        int count = (int)((need + 119) / 120); // Blessed Scroll of Escape, weight 120
        Assert.InRange(count, 1, ZoneWorld.MaxGrantCount);

        Assert.Null(await kit.World.TryGiveItemAsync(session, ScrollId, count,
            CancellationToken.None));
        Assert.Equal(2, player.WeightPenaltyLevel);
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutEtcStatusUpdate)));

        // Dropping the same stack walks it back down through the same hook.
        var stack = player.Inventory.FindByItemId(ScrollId);
        Assert.NotNull(stack);
        Assert.Null(await kit.World.TryDropItemAsync(session, stack!.ObjectId, count, 0, 0, 0, CancellationToken.None));
        Assert.Equal(0, player.WeightPenaltyLevel);

        // GrantItemRequest is the one add path that does not go through SaveInventory (the bag is persisted on leave),
        // so it calls the recompute itself; it must move the level just the same.
        Assert.Null(await kit.World.TryGrantItemAsync(session, ScrollId, count,
            CancellationToken.None));
        Assert.Equal(2, player.WeightPenaltyLevel);
    }

    [Fact]
    public async Task Max_load_folds_the_weight_limit_passive()
    {
        var kit = new NetTestKit();
        var (_, player) = await kit.EnterAsync(classId: 18);

        int baseLoad = kit.World.MaxLoadOf(player);
        Assert.Equal(WeightPenalty.BaseMaxLoad(1.28, ZoneWeightSettings.AltWeightLimit), baseLoad);
        Assert.Equal(88_320, baseLoad); // AltWeightLimit is 1 unless the env overrides it

        // Weight Limit 150 is a real passive in the regenerated catalog: weightLimit ×2 at level 1.
        player.KnownSkills[150] = 1;
        try
        {
            Assert.Equal(baseLoad * 2, kit.World.MaxLoadOf(player));
        }
        finally
        {
            player.KnownSkills.Remove(150);
        }

        Assert.Equal(baseLoad, kit.World.MaxLoadOf(player));
    }

    [Fact]
    public async Task Bonus_weight_penalty_comes_from_the_armor_set_passive()
    {
        var kit = new NetTestKit();
        var (_, player) = await kit.EnterAsync(classId: 18);

        // Creature.java:6294 calcStat(Stat.WEIGHT_PENALTY, 1): the default is 1, not 0.
        Assert.Equal(1L, kit.World.BonusWeightPenaltyOf(player));

        // Composite Heavy Armor Set 3512 adds 5795 (data/stats/skills/03500-03599.xml).
        player.KnownSkills[3512] = 1;
        try
        {
            Assert.Equal(5_796L, kit.World.BonusWeightPenaltyOf(player));
        }
        finally
        {
            player.KnownSkills.Remove(3512);
        }

        Assert.Equal(1L, kit.World.BonusWeightPenaltyOf(player));
    }

    [Fact]
    public async Task Etc_status_carries_the_weight_level_and_the_grade_flag()
    {
        var (_, _, player, _) = await SetupAsync();
        player.WeightPenaltyLevel = 3;
        player.WeaponGradePenalty = 2;
        player.ArmorGradePenalty = 0;

        var status = ZoneWorld.EtcStatusOf(player);
        Assert.Equal(3, status.WeightPenalty);
        Assert.True(status.GradePenalty); // Mobius writes the flag when either expertise penalty is above zero
        Assert.False(status.ChatBlocked);
        Assert.False(status.DangerArea);
        Assert.False(status.CharmOfCourage);
        Assert.Equal(0, status.Charges);
        Assert.Equal(0, status.DeathPenalty);

        player.WeaponGradePenalty = 0;
        Assert.False(ZoneWorld.EtcStatusOf(player).GradePenalty);
    }

    [Fact]
    public async Task Net_etc_status_is_projected_on_enter()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);
        Assert.True(kit.World.TryBuildNetEtcStatus(session, out var status));
        Assert.Equal(player.WeightPenaltyLevel, status.WeightPenalty);
        var net = Fineage.Zone.Net.NetZoneServer.ToNetEtcStatus(status, player.ObjectId);
        Assert.Equal(1u, net.EntityGeneration);
        Assert.Equal((ulong)(uint)player.ObjectId, net.EntityValue);
    }

    [Fact]
    public void Etc_status_codec_roundtrips_every_field()
    {
        var msg = new EtcStatusUpdateNetMsg
        {
            EntityValue = 77, EntityGeneration = 3, Charges = 5, WeightPenalty = 4,
            ChatBlocked = true, DangerArea = true, GradePenalty = true, CharmOfCourage = true, DeathPenalty = 7
        };
        var back = NetCodec.DecodeEtcStatusUpdate(NetCodec.EncodeEtcStatusUpdate(msg));
        Assert.Equal(msg.EntityValue, back.EntityValue);
        Assert.Equal(msg.EntityGeneration, back.EntityGeneration);
        Assert.Equal(msg.Charges, back.Charges);
        Assert.Equal(msg.WeightPenalty, back.WeightPenalty);
        Assert.Equal(msg.ChatBlocked, back.ChatBlocked);
        Assert.Equal(msg.DangerArea, back.DangerArea);
        Assert.Equal(msg.GradePenalty, back.GradePenalty);
        Assert.Equal(msg.CharmOfCourage, back.CharmOfCourage);
        Assert.Equal(msg.DeathPenalty, back.DeathPenalty);
        Assert.Equal(0x035D, NetOpcode.EtcStatusUpdate);
    }
}

/// <summary>
/// Regen at half rate (skill 4270's <c>regHp</c>/<c>regMp</c> ×0.5). Vitals are ints here, so a halved rate must
/// accumulate through the per-entity fractional carry instead of being truncated away every tick.
/// </summary>
public sealed class WeightPenaltyRegenTests
{
    /// <summary>Clean baseline: the paperdoll only (14650 of 88320 = 16%), so the penalty level is 0.</summary>
    static async Task<(NetTestKit Kit, ZoneEntity Player)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);
        Assert.NotNull(session);
        WeightPenaltyWorldTests.StripToPaperdoll(player);
        Assert.True(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(0, player.WeightPenaltyLevel);

        player.CurHp = 1;
        player.CurMp = 1;
        player.RegenCarryHp = 0;
        player.RegenCarryMp = 0;
        return (kit, player);
    }

    static async Task TickAsync(NetTestKit kit, int count = 1)
    {
        for (int i = 0; i < count; i++)
            await kit.World.RunRegenTickAsync(CancellationToken.None);
    }

    /// <summary>Clean per-tick rate the world will actually pay for this player, from the shared regen pipeline.</summary>
    static (double Hp, double Mp) CleanRate(NetTestKit kit, ZoneEntity player) =>
        kit.World.RegenRate(player);

    [Fact]
    public async Task Overloaded_regen_is_half_of_clean_regen()
    {
        double cleanHpPerTick, cleanMpPerTick;
        int cleanHp, cleanMp;
        {
            var (kit, player) = await SetupAsync();
            (cleanHpPerTick, cleanMpPerTick) = CleanRate(kit, player);
            await TickAsync(kit, 4);
            cleanHp = player.CurHp - 1;
            cleanMp = player.CurMp - 1;
            Assert.Equal((int)Math.Floor(cleanHpPerTick * 4), cleanHp);
            Assert.Equal((int)Math.Floor(cleanMpPerTick * 4), cleanMp);
        }

        {
            var (kit, player) = await SetupAsync();
            player.WeightPenaltyLevel = 1;
            // Skill 4270 regHp/regMp ×0.5 halves the rate; the fractional remainder is carried between ticks.
            await TickAsync(kit, 4);
            Assert.Equal(cleanHp / 2, player.CurHp - 1);
            Assert.Equal(cleanMp / 2, player.CurMp - 1);
        }
    }

    [Fact]
    public async Task Half_rate_carries_the_remainder_between_ticks()
    {
        var (kit, player) = await SetupAsync();
        player.WeightPenaltyLevel = 1;

        // Halving the clean rate leaves a fraction; it must land as a whole point on a later tick instead of
        // being floored away every tick and never healing at all.
        double half = kit.World.RegenRate(player).Hp;
        Assert.True(half - Math.Floor(half) > 0, $"expected a fractional half-rate, got {half}");

        await TickAsync(kit);
        await TickAsync(kit);

        // Two ticks of a fractional rate pay out floor(2 × rate), never 2 × floor(rate).
        Assert.Equal(1 + (int)Math.Floor(half * 2), player.CurHp);
        Assert.Equal(half * 2 - Math.Floor(half * 2), player.RegenCarryHp, 6);
    }

    [Fact]
    public async Task Clearing_the_penalty_restores_the_full_rate()
    {
        var (kit, player) = await SetupAsync();
        player.WeightPenaltyLevel = 1;
        await TickAsync(kit);
        double leftover = player.RegenCarryHp;
        Assert.True(leftover > 0, $"the penalised tick should stash a fraction, got {leftover}");

        player.WeightPenaltyLevel = 0;
        int before = player.CurHp;
        double clean = kit.World.RegenRate(player).Hp;
        // The leftover carry is not thrown away: leftover + the clean rate pays out on this same tick.
        await TickAsync(kit);
        Assert.Equal(before + (int)Math.Floor(leftover + clean), player.CurHp);
        Assert.Equal(leftover + clean - Math.Floor(leftover + clean), player.RegenCarryHp, 6);
    }

    [Fact]
    public async Task Regen_masteries_and_the_penalty_share_one_pipeline()
    {
        var (kit, player) = await SetupAsync();

        // Sitting multiplies the base rate by 1.5 before the ×0.5 penalty fold, so sitting overloaded still
        // beats standing clean — the two folds share one pipeline instead of overwriting each other.
        player.Sitting = true;
        player.WeightPenaltyLevel = 1;
        double sittingPenalised = kit.World.RegenRate(player).Hp;
        player.Sitting = false;
        player.WeightPenaltyLevel = 0;
        double standingClean = kit.World.RegenRate(player).Hp;

        Assert.True(sittingPenalised > standingClean / 2,
            $"sitting×1.5 then ×0.5 ({sittingPenalised}) should stay well above half of standing clean ({standingClean})");
    }
}
