using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>AttackableAI</c> has no "return home" flag. A lost target drops the AggroList
/// (<c>AggroInfo.checkHate</c> → hate 0, <c>AggroList</c> pruned) and the pawn goes to
/// <c>AI_INTENTION_ACTIVE</c> (<c>AttackableAI.java:797-802</c>). <c>thinkActive</c> then starts the walk back
/// only when the pawn is past <c>MaxDriftRange = 300</c> of its spawn
/// (<c>AttackableAI.java:625-631</c>, <c>NPC.ini MaxDriftRange</c>). <c>CreatureAI.onEvtArrived</c>
/// (<c>CreatureAI.java:717-724</c>) turns the walk's <c>MOVE_TO</c> intention back into <c>ACTIVE</c> at the spawn,
/// so "returning home" never outlives the walk. Issue #1104.
/// </summary>
public sealed class ReturnHomeClearsAggroTests
{
    const int MobId = 9_620_001;
    const int HomeX = NetTestKit.ElfX;
    const int HomeY = NetTestKit.ElfY;
    const int Z = NetTestKit.ElfZ;
    const int MobHp = 230;

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["return-aggro"] = new("return-aggro", true,
            [
                // WalkSpeed 60 / RunSpeed 140 match the Mobius Misery Skeleton stats the #980 scenes use.
                new SpawnNpcDefinition(MobId, 20022, "Misery Skeleton", HomeX + 55, HomeY, Z, 0,
                    true, MobHp, 140f, 60f, 10, 0, "", "Monster",
                    Level: 14, Exp: 424, Sp: 16, PAtk: 27.246, PDef: 71.082, MAtk: 18.606, MDef: 47.327,
                    AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneEntity Mob, ZoneEntity Player)> SetupAsync()
    {
        var kit = new NetTestKit(Map(), "return-aggro", combatRandom: new Random(9824));
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        var (_, player) = await kit.EnterAsync();
        return (kit, mob, player);
    }

    /// <summary>Drop the fight from the player side: the target leaves the 3000-unit AggroForgetRange.</summary>
    static async Task DropTargetAsync(NetTestKit kit, ZoneEntity player)
    {
        kit.World.TeleportPlayerForTests(player.Session!, HomeX + 6_000, HomeY, Z);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
    }

    /// <summary>
    /// Inside <c>MaxDriftRange</c>: Mobius resolves the intention to <c>ACTIVE</c> and the pawn simply stands.
    /// It must not invent a short walk to the exact spawn point, and the hate must still be gone.
    /// </summary>
    [Fact]
    public async Task Lost_target_inside_the_drift_box_clears_hate_and_stays_put()
    {
        var (kit, mob, player) = await SetupAsync();

        kit.World.DamageNpcByForTest(player, mob, 100);
        Assert.Equal(player.ObjectId, mob.AggroTargetId);
        Assert.NotEmpty(mob.HateList);

        await DropTargetAsync(kit, player);

        Assert.Equal(0, mob.AggroTargetId);
        Assert.Empty(mob.HateList);
        Assert.False(mob.ReturningHome);
        Assert.False(mob.Chasing);
        Assert.False(mob.MoveActive);
        Assert.Equal(HomeX + 55, mob.X);
        Assert.Equal(HomeY, mob.Y);
    }

    /// <summary>
    /// Past <c>MaxDriftRange</c>: <c>setWalking()</c> + <c>returnHome()</c> — the AggroList is cleared, the gait is
    /// walk, and the pawn moves back to its spawn. <c>returnHome</c> itself never heals
    /// (<c>Attackable.java:1685</c>); only the AggroDistanceCheck leash reset does.
    /// </summary>
    [Fact]
    public async Task Lost_target_past_the_drift_box_starts_the_walk_home_at_walk_speed()
    {
        var (kit, mob, player) = await SetupAsync();

        kit.World.DamageNpcByForTest(player, mob, 100);
        int wounded = MobHp - 100;
        Assert.Equal(wounded, mob.CurHp);
        Assert.True(mob.Running);

        // The chase pulled the pawn past its 300-unit drift box.
        mob.X = HomeX + 400;
        mob.Y = HomeY;
        int xBefore;
        await DropTargetAsync(kit, player);
        xBefore = mob.X;

        Assert.Equal(0, mob.AggroTargetId);
        Assert.Empty(mob.HateList);
        Assert.Empty(mob.DamageBy);
        Assert.True(mob.ReturningHome);
        Assert.True(mob.MoveActive);
        Assert.False(mob.Running);
        Assert.Equal(wounded, mob.CurHp);

        // One more 200 ms step of walking at 60 units/s ≈ 12 units toward the spawn, not the 140 run speed.
        kit.Now = kit.Now.AddMilliseconds(ZoneSimulationSettings.StepMilliseconds);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.True(mob.X < xBefore);
        Assert.InRange(xBefore - mob.X, 1, 30);
    }

    /// <summary>
    /// <c>CreatureAI.onEvtArrived</c>: reaching the spawn ends the walk (<c>MOVE_TO</c> → <c>ACTIVE</c>). Leaving
    /// the flag set would keep the pawn in the AI-busy set forever.
    /// </summary>
    [Fact]
    public async Task Arrival_at_the_spawn_ends_the_return()
    {
        var (kit, mob, player) = await SetupAsync();

        kit.World.DamageNpcByForTest(player, mob, 100);
        mob.X = HomeX + 400;
        mob.Y = HomeY;
        await DropTargetAsync(kit, player);
        Assert.True(mob.ReturningHome);

        // 60 units/s over a ~345-unit walk: a few seconds of simulation ticks.
        for (int i = 0; i < 80 && mob.ReturningHome; i++)
        {
            kit.Now = kit.Now.AddMilliseconds(ZoneSimulationSettings.StepMilliseconds);
            await kit.World.RunCombatTickAsync(CancellationToken.None);
        }

        Assert.False(mob.ReturningHome);
        Assert.False(mob.MoveActive);
        Assert.Equal(mob.HomeX, mob.X);
        Assert.Equal(mob.HomeY, mob.Y);
    }
}
