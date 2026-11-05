using Fineage.Net.Contract;
using Fineage.Zone.Net;
using Fineage.Zone.Rewards;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

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

    static List<SystemMessageNetMsg> KillNotices(NetBridgeRecorder rec) =>
        rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage))
            .Select(args => Assert.IsType<SystemMessageNetMsg>(args[1]))
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
        Assert.Empty(rec.Of(nameof(INetPresenceBridge.FanoutPlayerStats)));
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
        Assert.Equal(2.2, KillRewardFormula.PartyBonus(9));
        Assert.Equal(2.2, KillRewardFormula.PartyBonus(12));
    }
}
