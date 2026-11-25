using Fineage.Protocol;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;
namespace Fineage.Zone.Tests;

/// <summary>
/// An unreachable target used to cost a full A* every approach tick. The soak in #971 measured it: 152k approach
/// attempts for 189 pawns, every one re-opening a search from the same origin, until P1 chased its own tail and the
/// node budget ran out for real work. These tests pin the two mechanisms that stop it — a negative route cache keyed on
/// origin, destination, instance and geometry version, and an approach backoff — plus the rule that neither may hide a
/// route that exists.
/// </summary>
public sealed class ZoneNoRouteCacheTests
{
    /// <summary>
    /// One region split end to end by a wall the walker cannot pass: every block in column 2 is solid on its east and
    /// west faces, so the halves have no route between them and MoveCheck clips any straight line at the wall.
    /// </summary>
    static GeoEngine Split()
    {
        var region = GeoEngine.Region.Flat1(96);
        for (int by = 0; by < 256; by++)
        {
            region.SetComplexBlock(2, by, static (_, _) =>
                (96, (byte)(GeoEngine.All & ~GeoEngine.East & ~GeoEngine.West)));
        }

        var geo = new GeoEngine();
        geo.SetRegion(20, 18, region);
        return geo;
    }

    static GeoEngine Open()
    {
        var geo = new GeoEngine();
        geo.SetRegion(20, 18, GeoEngine.Region.Flat1(96));
        return geo;
    }

    static ZoneEntity Walker(ZoneWorld world, int objectId, int x, int y) =>
        Actor(world, objectId, x, y, ZoneEntityKind.Npc);

    static ZoneEntity Actor(ZoneWorld world, int objectId, int x, int y, ZoneEntityKind kind)
    {
        var npc = new ZoneEntity
        {
            ObjectId = objectId,
            Kind = kind,
            NpcId = 30_000 + objectId,
            Name = "Walker" + objectId,
            X = x,
            Y = y,
            Z = 96,
            CurHp = 100,
            MaxHp = 100,
            HomeSet = true,
            HomeX = 0,
            HomeY = 0,
            HomeZ = 96,
        };
        world.PlaceForTests(npc);
        return npc;
    }

    /// <summary>West of the wall: 8 world units per cell, so 300 lands in the west half and 600 in the east half.</summary>
    const int WestX = 300, EastX = 600;

    const int MobId = 9_200_777;

