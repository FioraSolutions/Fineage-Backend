using Fineage.Net.Contract;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>NPCStatus.doRegeneration</c> (<c>CreatureStatus.java:379</c>): a wounded NPC restores
/// <c>max(1, vitals hpRegen/mpRegen)</c> per 3000 ms tick, capped at its maxima, and every tick fans a StatusUpdate
/// to the players who know it. Before this a mob stayed wounded until it died (audit <c>D30</c>).
/// </summary>
public sealed class NpcRegenTests
{
    const int MobId = 9_600_001;

    static SpawnCatalog Map(int level = 1) =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["regen"] = new("regen", true,
            [
                new SpawnNpcDefinition(MobId, 96000 + 1, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 400,
                    1f, 1f, 60, 0, "", "Monster",
                    Level: level, HpRegen: 2.5, MpRegen: 1.2, MaxMp: 100,
                    AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneEntity Mob, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit(Map(), "regen");
        await kit.EnterAsync();
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, mob, rec);
    }

    [Fact]
    public async Task Wounded_mob_heals_by_its_template_rate_each_tick()
    {
        var (kit, mob, _) = await SetupAsync();
        mob.CurHp = 100;
        mob.CurMp = 10;
        kit.World.SyncNeedRegen(mob);

        await kit.World.RunRegenTickAsync(CancellationToken.None);

        Assert.Equal(102, mob.CurHp);
        Assert.Equal(11, mob.CurMp);
    }

    [Fact]
    public async Task Fractional_regen_carries_between_ticks_instead_of_being_lost()
    {
        var (kit, mob, _) = await SetupAsync();
        mob.CurHp = 100;
        mob.MaxHp = 400;
        mob.CurMp = mob.MaxMp;
        kit.World.SyncNeedRegen(mob);

        await kit.World.RunRegenTickAsync(CancellationToken.None);
        Assert.Equal(102, mob.CurHp);

        // 0.5 was carried: the second tick reaches the next whole point instead of stalling at 2/tick.
        await kit.World.RunRegenTickAsync(CancellationToken.None);
        Assert.Equal(105, mob.CurHp);
    }

    [Fact]
    public async Task Regen_never_overshoots_the_maximums()
    {
        var (kit, mob, _) = await SetupAsync();
        mob.CurHp = mob.MaxHp - 1;
        mob.CurMp = mob.MaxMp - 1;
        kit.World.SyncNeedRegen(mob);

        await kit.World.RunRegenTickAsync(CancellationToken.None);

        Assert.Equal(mob.MaxHp, mob.CurHp);
        Assert.Equal(mob.MaxMp, mob.CurMp);
    }

    [Fact]
    public async Task Full_health_mob_regen_tick_is_silent()
    {
        var (kit, mob, rec) = await SetupAsync();

        await kit.World.RunRegenTickAsync(CancellationToken.None);

        Assert.Equal(mob.MaxHp, mob.CurHp);
        Assert.Empty(rec.Of(nameof(INetPresenceBridge.FanoutStatusUpdate)));
    }

    [Fact]
    public async Task Dead_npc_is_not_regenerated()
    {
        var (kit, mob, _) = await SetupAsync();
        mob.CurHp = 0;
        mob.CurMp = 0;

        await kit.World.RunRegenTickAsync(CancellationToken.None);

        Assert.Equal(0, mob.CurHp);
        Assert.Equal(0, mob.CurMp);
    }

    /// <summary>
    /// Mobius <c>AttackableAI.thinkAttack</c> leash (<c>AttackableAI.java:740</c>): pulled past
    /// <c>AggroDistanceCheckRange</c> from spawn, the mob resets and — with <c>AggroDistanceCheckRestoreLife</c> —
    /// comes back to full HP/MP. This is the only place Mobius full-heals a monster.
    /// </summary>
    [Fact]
    public async Task Leash_reset_restores_life_when_restore_life_is_on()
    {
        var (kit, mob, _) = await SetupAsync();
        kit.World.SetAggroTarget(mob, 1);
        mob.CurHp = 40;
        mob.CurMp = 5;
        mob.X = mob.HomeX + 2_500;

        bool reset = kit.World.LeashResetForTest(mob, range: 2_000, raidRange: 4_000, checkRaids: false, restoreLife: true);

        Assert.True(reset);
        Assert.Equal(mob.MaxHp, mob.CurHp);
        Assert.Equal(mob.MaxMp, mob.CurMp);
        Assert.Equal(0, mob.AggroTargetId);
        Assert.True(mob.ReturningHome);
    }

    [Fact]
    public async Task Leash_reset_keeps_the_wound_when_restore_life_is_off()
    {
        var (kit, mob, _) = await SetupAsync();
        kit.World.SetAggroTarget(mob, 1);
        mob.CurHp = 40;
        mob.X = mob.HomeX + 2_500;

        Assert.True(kit.World.LeashResetForTest(mob, 2_000, 4_000, checkRaids: false, restoreLife: false));
        Assert.Equal(40, mob.CurHp);
    }

    [Fact]
    public async Task Mob_inside_the_leash_is_left_alone()
    {
        var (kit, mob, _) = await SetupAsync();
        kit.World.SetAggroTarget(mob, 1);
        mob.CurHp = 40;
        mob.X = mob.HomeX + 1_500;

        Assert.False(kit.World.LeashResetForTest(mob, 2_000, 4_000, checkRaids: false, restoreLife: true));
        Assert.Equal(40, mob.CurHp);
        Assert.Equal(1, mob.AggroTargetId);
    }

    [Fact]
    public async Task Raid_boss_ignores_the_leash_unless_raids_are_enabled()
    {
        var kit = new NetTestKit(RaidMap(), "regen");
        await kit.EnterAsync();
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        kit.World.SetAggroTarget(mob, 1);
        mob.X = mob.HomeX + 5_000;

        Assert.False(kit.World.LeashResetForTest(mob, 2_000, 4_000, checkRaids: false, restoreLife: true));
        Assert.True(kit.World.LeashResetForTest(mob, 2_000, 4_000, checkRaids: true, restoreLife: true));
    }

    /// <summary>
    /// Mobius <c>Attackable.clearAggroList</c> on returnHome drops the damage shares too: a mob that walked away must
    /// not pay the old attacker's share to whoever kills it next.
    /// </summary>
    [Fact]
    public async Task Return_home_forgets_the_damage_share_of_the_old_attacker()
    {
        var (kit, mob, _) = await SetupAsync();
        kit.World.SetAggroTarget(mob, 1);
        mob.DamageBy[1] = 900;
        mob.X = mob.HomeX + 2_500;

        Assert.True(kit.World.LeashResetForTest(mob, 2_000, 4_000, checkRaids: false, restoreLife: false));
        Assert.Empty(mob.DamageBy);
    }

    static SpawnCatalog RaidMap() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["regen"] = new("regen", true,
            [
                new SpawnNpcDefinition(MobId, 96002, "Raid", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 400,
                    1f, 1f, 60, 0, "", "RaidBoss",
                    Level: 30, HpRegen: 2.5, MpRegen: 1.2, MaxMp: 100,
                    AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });
}
