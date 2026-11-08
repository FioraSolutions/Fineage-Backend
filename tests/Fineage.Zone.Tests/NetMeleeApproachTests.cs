using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius moveToPawn on the Net path: RequestAttack on a far mob runs the pawn in (timed move, MoveState to self),
/// ValidatePosition along the run is not corrected onto the mob, and the first swing fires on arrival.
/// </summary>
public sealed class NetMeleeApproachTests
{
    const int MobId = 9_200_001;
    const int Distance = 600;

    static SpawnCatalog Map(int x, int y, int z) =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["approach"] = new("approach", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 1, "Gremlin", x + Distance, y, z, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit(Map(NetTestKit.ElfX, NetTestKit.ElfY, NetTestKit.ElfZ), "approach");
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0); // Human Fighter: melee
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        Assert.Contains(MobId, player.Knownlist);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));
        return (kit, session, player, mob, rec);
    }

    [Fact]
    public async Task Attack_far_mob_runs_in_instead_of_teleporting()
    {
        var (kit, session, player, mob, rec) = await SetupAsync();
        int startX = player.X;

        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));

        // Server position did not jump: still at the start, a timed run toward the mob is active.
        Assert.Equal(startX, player.X);
        Assert.Equal(startX, session.X);
        Assert.True(player.MoveActive);
        Assert.True(player.MoveDestX > startX && player.MoveDestX < mob.X, $"dest {player.MoveDestX} must stop short of mob {mob.X}");
        Assert.True(ZoneCombat.Within(player.MoveDestX, player.MoveDestY, mob.X, mob.Y, ZoneCombat.MeleeRange));

        // Self got a MoveState (client animates the run) from the start point to the stop point.
        var moves = rec.Of(nameof(IWorldEventPublisher.FanoutMoveState));
        Assert.Contains(moves, m => ((IReadOnlyList<int>)m[0]!).Contains(player.ObjectId) && (int)m[1]! == player.ObjectId &&
                                    (int)m[2]! == startX && (int)m[5]! == player.MoveDestX);
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));

        // Halfway: the client is mid-run; ValidatePosition at its interpolated spot is NOT corrected.
        long eta = session.ApproachArriveUnixMs - kit.Now.ToUnixTimeMilliseconds();
        Assert.True(eta > 500, $"eta {eta}ms for {Distance}u");
        kit.Now = kit.Now.AddMilliseconds(eta / 2);
        int midX = startX + (player.MoveDestX - startX) / 2;
        Assert.False(kit.World.ValidateClientPosition(session, midX, player.Y, player.Z, out int dxy, out _), $"drift {dxy}");
        Assert.InRange(player.X, startX + 1, mob.X - 1);

        // Before arrival no swing yet.
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));

        // Arrival: first swing fires from the stop point, the pawn never stands on the mob.
        kit.Now = kit.Now.AddMilliseconds(eta);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        var attacks = rec.Of(nameof(IWorldEventPublisher.FanoutAttackState));
        Assert.NotEmpty(attacks);
        var swing = (AttackStateMsg)attacks[0][1]!;
        Assert.Equal((ulong)(uint)player.ObjectId, swing.AttackerValue);
        Assert.Equal((ulong)(uint)MobId, swing.TargetValue);
        Assert.False(player.MoveActive);
        Assert.True(ZoneCombat.Within(player.X, player.Y, mob.X, mob.Y, ZoneCombat.MeleeRange + ZoneCombat.HitRangeTolerance));
        Assert.True(Math.Abs(player.X - mob.X) >= 10, "pawn must stop in front of the mob, not on it");
        Assert.False(kit.World.ValidateClientPosition(session, player.X, player.Y, player.Z, out _, out _));
    }

    [Fact]
    public async Task Attack_in_reach_swings_without_moving()
    {
        var (kit, session, player, mob, rec) = await SetupAsync();
        // Walk next to the mob first (timed move, then let it finish).
        Assert.Null(await kit.World.TryMoveAsync(session, mob.X - 30, mob.Y, mob.Z, CancellationToken.None));
        kit.Now = kit.Now.AddSeconds(10);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.False(player.MoveActive);
        int x = player.X;
        rec.Calls.Clear();

        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));
        Assert.Equal(x, player.X);
        Assert.False(player.MoveActive);
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));
        Assert.DoesNotContain(rec.Of(nameof(IWorldEventPublisher.FanoutMoveState)), m => (int)m[1]! == player.ObjectId);
    }
}
