using Fineage.Game.Economy;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>Creature.doDie</c> ends the fight: <c>Attackable.getMostHated</c> is null for a dead owner
/// (<c>Attackable.java:876</c> checks <c>isAlikeDead</c>), the AggroList is dropped on decay
/// (<c>Creature.decayMe</c>, <c>Creature.java:2465</c>) and rebuilt empty on spawn
/// (<c>Attackable.onSpawn</c>, <c>Attackable.java:1455</c>). Fineage clears the same state at the death
/// transition, so a corpse can never expose stale hate (issue #1105).
/// </summary>
public sealed class DeathClearsAggroTests
{
    const int MobId = 9_610_001;
    const long MobExp = 1000;
    const int MobSp = 100;

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["death-aggro"] = new("death-aggro", true,
            [
                new SpawnNpcDefinition(MobId, 20022, "Misery Skeleton", NetTestKit.ElfX + 50, NetTestKit.ElfY, NetTestKit.ElfZ, 0,
                    true, 230, 80f, 40f, 10, 0, "", "Monster",
                    Level: 1, Exp: MobExp, Sp: MobSp, PAtk: 1, PDef: 1, MAtk: 1, MDef: 1,
                    AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneEntity Mob, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit(Map(), "death-aggro", combatRandom: new Random(9891));
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        await Task.CompletedTask;
        return (kit, mob, rec);
    }

    [Fact]
    public async Task Death_clears_the_aggro_list_but_keeps_the_damage_share_for_rewards()
    {
        var (kit, mob, rec) = await SetupAsync();
        var (_, player) = await kit.EnterAsync();

        kit.World.DamageNpcByForTest(player, mob, 100);
        Assert.Equal(player.ObjectId, mob.AggroTargetId);
        Assert.Equal(ZoneWorld.DamageHate(100, mob.Level), mob.HateList[player.ObjectId]);

        kit.World.DamageNpcByForTest(player, mob, 9999);

        Assert.True(mob.Dead);
        Assert.Equal(0, mob.CurHp);
        Assert.Equal(0, mob.AggroTargetId);
        Assert.Empty(mob.HateList);
        // Mobius Attackable.calculateRewards runs over the AggroInfo damage shares inside doDie, so the split of the
        // kill must survive the cleared hate.
        Assert.Equal(10_099, mob.DamageBy[player.ObjectId]);
        Assert.Equal(MobExp, player.Exp);
        Assert.Equal(MobSp, player.Sp);
        Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutDie)));
    }

    [Fact]
    public async Task Hit_after_death_does_not_grow_hate_or_pay_twice()
    {
        var (kit, mob, rec) = await SetupAsync();
        var (_, player) = await kit.EnterAsync();

        kit.World.DamageNpcByForTest(player, mob, 9999);
        Assert.True(mob.Dead);

        kit.World.DamageNpcByForTest(player, mob, 777);

        Assert.Empty(mob.HateList);
        Assert.Equal(0, mob.AggroTargetId);
        Assert.Equal(MobExp, player.Exp);
        Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutDie)));
    }

    [Fact]
    public async Task Respawned_mob_has_no_ghost_hate_from_the_previous_lifecycle()
    {
        var (kit, mob, _) = await SetupAsync();
        var (_, player) = await kit.EnterAsync();

        kit.World.DamageNpcByForTest(player, mob, 9999);
        Assert.True(mob.Dead);

        // Mobius DecayTaskManager removes the corpse and Spawn.decreaseCount schedules the respawn.
        kit.Now = kit.Now.AddMinutes(10);
        kit.World.AdvanceSpawnsForTests();
        Assert.False(kit.World.TryGetEntityById(MobId, out _));
        kit.Now = kit.Now.AddSeconds(15);
        kit.World.AdvanceSpawnsForTests();
        Assert.True(kit.World.TryGetEntityById(MobId, out var respawned));
        Assert.NotSame(mob, respawned);
        Assert.False(respawned.Dead);
        Assert.True(respawned.CurHp > 0);
        Assert.Empty(respawned.HateList);
        Assert.Empty(respawned.DamageBy);
        Assert.Equal(0, respawned.AggroTargetId);
    }
}
