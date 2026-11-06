using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius moveToPawn approach uses FindPath when the straight line is geo-blocked (#273).</summary>
public sealed class ZoneApproachPathTests
{
    const int MobId = 9_200_101;

    static GeoEngine FlatWithWall()
    {
        var region = GeoEngine.Region.Flat1(96);
        region.SetComplexBlock(1, 0, (cx, cy) =>
        {
            byte n = GeoEngine.All;
            if (cx == 3) n = (byte)(n & ~GeoEngine.East);
            if (cx == 4) n = (byte)(n & ~GeoEngine.West);
            return (96, n);
        });
        var geo = new GeoEngine();
        geo.SetRegion(20, 18, region);
        return geo;
    }

    static SpawnCatalog Map(int px, int py, int pz, int mx, int my, int mz) =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["approach_path"] = new("approach_path", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 2, "Gremlin", mx, my, mz, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    [Fact]
    public void RemainingPathTravelMs_sums_waypoints()
    {
        var e = new ZoneEntity
        {
            X = 0, Y = 0,
            MoveDestX = 100, MoveDestY = 0,
            MoveActive = true
        };
        e.MovePath.Enqueue((200, 0, 0));
        e.MovePath.Enqueue((200, 100, 0));
        // 100 + 100 + 100 = 300 at speed 100 → 3000 ms
        Assert.Equal(3000, ZoneWorld.RemainingPathTravelMs(e, 100f));
    }

    [Fact]
    public async Task Approach_around_wall_queues_geo_path_and_longer_eta()
    {
        var geo = FlatWithWall();
        // Same layout as GeoEngineTests.Blocked_move_finds_path_around_wall_end.
        int px = 8, py = 40, pz = 96;
        int mx = 400, my = 40, mz = 96;
        var kit = new NetTestKit(Map(px, py, pz, mx, my, mz), "approach_path", geo: geo);
        var who = kit.Create(classId: 0, race: 0, x: px, y: py, z: pz);
        var (session, player) = await kit.EnterAsync(who);
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));

        double dist = Math.Sqrt((mx - px) * (mx - px) + (my - py) * (my - py));
        double stopAt = Math.Max(10, ZoneCombat.MeleeRange - ZoneCombat.ApproachSlack);
        float speed = player.RunSpeed;
        long straightMs = Math.Max(1, (long)Math.Ceiling((dist - stopAt) * 1000.0 / speed));

        Assert.Null(await kit.World.TryAttackAsync(session, new AttackRequestMsg { TargetObjectId = MobId }, CancellationToken.None));

        Assert.True(player.MoveActive);
        Assert.True(player.MovePath.Count > 0, "straight line hits the wall — FindPath must queue detour waypoints");
        long eta = session.ApproachArriveUnixMs - kit.Now.ToUnixTimeMilliseconds();
        Assert.True(eta >= straightMs,
            $"path ETA {eta}ms should be at least straight {straightMs}ms (detour is longer)");
        // First leg is a geo waypoint, not the ideal stop behind the wall.
        Assert.False(ZoneCombat.Within(player.MoveDestX, player.MoveDestY, mob.X, mob.Y, ZoneCombat.MeleeRange));
    }
}