    static SpawnCatalog Map(int px, int py, int pz, int mx, int my, int mz) =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["no_route_backoff"] = new("no_route_backoff", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 2, "Gremlin", mx, my, mz, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    [Fact]
    public void A_completed_search_that_finds_nothing_is_remembered_and_not_repeated()
    {
        var world = new ZoneWorld(geo: Split());
        var npc = Walker(world, 700, WestX, 40);

        world.RequestNpcGeoPathForTest(npc, EastX, 40, 96);
        world.RunStepForTest();

        // The search ran to completion and reported "no route", so the pair isnow a known fact.
        Assert.True(world.HasCachedNoRouteForTest(npc, WestX, 40, EastX, 40),
            "a NoRoute answer must be cached for the exact origin and destination");
        Assert.False(npc.PathfindPending, "the request must be resolved, not left pending");
        Assert.Empty(world.PathfindQueueForTest());
    }

    [Fact]
    public void A_repeat_request_for_the_same_unreachable_pair_spends_no_node_budget()
    {
        var world = new ZoneWorld(geo: Split());
        var npc = Walker(world, 701, WestX, 40);

        world.RequestNpcGeoPathForTest(npc, EastX, 40, 96);
        world.RunStepForTest();
        var (_, _, firstNodes, _) = world.PathfindDiagnostics();

        // The approach loop re-asks for the same pair every tick. Every one of those must be free now.
        for (int i = 0; i < 5; i++)
        {
            world.RequestNpcGeoPathForTest(npc, EastX, 40, 96);
            world.RunStepForTest();
        }

        var (queued, _, nodes, _) = world.PathfindDiagnostics();
        Assert.Equal(0, queued);
        Assert.Equal(0, nodes);
        Assert.True(firstNodes > 0, "the first search must have done real work, or the test proves nothing");
    }

    [Fact]
    public void A_pair_with_a_route_is_never_cached_as_unreachable()
    {
        var world = new ZoneWorld(geo: Open());
        var npc = Walker(world, 702, WestX, 40);

        world.RequestNpcGeoPathForTest(npc, EastX, 40, 96);
        world.RunStepForTest();

        Assert.False(world.HasCachedNoRouteForTest(npc, WestX, 40, EastX, 40));
        // And the reachable route is still served: the negative cache must not shadow the positive one.
        Assert.True(npc.MoveActive || npc.MovePath.Count > 0 || npc.X != WestX);
    }

    [Fact]
    public void An_unfinished_search_is_never_cached()
    {
        var world = new ZoneWorld(geo: Split());
        var npc = Walker(world, 703, WestX, 40);

        // Queued but not stepped: the search has not concluded anything yet.
        world.RequestNpcGeoPathForTest(npc, EastX, 40, 96);
        Assert.True(npc.PathfindPending);
        Assert.False(world.HasCachedNoRouteForTest(npc, WestX, 40, EastX, 40),
            "a queued request is not an answer");

        world.RunStepForTest();
        Assert.True(world.HasCachedNoRouteForTest(npc, WestX, 40, EastX, 40));
    }

    [Fact]
    public void A_cached_no_route_answer_survives_its_ttl_then_is_searched_again()
    {
        var world = new ZoneWorld(geo: Split());
        var npc = Walker(world, 704, WestX, 40);

        world.RequestNpcGeoPathForTest(npc, EastX, 40, 96);
        world.RunStepForTest();
        Assert.True(world.HasCachedNoRouteForTest(npc, WestX, 40, EastX, 40),
            "a NoRoute answer must be cached for the exact origin and destination");

        // The cached fact is stamped with the world clock, and the TTL is measured from it.
        long? stamp = world.NoRouteCachedStampForTest(npc, WestX, 40, EastX, 40);
        Assert.NotNull(stamp);
        Assert.True(world.HasCachedNoRouteForTest(npc, WestX, 40, EastX, 40, stamp.Value + 1),
            "an answer inside its TTL must still be trusted");
        // Past the TTL it is not: an expired entry is dropped so the next request searches again.
        Assert.False(world.HasCachedNoRouteForTest(npc, WestX, 40, EastX, 40, stamp.Value + ZoneWorld.NpcNoRouteCacheTtlMs + 1),
            "an answer older than the TTL must not be trusted: a blocker may have moved");
        Assert.Null(world.NoRouteCachedStampForTest(npc, WestX, 40, EastX, 40));
    }

    [Fact]
    public void A_geometry_change_invalidates_the_cached_answer()
    {
        var geo = Split();
        var world = new ZoneWorld(geo: geo);
        var npc = Walker(world, 705, WestX, 40);

        world.RequestNpcGeoPathForTest(npc, EastX, 40, 96);
        world.RunStepForTest();
        Assert.True(world.HasCachedNoRouteForTest(npc, WestX, 40, EastX, 40));

        int before = geo.GeometryVersion;
        geo.SetRegion(20, 18, GeoEngine.Region.Flat1(96));
        Assert.NotEqual(before, geo.GeometryVersion);

        // The key carries the geometry version, so the old answer cannot shadow the now-open world.
        Assert.False(world.HasCachedNoRouteForTest(npc, WestX, 40, EastX, 40));

        var from = Walker(world, 706, WestX, 44);
        world.RequestNpcGeoPathForTest(from, EastX, 44, 96);
        world.RunStepForTest();
        Assert.True(from.MoveActive || from.MovePath.Count > 0,
            "after the wall is gone the route must be found, not answered from a stale negative entry");
    }

    [Fact]
    public void Approach_backoff_doubles_and_is_capped()
    {
        Assert.Equal(ZoneCombat.ApproachRetryMinMs, ZoneWorld.ApproachRetryDelayMs(1));
        Assert.Equal(ZoneCombat.ApproachRetryMinMs * 2, ZoneWorld.ApproachRetryDelayMs(2));
        Assert.Equal(ZoneCombat.ApproachRetryMinMs * 4, ZoneWorld.ApproachRetryDelayMs(3));
        Assert.Equal(ZoneCombat.ApproachRetryMaxMs, ZoneWorld.ApproachRetryDelayMs(30));
        Assert.True(ZoneWorld.ApproachRetryDelayMs(1_000_000) <= ZoneCombat.ApproachRetryMaxMs,
            "a long failure run must not overflow the interval");
    }

    [Fact]
    public void Approach_retry_is_due_again_after_the_wait_but_not_before()
    {
        var world = new ZoneWorld(geo: Split());
        var player = Actor(world, 707, WestX, 40, ZoneEntityKind.Player);
        var target = Walker(world, 708, EastX, 40);

        player.Intent.ApproachTargetId = target.ObjectId;
        player.Intent.ApproachTargetX = target.X;
        player.Intent.ApproachTargetY = target.Y;
        player.Intent.ApproachAttempts = 3;
        long now = 1_000_000;
        player.Intent.ApproachRetryAtUnixMs = now + 1_000;

        Assert.False(ZoneWorld.ApproachRetryDue(player, target, now));
        Assert.True(ZoneWorld.ApproachRetryDue(player, target, now + 1_000));
    }

    [Fact]
    public void A_moved_target_clears_the_approach_backoff()
    {
        var world = new ZoneWorld(geo: Split());
        var player = Walker(world, 709, WestX, 40);
        var target = Walker(world, 710, EastX, 40);

        player.Intent.ApproachTargetId = target.ObjectId;
        player.Intent.ApproachTargetX = target.X;
        player.Intent.ApproachTargetY = target.Y;
        player.Intent.ApproachRetryAtUnixMs = 10_000_000;

        Assert.False(ZoneWorld.ApproachRetryDue(player, target, 1_000));

        // A target that stepped further than the repath tolerance is a new question for the pathfinder.
        target.X += ZoneCombat.ApproachRepathDistance * 2;
        Assert.True(ZoneWorld.ApproachRetryDue(player, target, 1_000));
    }

    [Fact]
    public void Cleared_intent_drops_the_backoff_so_the_next_attack_starts_fresh()
    {
        var intent = new PlayerIntent
        {
            ApproachTargetId = 5,
            ApproachAttempts = 4,
            ApproachRetryAtUnixMs = 9_000,
        };
        intent.ClearApproach();

        Assert.Equal(0, intent.ApproachAttempts);
        Assert.Equal(0, intent.ApproachRetryAtUnixMs);
        Assert.Equal(0, intent.ApproachTargetId);
    }

    [Fact]
    public void Every_approach_path_waits_out_the_backoff_not_only_the_auto_attack_tick()
    {
        var world = new ZoneWorld(geo: Split());
        var player = Actor(world, 711, WestX, 40, ZoneEntityKind.Player);
        var target = Walker(world, 712, EastX, 40);

        // The soak's flood came through the action path (RequestAttack → TryAttack → ApproachTarget), not only through
        // TickAutoAttack. The backoff has to live in ApproachTarget itself, or those callers re-run the search anyway.
        player.Intent.ApproachTargetId = target.ObjectId;
        player.Intent.ApproachTargetX = target.X;
        player.Intent.ApproachTargetY = target.Y;
        player.Intent.ApproachAttempts = 2;
        player.Intent.ApproachRetryAtUnixMs = long.MaxValue;

        long ms = world.ApproachTargetForTest(player, target, ZoneCombat.MeleeRange);

        Assert.True(ms > 0, "the call reports the remaining wait instead of returning a failure");
        Assert.False(player.PathfindPending, "a backed-off approach must not queue a search");
        Assert.Empty(world.PathfindQueueForTest());
    }

    [Fact]
    public async Task Repeated_client_attack_requests_do_not_restart_the_search_inside_the_backoff_window()
    {
        // The real path: a client RequestAttack reaches TryAttack, which calls StartTimedMove -> RequestNpcGeoPath. The
        // soak's flood came through exactly this route, so the test drives it too instead of calling ApproachTarget.
        // Standing inside the solid wall block, which is where a pawn ends up after its first clipped step: the very
        // first MoveCheck step is refused, so it does not creep a cell and the attempt is a genuine failure (a pawn
        // that does move one leg counts as progress and resets, which is correct).
        int px = WestX, py = 40, pz = 96;
        int mx = 2000, my = 40, mz = 96;
        var geo = Split();
        var kit = new NetTestKit(Map(px, py, pz, mx, my, mz), "no_route_backoff", geo: geo);
        var who = kit.Create(classId: 0, race: 0, x: px, y: py, z: pz);
        var (session, player) = await kit.EnterAsync(who);
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));

