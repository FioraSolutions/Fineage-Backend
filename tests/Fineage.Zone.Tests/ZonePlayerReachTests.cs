using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// A player's melee reach is their own weapon attackRange + both collision radii.
/// It does not grow when the target is walking in, and it does not include the mob's attackRange.
/// </summary>
public sealed class ZonePlayerReachTests
{
    const int MeleeMobId = 9_400_801;
    const int ArcherMobId = 9_400_802;

    [Fact]
    public void Shared_attack_reach_ignores_the_other_side_attack_range()
    {
        // Player dagger 40 vs a radius-10 mob: 40+9+10=59, whether that mob is melee 40 or bow 1100.
        Assert.Equal(59, ZoneCombat.AttackReach(40, 9, 10));
        Assert.Equal(59, ZoneCombat.PlayerAttackReach(40, 10));
        // The mob's own reach may be long; the player's must not adopt it.
        Assert.Equal(1119, ZoneCombat.AttackReach(1100, 10, 9));
        Assert.NotEqual(59, 1119);
    }

    [Fact]
    public async Task Player_cannot_swing_from_beyond_own_reach_at_an_archer_or_approaching_mob()
    {
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["reach"] = new("reach", true,
            [
                new SpawnNpcDefinition(MeleeMobId, 90000 + 801, "Gremlin",
                    NetTestKit.ElfX + 200, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 20, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: []),
                new SpawnNpcDefinition(ArcherMobId, 90000 + 802, "Skeleton Archer",
                    NetTestKit.ElfX, NetTestKit.ElfY + 200, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 20, AttackRange: 1100, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        }), "reach", geo: GeoEngine.Empty);
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        Assert.True(kit.World.TryGetEntityById(MeleeMobId, out var melee));
        Assert.True(kit.World.TryGetEntityById(ArcherMobId, out var archer));
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        // Test spawn rows are not in npcs.json, so collision falls back to DefaultNpcCollision (31).
        int reach = ZoneCombat.AttackReach(
            ZoneCombat.DefaultWeaponRange, ZoneCombat.PlayerCollisionRadius, ZoneCombat.DefaultNpcCollision);
        Assert.Equal(40, melee.AttackRange);
        Assert.Equal(1100, archer.AttackRange);
        Assert.True(Math.Abs(archer.X - player.X) + Math.Abs(archer.Y - player.Y) > reach);
        Assert.True(Math.Abs(melee.X - player.X) + Math.Abs(melee.Y - player.Y) > reach);

        Assert.Null(await kit.World.TrySelectTargetAsync(session, ArcherMobId, CancellationToken.None));
        Assert.Null(await kit.World.TryAttackAsync(session, ArcherMobId, CancellationToken.None));
        Assert.True(player.MoveActive, "a dagger cannot start a swing at an archer standing 200 away");
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));

        session.AutoAttackTargetId = 0;
        session.ClearApproach();
        rec.Calls.Clear();

        // Mid-chase melee mob: dest is the player, body still 200 away. Reach must stay the player's.
        melee.MoveDestX = player.X;
        melee.MoveDestY = player.Y;
        melee.MoveDestZ = player.Z;
        melee.MoveActive = true;
        melee.MoveLastTickMs = kit.Now.ToUnixTimeMilliseconds();
        int x = player.X, y = player.Y;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MeleeMobId, CancellationToken.None));
        Assert.Null(await kit.World.TryAttackAsync(session, MeleeMobId, CancellationToken.None));
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));
        Assert.Equal(x, player.X);
        Assert.Equal(y, player.Y);
    }
}
