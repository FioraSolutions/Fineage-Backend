using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class TalkingIslandSpawnTests
{
    [Fact]
    public void Spawn_constants_match_cedric_training_hall()
    {
        Assert.Equal(TalkingIslandSpawn.X, TalkingIsland.SpawnX);
        Assert.Equal(TalkingIslandSpawn.Y, TalkingIsland.SpawnY);
        Assert.Equal(TalkingIslandSpawn.Z, TalkingIsland.SpawnZ);
        Assert.Equal(-72674, TalkingIsland.SpawnX);
        Assert.Equal(256819, TalkingIsland.SpawnY);
        Assert.Equal(-3112, TalkingIsland.SpawnZ);
    }

    [Fact]
    public void Gremlins_stay_at_cedrics_hall()
    {
        Assert.Equal(3, TalkingIsland.Gremlins.Length);
        foreach (var gremlin in TalkingIsland.Gremlins)
        {
            Assert.Equal(TalkingIsland.SpawnZ, gremlin.Z);
            Assert.Equal(TalkingIsland.GremlinNpcId, gremlin.NpcId);
            long dx = gremlin.X - (long)TalkingIsland.SpawnX;
            long dy = gremlin.Y - (long)TalkingIsland.SpawnY;
            Assert.True(dx * dx + dy * dy <= 400L * 400);
        }

        Assert.Equal(TalkingIsland.SpawnX + 150, TalkingIsland.Gremlins[0].X);
        Assert.Equal(TalkingIsland.SpawnY + 80, TalkingIsland.Gremlins[0].Y);
    }
}
