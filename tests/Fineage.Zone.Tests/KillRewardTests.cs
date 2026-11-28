using Fineage.Game.Economy;
using Fineage.Net.Contract;
using Fineage.Zone.Drops;
using Fineage.Zone.Net;
using Fineage.Zone.Rewards;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;
using Fineage.Protocol;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>Attackable.calculateRewards</c> + <c>calculateExpAndSp</c> (<c>Attackable.java:1347</c>) and
/// <c>Party.distributeXpAndSp</c> (<c>Party.java:712</c>): exp/SP are a share of the monster template reward, cut off
/// outside <c>MonsterExpMaxLevelDifference</c>.
/// </summary>
public sealed class KillRewardTests
{
    const int MobId = 9_500_001;
    const long MobExp = 1000;
    const int MobSp = 100;
    const int SummonKat = 1111;

    static SpawnCatalog Map(int level = 1) =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["reward"] = new("reward", true,
            [
                new SpawnNpcDefinition(MobId, 95000 + 1, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 200,
                    1f, 1f, 60, 0, "", "Monster",
                    Level: level, Exp: MobExp, Sp: MobSp,
                    AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneEntity Mob, NetBridgeRecorder Rec)> SetupAsync(int level = 1)
    {
        var kit = new NetTestKit(Map(level), "reward");
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        await Task.CompletedTask;
        return (kit, mob, rec);
    }

    const int IdempotenceMobId = MobiusDropTestSeed.TrainingHallGremlinNpcId;

    /// <summary>One-HP Gremlin with a real Mobius droplist and the reward row, so a death rolls loot and XP/SP.</summary>
    static SpawnCatalog IdempotenceMap() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["idempotence"] = new("idempotence", true,
            [
                new SpawnNpcDefinition(IdempotenceMobId, IdempotenceMobId, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 1,
                    1f, 1f, 1, 0, "", "Monster",
                    Level: 1, Exp: MobExp, Sp: MobSp, PAtk: 1, PDef: 1, MAtk: 1, MDef: 1,
                    AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneEntity Mob, NetBridgeRecorder Rec)> IdempotenceSetupAsync()
    {
        var kit = new NetTestKit(IdempotenceMap(), "idempotence", dropRandom: new Random(MobiusDropTestSeed.DefaultSeed));
        Assert.True(kit.World.TryGetEntityById(IdempotenceMobId, out var mob));
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        await Task.CompletedTask;
        return (kit, mob, rec);
    }

    [Fact]
    public async Task Repeated_damage_in_the_same_tick_rolls_one_drop_and_pays_rewards_once()
    {
        var (kit, mob, rec) = await IdempotenceSetupAsync();
        var (_, first) = await kit.EnterAsync();
        var (_, second) = await kit.EnterAsync(x: NetTestKit.ElfX + 10);

        // Two damage sources in one simulation tick. The second one reaches a corpse and must not re-enter the
        // death transition (Mobius Creature.doDie: "killing is only possible one time").
        kit.World.DamageNpcByForTest(first, mob, 100);
        kit.World.DamageNpcByForTest(second, mob, 100);

        Assert.True(mob.Dead);
        Assert.Equal(0, mob.CurHp);
        Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutDie)));
        var loot = Assert.Single(kit.Ledger.Entries, entry => entry.Transaction.Kind == LedgerTxKind.Loot).Transaction;
        Assert.Equal((first.ObjectId, IdempotenceMobId), (loot.CausedBy, loot.Reference));
        Assert.Equal(MobExp, first.Exp + second.Exp);
        Assert.Equal(MobSp, first.Sp + second.Sp);

        // One corpse lifecycle: decay despawns once and the spawn slot comes back alive.
        kit.Now = kit.Now.AddMinutes(10);
        kit.World.AdvanceSpawnsForTests();
        Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutEntityDespawn)));
        // Decay schedules the respawn at now + RespawnDelaySeconds; the spawn stage needs the next tick to bring it back.
        kit.Now = kit.Now.AddSeconds(5);
        kit.World.AdvanceSpawnsForTests();
        Assert.True(kit.World.TryGetEntityById(IdempotenceMobId, out var respawned));
        Assert.False(respawned.Dead);
        Assert.True(respawned.CurHp > 0);
    }

    [Fact]
    public async Task Queued_melee_after_another_killer_does_not_deal_a_second_death()
    {
        var (kit, mob, rec) = await IdempotenceSetupAsync();
        var (swingSession, _) = await kit.EnterAsync();
        var (_, finisher) = await kit.EnterAsync(x: NetTestKit.ElfX + 10);

        // Queue the swing while the mob is alive (the hit lands at hitTime, not at the attack packet), then let
        // another source kill the mob before the melee stage resolves the queued hit in this same tick.
        Assert.Null(await kit.World.TrySelectTargetAsync(swingSession, IdempotenceMobId, CancellationToken.None));
        Assert.Null(await kit.World.TryAttackAsync(swingSession, IdempotenceMobId, CancellationToken.None));
        kit.World.DamageNpcByForTest(finisher, mob, 100);

        kit.Now = kit.Now.AddSeconds(30);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);

        Assert.True(mob.Dead);
        Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutDie)));
        Assert.Single(kit.Ledger.Entries, entry => entry.Transaction.Kind == LedgerTxKind.Loot);
        Assert.Equal(MobExp, finisher.Exp);
        Assert.Equal(MobSp, finisher.Sp);
    }

    static List<SystemMessageMsg> KillNotices(NetBridgeRecorder rec) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Select(args => Assert.IsType<SystemMessageMsg>(args[1]))
            .Where(message => message.Id == ZoneWorld.KillRewardMessageId)
            .ToList();

    [Fact]
    public async Task Solo_killer_takes_the_whole_template_reward()
    {
        var (kit, mob, rec) = await SetupAsync();
        var (_, player) = await kit.EnterAsync();
        mob.DamageBy[player.ObjectId] = 100;

        kit.World.GrantKillRewardForTest(player, mob);

        Assert.Equal(MobExp, player.Exp);
        Assert.Equal(MobSp, player.Sp);
        var notice = Assert.Single(KillNotices(rec));
        Assert.Equal(["1000", "100"], notice.Params);
    }

    [Fact]
    public async Task Damage_share_splits_the_reward_between_solo_attackers()
    {
        var (kit, mob, rec) = await SetupAsync();
        var (_, heavy) = await kit.EnterAsync();
        var (_, light) = await kit.EnterAsync(x: NetTestKit.ElfX + 200);
        mob.DamageBy[heavy.ObjectId] = 750;
        mob.DamageBy[light.ObjectId] = 250;

        kit.World.GrantKillRewardForTest(heavy, mob);

        // 75 % / 25 % of the template reward, not the last hit.
        Assert.Equal(750, heavy.Exp);
        Assert.Equal(250, light.Exp);
        Assert.Equal(75, heavy.Sp);
        Assert.Equal(25, light.Sp);
        Assert.Equal(2, KillNotices(rec).Count);
    }

    [Fact]
    public async Task Attacker_out_of_party_range_gets_nothing()
    {
        var (kit, mob, rec) = await SetupAsync();
        var (_, near) = await kit.EnterAsync();
        var (_, far) = await kit.EnterAsync(x: NetTestKit.ElfX + ZoneWorld.QuestPartyRange + 100);
        mob.DamageBy[near.ObjectId] = 500;
        mob.DamageBy[far.ObjectId] = 500;

        kit.World.GrantKillRewardForTest(near, mob);

        // The far hit is dropped from both the pool and the shares, so the near attacker takes it all.
        Assert.Equal(MobExp, near.Exp);
        Assert.Equal(0, far.Exp);
        Assert.Single(KillNotices(rec));
    }

    [Fact]
    public async Task Monster_far_below_the_killer_level_pays_nothing()
    {
        var (kit, mob, rec) = await SetupAsync();
        var (_, player) = await kit.EnterAsync();
        player.Level = 60;
        mob.DamageBy[player.ObjectId] = 100;

        kit.World.GrantKillRewardForTest(player, mob);

        // Mobius calculateExpAndSp: levelDiff must stay inside ±MonsterExpMaxLevelDifference (Rates.ini 40).
        Assert.Equal(0, player.Exp);
        Assert.Empty(KillNotices(rec));
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutPlayerStats)));
    }

    [Fact]
    public async Task Monster_far_above_the_killer_level_pays_nothing()
    {
        var (kit, mob, rec) = await SetupAsync(level: 70);
        var (_, player) = await kit.EnterAsync();
        mob.DamageBy[player.ObjectId] = 100;

        kit.World.GrantKillRewardForTest(player, mob);

        Assert.Equal(0, player.Exp);
        Assert.Empty(KillNotices(rec));
    }

    [Fact]
    public void Level_85_killer_on_a_grey_mob_takes_the_mobius_penalty()
    {
        // Mobius smooths the high-level penalty on mobs 3+ levels below an 85+ character by an explicit table.
        Assert.Equal(0.97, KillRewardFormula.HighLevelPenalty(-3));
        Assert.Equal(0.67, KillRewardFormula.HighLevelPenalty(-4));
        Assert.Equal(0.03, KillRewardFormula.HighLevelPenalty(-10));
        Assert.Equal(1, KillRewardFormula.HighLevelPenalty(-11));
        Assert.Equal((970d, 97d), KillRewardFormula.CalculateExpAndSp(85, 88, 1000, 100, 10, 10, 40));
    }

    [Fact]
    public async Task Party_pool_takes_the_bonus_and_splits_by_level_squared()
    {
        var (kit, mob, rec) = await SetupAsync();
        var (_, leader) = await kit.EnterAsync();
        var (_, mate) = await kit.EnterAsync(x: NetTestKit.ElfX + 100);
        mate.Level = 2;
        mate.Exp = kit.Game.Classes!.Experience.ExpToBeLevel(2);
        kit.World.JoinPartyForTest(leader, mate);
        mob.DamageBy[leader.ObjectId] = 700;
        mob.DamageBy[mate.ObjectId] = 300;

        kit.World.GrantKillRewardForTest(leader, mob);

        // Party bonus for 2 members is 1.10; the pool splits 1²:2² → 20 % / 80 %.
        long leaderExp = leader.Exp;
        long mateExp = mate.Exp - kit.Game.Classes.Experience.ExpToBeLevel(2);
        Assert.Equal(220, leaderExp);
        Assert.Equal(880, mateExp);
        Assert.Equal(2, KillNotices(rec).Count);
    }

    [Fact]
    public async Task Party_member_too_far_below_the_top_level_is_cut_off()
    {
        var (kit, mob, _) = await SetupAsync(level: 30);
        var (_, leader) = await kit.EnterAsync();
        var (_, lowbie) = await kit.EnterAsync(x: NetTestKit.ElfX + 100);
        leader.Level = 30;
        leader.Exp = kit.Game.Classes!.Experience.ExpToBeLevel(30);
        lowbie.Level = 5;
        lowbie.Exp = kit.Game.Classes.Experience.ExpToBeLevel(5);
        kit.World.JoinPartyForTest(leader, lowbie);
        mob.DamageBy[leader.ObjectId] = 900;
        mob.DamageBy[lowbie.ObjectId] = 100;

        kit.World.GrantKillRewardForTest(leader, mob);

        // PartyXpCutoffMethod = level, PartyXpCutoffLevel = 20: 30 - 5 is over the cutoff, the lowbie earns nothing.
        Assert.Equal(0, lowbie.Exp - kit.Game.Classes.Experience.ExpToBeLevel(5));
        Assert.True(leader.Exp > kit.Game.Classes.Experience.ExpToBeLevel(30));
    }

    [Fact]
    public void Formula_matches_the_mobius_multiplier_table()
    {
        Assert.Equal((0d, 0d), KillRewardFormula.CalculateExpAndSp(70, 24, 1000, 100, 10, 10, 40));
        Assert.Equal((1000d, 100d), KillRewardFormula.CalculateExpAndSp(24, 24, 1000, 100, 10, 10, 40));
        Assert.Equal((500d, 50d), KillRewardFormula.CalculateExpAndSp(24, 24, 1000, 100, 5, 10, 40));
        Assert.Equal((30d, 3d), KillRewardFormula.CalculateExpAndSp(85, 95, 1000, 100, 10, 10, 40));
        Assert.Equal(1.1, KillRewardFormula.PartyBonus(2));
        Assert.Equal(1.2, KillRewardFormula.PartyBonus(3));
        Assert.Equal(2.2, KillRewardFormula.PartyBonus(9));
        Assert.Equal(2.2, KillRewardFormula.PartyBonus(12));
    }

    [Fact]
    public async Task Party_bonus_counts_only_level_valid_members()
    {
        var (kit, mob, _) = await SetupAsync();
        var (_, leader) = await kit.EnterAsync();
        var (_, cutOff) = await kit.EnterAsync(x: NetTestKit.ElfX + 100);
        leader.Level = 25;
        leader.Exp = kit.Game.Classes!.Experience.ExpToBeLevel(25);
        cutOff.Level = 4;
        cutOff.Exp = kit.Game.Classes.Experience.ExpToBeLevel(4);
        kit.World.JoinPartyForTest(leader, cutOff);
        mob.DamageBy[leader.ObjectId] = 600;
        mob.DamageBy[cutOff.ObjectId] = 400;

        kit.World.GrantKillRewardForTest(leader, mob);

        // 25 - 4 > PartyXpCutoffLevel: cutOff is excluded, so no 1.10 party bonus — same pool as a solo kill.
        long leaderGain = leader.Exp - kit.Game.Classes.Experience.ExpToBeLevel(25);
        Assert.Equal(MobExp, leaderGain);
        Assert.Equal(0, cutOff.Exp - kit.Game.Classes.Experience.ExpToBeLevel(4));
    }

    [Fact]
    public async Task Party_member_at_exact_cutoff_level_still_shares_the_pool()
    {
        var (kit, mob, _) = await SetupAsync();
        var (_, high) = await kit.EnterAsync();
        var (_, edge) = await kit.EnterAsync(x: NetTestKit.ElfX + 100);
        high.Level = 25;
        high.Exp = kit.Game.Classes!.Experience.ExpToBeLevel(25);
        edge.Level = 5;
        edge.Exp = kit.Game.Classes.Experience.ExpToBeLevel(5);
        kit.World.JoinPartyForTest(high, edge);
        mob.DamageBy[high.ObjectId] = 500;
        mob.DamageBy[edge.ObjectId] = 500;

        kit.World.GrantKillRewardForTest(high, mob);

        // topLvl - edge.Level == 20 → still in the pool; 2-member bonus 1.10, split 25²:5².
        long highGain = high.Exp - kit.Game.Classes.Experience.ExpToBeLevel(25);
        long edgeGain = edge.Exp - kit.Game.Classes.Experience.ExpToBeLevel(5);
        Assert.Equal(1058, highGain);
        Assert.Equal(42, edgeGain);
        Assert.Equal((long)(MobExp * KillRewardFormula.PartyBonus(2)), highGain + edgeGain);
    }

    [Fact]
    public async Task Party_servitor_penalty_scales_only_that_members_exp_share()
    {
        var (kit, mob, _) = await SetupAsync();
        var (_, plain) = await kit.EnterAsync();
        var (_, tamer) = await kit.EnterAsync(x: NetTestKit.ElfX + 100);
        kit.World.JoinPartyForTest(plain, tamer);
        kit.World.SpawnServitorForTest(tamer, SummonKat, 1);
        mob.DamageBy[plain.ObjectId] = 500;
        mob.DamageBy[tamer.ObjectId] = 500;

        kit.World.GrantKillRewardForTest(plain, mob);

        long plainGain = plain.Exp;
        long tamerGain = tamer.Exp;
        Assert.Equal(550, plainGain);
        Assert.Equal(385, tamerGain);
        Assert.Equal(Math.Round(plainGain * 0.7), tamerGain);
    }

    [Fact]
    public async Task Party_member_out_of_range_earns_nothing_even_in_the_same_party()
    {
        var (kit, mob, rec) = await SetupAsync();
        var (_, near) = await kit.EnterAsync();
        var (_, far) = await kit.EnterAsync(x: NetTestKit.ElfX + ZoneWorld.QuestPartyRange + 100);
        kit.World.JoinPartyForTest(near, far);
        mob.DamageBy[near.ObjectId] = 500;
        mob.DamageBy[far.ObjectId] = 500;

        kit.World.GrantKillRewardForTest(near, mob);

        Assert.Equal(MobExp, near.Exp);
        Assert.Equal(0, far.Exp);
        Assert.Single(KillNotices(rec));
    }
}
