using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius Think/Movement task managers: idle entities stay off the combat tick registries.
/// </summary>
public sealed class ZoneActiveSetsTests
{
    [Fact]
    public async Task Idle_world_combat_tick_keeps_active_sets_empty()
    {
        var kit = new WorldBench();
        // Spawned NPCs from the default map sit idle with empty registries.
        var before = kit.World.ActiveSetCounts;
        Assert.Equal(0, before.Moving);
        Assert.Equal(0, before.AiBusy);
        Assert.Equal(0, before.PendingHit);
        Assert.Equal(0, before.Decaying);

        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);

        var after = kit.World.ActiveSetCounts;
        Assert.Equal(0, after.Moving);
        Assert.Equal(0, after.AiBusy);
        Assert.Equal(0, after.PendingHit);
        Assert.Equal(0, after.Decaying);
    }

    [Fact]
    public async Task Moving_npc_enters_and_leaves_moving_registry()
    {
        var kit = new NetTestKit();
        Assert.True(kit.World.TryGetEntityById(50001, out var npc));
        long t0 = kit.Now.ToUnixTimeMilliseconds();
        kit.World.SetMoveActive(npc!, true);
        npc!.MoveDestX = npc.X + 400;
        npc.MoveDestY = npc.Y;
        npc.MoveDestZ = npc.Z;
        npc.MoveLastTickMs = t0;
        Assert.Equal(1, kit.World.ActiveSetCounts.Moving);

        // Far enough time that AdvanceMove finishes the short step and clears MoveActive.
        kit.Now = kit.Now.AddSeconds(30);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);

        Assert.False(npc.MoveActive);
        Assert.Equal(0, kit.World.ActiveSetCounts.Moving);
    }

    [Fact]
    public void Aggro_target_tracks_ai_busy_registry()
    {
        var kit = new NetTestKit();
        Assert.True(kit.World.TryGetEntityById(50001, out var npc));
        kit.World.SetAggroTarget(npc!, 42);
        Assert.Equal(1, kit.World.ActiveSetCounts.AiBusy);
        kit.World.SetAggroTarget(npc!, 0);
        Assert.Equal(0, kit.World.ActiveSetCounts.AiBusy);
    }
}
