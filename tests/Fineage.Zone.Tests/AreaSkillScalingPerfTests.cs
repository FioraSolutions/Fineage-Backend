using System.Diagnostics;
using Fineage.Zone.Interest;
using Fineage.Zone.World;
using Xunit;
using Xunit.Abstractions;

namespace Fineage.Zone.Tests;

/// <summary>
/// The area-skill target resolver must cost what the caster's neighbourhood costs, not what the world costs.
/// </summary>
/// <remarks>
/// <c>HostileAffectTargets</c> used to walk <c>_byId.Values</c> (the whole 34k-NPC world) and then drop every
/// entity outside the caster's AOI — measured as <c>commands.apply</c> p99 19.0 ms in
/// <c>verify-fullworld-aoe</c>. Mobius (<c>handlers/targethandlers/Aura.java</c>, <c>Area.java</c>) reads
/// <c>World.getVisibleObjectsInRange</c>, i.e. the caster's surrounding <c>WorldRegion</c>s. These tests hold
/// the equivalence (same ids, same order) and the scaling (cost flat as the rest of the world grows).
/// </remarks>
[Collection("Zone simulation perf")]
[Trait("Category", "perf")]
public sealed class AreaSkillScalingPerfTests(ITestOutputHelper output)
{
    const int Whirlwind = 36;

    [Fact]
    public void Aura_target_cost_does_not_grow_with_the_rest_of_the_world()
    {
        // Same caster, same neighbourhood, same targets: only the number of untouched NPCs elsewhere changes.
        double small = MeasurePerResolve(farNpcs: 0);
        double large = MeasurePerResolve(farNpcs: 20_000);

        output.WriteLine($"aura resolve: empty world {small:F4} ms, +20k far NPCs {large:F4} ms");

        // A whole-world walk is ~20k dictionary probes per resolve; the AOI lookup stays near the small-world
        // cost. A generous factor keeps this a scaling guard rather than a machine benchmark.
        Assert.True(large < Math.Max(0.05, small * 8),
            $"aura resolve grew with the world: {small:F4} ms -> {large:F4} ms with 20k extra NPCs");
    }

    [Fact]
    public void Aura_targets_are_the_same_ids_in_ascending_order_whatever_the_world_size()
    {
        List<int> idsSmall = TargetIds(farNpcs: 0);
        List<int> idsLarge = TargetIds(farNpcs: 5_000);

        Assert.Equal([900_101, 900_102, 900_103], idsSmall);
        Assert.Equal(idsSmall, idsLarge);
    }

    static List<int> TargetIds(int farNpcs)
    {
        NetTestKit kit = Build(farNpcs, out ZoneEntity player);
        return kit.World.HostileAffectTargetIdsForTest(player, null, Whirlwind);
    }

    double MeasurePerResolve(int farNpcs)
    {
        NetTestKit kit = Build(farNpcs, out ZoneEntity player);

        // Warm the JIT and the thread-static candidate list before timing.
        for (int i = 0; i < 50; i++)
            kit.World.HostileAffectTargetIdsForTest(player, null, Whirlwind);

        const int iterations = 2_000;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            kit.World.HostileAffectTargetIdsForTest(player, null, Whirlwind);
        watch.Stop();

        return watch.Elapsed.TotalMilliseconds / iterations;
    }

    static NetTestKit Build(int farNpcs, out ZoneEntity player)
    {
        var kit = new NetTestKit(
            Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting", aoi: new GridAoi(cellSize: 1_000));
        (_, player) = kit.EnterAsync(classId: 25, x: 0, y: 0, z: 0).GetAwaiter().GetResult();

        // Three targets inside the caster's AOI, at 5/10/15 units.
        Npc(kit, 900_103, 15, 0);
        Npc(kit, 900_101, 5, 0);
        Npc(kit, 900_102, 10, 0);

        // Far away: outside the AOI, outside the affect range, and progressively numerous.
        for (int i = 0; i < farNpcs; i++)
            Npc(kit, 950_000 + i, 40_000 + i * 10, 40_000);

        return kit;
    }

    static ZoneEntity Npc(NetTestKit kit, int objectId, int x, int y)
    {
        var npc = new ZoneEntity
        {
            ObjectId = objectId,
            Kind = ZoneEntityKind.Npc,
            NpcId = 20001,
            NpcType = "Monster",
            Attackable = true,
            Level = 1,
            X = x,
            Y = y,
            Z = 0,
            MaxHp = 500,
            CurHp = 500
        };
        kit.World.PlaceForTests(npc);
        kit.World.EnterAoiForTests(npc.ObjectId, x, y);
        return npc;
    }
}
