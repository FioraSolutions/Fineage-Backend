using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>WorldRegion</c> deactivation hysteresis (issue #1106). <c>WorldRegion.removeVisibleObject</c> calls
/// <c>startDeactivation</c> only when the 3×3 neighbourhood holds no playable, and that starts a
/// <c>1000 * Config.GRID_NEIGHBOR_TURNOFF_TIME</c> timer (<c>General.ini GridNeighborTurnOffTime = 90</c>,
/// <c>Config.java:2053</c>). Inside the window <c>WorldRegion.startActivation</c> cancels the timer, so a player who
/// steps out and straight back in finds the pawn exactly as it was. When the timer does fire,
/// <c>setActive(false) → switchAI(false)</c> runs unconditionally per attackable: <c>setTarget(null)</c>,
/// <c>stopMove(null)</c>, <c>stopAllEffects()</c>, <c>clearAggroList()</c>, <c>getAttackByList().clear()</c>,
/// <c>getAI().setIntention(IDLE)</c> + <c>stopAITask()</c>, <c>abortAttack()</c>, and <c>teleToLocation(spawn)</c>
/// when the pawn drifted past <c>MaxDriftRange</c>.
/// </summary>
public sealed class ZoneRegionDeactivationHysteresisTests
{
    const int BaseX = 100_000;
    const int BaseY = 100_000;
    const int Z = -3000;
    const int MobId = 9_500_000;

    static SpawnNpcDefinition Mob(int x = BaseX + 100, int y = BaseY + 100) =>
        new(MobId, 20001, "Gremlin", x, y, Z, 0, true, 80, 80f, 40f, 30, 0,
            NpcType: "Monster", Level: 1, Aggressive: false);

    static NetTestKit Kit(SpawnNpcDefinition mob) =>
        new(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["hysteresis"] = new("hysteresis", true, [mob])
        }), "hysteresis", geo: GeoEngine.Empty, aoi: new GridAoi(512));

    static void At(NetTestKit kit, long unixMs) => kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

    /// <summary>
    /// Enter at the mob, run one tick (region active, mob thinking), then move the body far away and run the tick that
    /// observes the empty 3×3 neighbourhood. That observing tick is where Fineage starts the Mobius lease
    /// (<c>WorldRegion.removeVisibleObject → startDeactivation</c>), and it is the returned timestamp.
    /// </summary>
    static async Task<(ZoneSession Session, ZoneEntity Player, ZoneEntity Mob, long LeaseStart)> EnterThenLeaveAsync(
        NetTestKit kit)
    {
        var (session, player) = await kit.EnterAsync(x: BaseX, y: BaseY, z: Z);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        Assert.True(Thinking(mob), "the mob must be thinking while the player stands in its region");

        // Teleport (not a coordinate poke): the AOI forgets the mob, so the watcher clause stops holding it awake and
        // only the WorldRegion lease keeps the pawn alive — the Mobius areNeighborsEmpty() state.
        kit.World.TeleportPlayerForTests(session, BaseX + 200_000, BaseY + 200_000, Z);
        long leaseStart = kit.Now.ToUnixTimeMilliseconds();
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.Contains(MobRegion(mob), kit.World.PlayerActiveRegions);
        Assert.True(Thinking(mob), "the lease starts with the pawn still awake");
        return (session, player, mob, leaseStart);
    }

    static bool Thinking(ZoneEntity npc) => (npc.ActiveSets & ActiveSets.Thinking) != 0;

    static RegionId MobRegion(ZoneEntity mob) => ZoneWorld.MicroRegionAt(mob.InstanceId, mob.X, mob.Y);

    [Fact]
    public async Task Region_keeps_its_ai_for_the_90s_lease_after_the_last_playable_leaves()
    {
        var kit = Kit(Mob());
        var (_, _, mob, leaseStart) = await EnterThenLeaveAsync(kit);
        RegionId region = MobRegion(mob);

        At(kit, leaseStart + 30_000);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.Contains(region, kit.World.PlayerActiveRegions);
        Assert.True(Thinking(mob), "Mobius keeps the region AI for GridNeighborTurnOffTime = 90 s");

        At(kit, leaseStart + ZoneWorld.RegionDeactivationGraceMs - 1);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.Contains(region, kit.World.PlayerActiveRegions);
        Assert.True(Thinking(mob));

        At(kit, leaseStart + ZoneWorld.RegionDeactivationGraceMs);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.DoesNotContain(region, kit.World.PlayerActiveRegions);
        Assert.False(Thinking(mob));
    }

    [Fact]
    public async Task Lease_expiry_sleeps_the_mob_the_way_switchAI_false_does()
    {
        var kit = Kit(Mob());
        var (_, _, mob, start) = await EnterThenLeaveAsync(kit);
        RegionId region = MobRegion(mob);

        // A live debuff is one of the things switchAI(false) drops via stopAllEffects().
        var wind = kit.Game.Skills.Find(1204)!;
        Assert.True(wind.TryLevelData(1, out var windLevel));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(wind, windLevel, mob));
        Assert.NotEmpty(mob.Abnormals);

        mob.HateList[7] = 5;
        mob.DamageBy[7] = 42;
        kit.World.SetAggroTarget(mob, 7);
        mob.Chasing = true;
        Assert.True(Thinking(mob));

        At(kit, start + ZoneWorld.RegionDeactivationGraceMs);
        await kit.World.RunCombatTickAsync(CancellationToken.None);

        Assert.DoesNotContain(region, kit.World.PlayerActiveRegions);
        Assert.False(Thinking(mob));
        Assert.Equal(0, mob.AggroTargetId);
        Assert.Empty(mob.HateList);
        Assert.Empty(mob.DamageBy);
        Assert.False(mob.Chasing);
        Assert.Equal(0, mob.AttackTimeoutAtMs);
        Assert.False(mob.ReturningHome);
        Assert.False(mob.MoveActive);
        Assert.False(mob.Running);
        Assert.Empty(mob.Abnormals);
        Assert.Equal(0, kit.World.ActiveSetCounts.WithAbnormals);
        Assert.Equal(ActiveSets.None, mob.ActiveSets & (ActiveSets.Moving | ActiveSets.Thinking | ActiveSets.AiBusy));
    }

    [Fact]
    public async Task Reentry_inside_the_window_cancels_the_pending_deactivation()
    {
        var kit = Kit(Mob());
        var (session, _, mob, start) = await EnterThenLeaveAsync(kit);
        RegionId region = MobRegion(mob);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.Contains(region, kit.World.PlayerActiveRegions);

        // Mobius startActivation: cancel _neighborsTask and keep self active. Entering at 60 s is inside the 90 s lease.
        At(kit, start + 60_000);
        kit.World.TeleportPlayerForTests(session, BaseX, BaseY, Z);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.Contains(region, kit.World.PlayerActiveRegions);
        Assert.True(Thinking(mob));

        // Well past what would have been the original 90 s deadline: the cancelled timer must not retire the region.
        At(kit, start + ZoneWorld.RegionDeactivationGraceMs + 60_000);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.Contains(region, kit.World.PlayerActiveRegions);
        Assert.True(Thinking(mob));
    }

    [Fact]
    public async Task Deactivation_snaps_a_drifted_mob_back_to_its_spawn()
    {
        var kit = Kit(Mob());
        var (_, _, mob, start) = await EnterThenLeaveAsync(kit);
        RegionId region = MobRegion(mob);

        // Mobius: switchAI(false) calls teleToLocation(spawn) when calculateDistance2D(spawn) > MaxDriftRange. Drift
        // towards -x so the pawn stays inside the same 2048-unit microregion that is being retired.
        mob.X = mob.HomeX - (ZoneNpcSettings.MaxDriftRange + 250);
        mob.Y = mob.HomeY;
        Assert.Equal(region, MobRegion(mob));
        At(kit, start + ZoneWorld.RegionDeactivationGraceMs);
        await kit.World.RunCombatTickAsync(CancellationToken.None);

        Assert.DoesNotContain(region, kit.World.PlayerActiveRegions);
        Assert.Equal(mob.HomeX, mob.X);
        Assert.Equal(mob.HomeY, mob.Y);
        Assert.Equal(mob.HomeZ, mob.Z);
    }

    [Fact]
    public async Task A_fight_without_a_watcher_still_ends_when_the_region_retires()
    {
        var kit = Kit(Mob());
        var (_, player, mob, start) = await EnterThenLeaveAsync(kit);

        // The player left the knownlist, so nothing watches the mob; it still holds the hate the fight left behind.
        Assert.DoesNotContain(mob.ObjectId, player.Knownlist);
        kit.World.SetAggroTarget(mob, player.ObjectId);
        mob.HateList[player.ObjectId] = 100;
        Assert.True(Thinking(mob));

        At(kit, start + ZoneWorld.RegionDeactivationGraceMs);
        await kit.World.RunCombatTickAsync(CancellationToken.None);

        Assert.Equal(0, mob.AggroTargetId);
        Assert.Empty(mob.HateList);
        Assert.False(Thinking(mob));
    }
}