        Assert.Null(await kit.World.TryCharacterActionAsync(session, ActionProtocol.Attack, mob.ObjectId, default));
        Assert.Equal(1, player.Intent.ApproachAttempts);
        bool pendingAfterFirst = player.PathfindPending;

        // The client keeps the button down: the next RequestAttack arrives one tick later, inside the 250 ms window.
        // It must not re-open the search, and it must not count as progress.
        kit.AdvanceToTickCovering(kit.Now.ToUnixTimeMilliseconds() + ZoneSimulationSettings.StepMilliseconds);
        Assert.Null(await kit.World.TryCharacterActionAsync(session, ActionProtocol.Attack, mob.ObjectId, default));

        Assert.Equal(1, player.Intent.ApproachAttempts);
        Assert.Equal(pendingAfterFirst, player.PathfindPending);

        // Once the wait expires the pawn asks again for real, so a target that became reachable is never hidden by the
        // backoff: the counter moves on and the request is queued again.
        kit.AdvanceToTickCovering(player.Intent.ApproachRetryAtUnixMs + ZoneSimulationSettings.StepMilliseconds);
        Assert.Null(await kit.World.TryCharacterActionAsync(session, ActionProtocol.Attack, mob.ObjectId, default));
        Assert.Equal(2, player.Intent.ApproachAttempts);
    }

    [Fact]
    public void Ordinary_chase_and_return_are_not_held_back_by_the_backoff()
    {
        var world = new ZoneWorld(geo: Open());
        var player = Actor(world, 713, WestX, 40, ZoneEntityKind.Player);

        // Chase: the pawn already ran into a failed approach on this target, but the target moved — that is a new
        // question, and a reachable chase must start immediately.
        var chased = Walker(world, 714, WestX + ZoneCombat.ApproachRepathDistance * 4, 40);
        player.Intent.ApproachTargetId = chased.ObjectId;
        player.Intent.ApproachTargetX = chased.X - ZoneCombat.ApproachRepathDistance * 4;
        player.Intent.ApproachTargetY = chased.Y;
        player.Intent.ApproachAttempts = 5;
        player.Intent.ApproachRetryAtUnixMs = 1_000_000;
        Assert.True(ZoneWorld.ApproachRetryDue(player, chased, 1_000),
            "a chase whose target moved is due now, not after the failure wait");

        // Return home: a different intent (target id 0) is never governed by the previous approach's backoff.
        var home = Walker(world, 715, WestX + 200, 40);
        Assert.True(ZoneWorld.ApproachRetryDue(player, home, 1_000),
            "an approach to a new target must not inherit the old target's backoff");

        // A first-ever approach has no backoff at all.
        var fresh = Actor(world, 716, WestX, 40, ZoneEntityKind.Player);
        Assert.True(ZoneWorld.ApproachRetryDue(fresh, home, 1_000));
    }

    [Fact]
    public async Task A_target_that_moves_out_from_behind_the_wall_is_searched_again()
    {
        // Standing inside the wall block, an approach fails and starts a backoff. The target then steps west, out of the
        // blocked geometry, and the same player must be allowed to search again immediately rather than wait out the
        // failure window — otherwise a reachable target is hidden behind a stale negative answer.
        int px = WestX, py = 40, pz = 96;
        var geo = Split();
        var kit = new NetTestKit(Map(px, py, pz, EastX, 40, 96), "no_route_backoff", geo: geo);
        var who = kit.Create(classId: 0, race: 0, x: px, y: py, z: pz);
        var (session, player) = await kit.EnterAsync(who);
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));

        Assert.Null(await kit.World.TryCharacterActionAsync(session, ActionProtocol.Attack, mob.ObjectId, default));
        Assert.Equal(1, player.Intent.ApproachAttempts);
        Assert.True(!ZoneWorld.ApproachRetryDue(player, mob, kit.Now.ToUnixTimeMilliseconds()),
            "the failed attempt holds the pawn for the backoff window");

        // The mob walks past the wall to the west side, so the route becomes a plain open-ground run.
        mob.X = WestX + 900;
        Assert.True(ZoneWorld.ApproachRetryDue(player, mob, kit.Now.ToUnixTimeMilliseconds()),
            "a target that displaced further than the repath tolerance clears the backoff");

        Assert.Null(await kit.World.TryCharacterActionAsync(session, ActionProtocol.Attack, mob.ObjectId, default));
        Assert.True(player.MoveActive || player.MovePath.Count > 0 || player.PathfindPending,
            "the pawn starts closing on the now reachable target instead of staying parked");
    }

    [Fact]
    public void Unserved_wait_is_recorded_on_its_own_instrument_not_the_served_one()
    {
        var text = MetricsText.Prometheus(new MetricsSnapshot
        {
            PathfindUnservedWait = [new StageMetrics { Stage = "p3", Duration = new HistogramMetrics() }],
            PathfindStarvedSteps = new Dictionary<string, long>(StringComparer.Ordinal) { ["p1"] = 7 },
        }, 0, 0);

        // The bug this pins: AppendLabeledCounter had help and labelName swapped, so the exposition emitted the help
        // text as the label name and no Prometheus selector for priority could ever match.
        Assert.Contains("fineage_npc_pathfind_starved_steps_total{priority=\"p1\"} 7", text);
        Assert.DoesNotContain("starved_steps_total{Steps where", text);
        Assert.Contains("fineage_npc_pathfind_unserved_wait_ms", text);
    }

    [Fact]
    public void No_route_counters_are_exposed_with_their_names()
    {
        var text = MetricsText.Prometheus(new MetricsSnapshot
        {
            PathfindNoRoute = 3,
            PathfindNoRouteCached = 11,
        }, 0, 0);

        Assert.Contains("fineage_npc_pathfind_no_route_total 3", text);
        Assert.Contains("fineage_npc_pathfind_no_route_cached_total 11", text);
    }
}
