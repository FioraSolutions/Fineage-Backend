using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Q409 <c>addSpawn</c> via quest <c>spawn_npc</c> ops.</summary>
public sealed class QuestSpawnTests
{
    [Fact]
    public async Task Replay_spawns_appear_near_anchor_and_hate_the_player()
    {
        var kit = new NetTestKit();
        var (session, elf) = await kit.EnterAsync(classId: 25);
        elf.X = 10_000;
        elf.Y = 20_000;
        elf.Z = -3000;

        int before = kit.World.CountNpcTemplateForTest(27032);
        kit.World.QuestSpawnNearForTest(elf, anchorNpcId: 30424, elf.X, elf.Y, elf.Z, [27032, 27033, 27034]);
        Assert.Equal(before + 1, kit.World.CountNpcTemplateForTest(27032));
        Assert.Equal(1, kit.World.CountNpcTemplateForTest(27033));
        Assert.Equal(1, kit.World.CountNpcTemplateForTest(27034));

        var lizard = kit.World.FindNpcTemplateForTest(27032);
        Assert.NotNull(lizard);
        Assert.True(lizard!.Attackable);
        Assert.Equal(elf.ObjectId, lizard.AggroTargetId);
        Assert.InRange(Math.Abs(lizard.X - elf.X) + Math.Abs(lizard.Y - elf.Y), 1, 200);
    }
}
