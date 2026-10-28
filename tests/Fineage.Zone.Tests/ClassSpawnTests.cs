using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ClassSpawnTests
{
    [Fact]
    public void Fighter_and_mystic_points_match_the_checked_creation_set()
    {
        Assert.Equal(new ClassSpawn.Point(-71338, 258271, -3104), ClassSpawn.HumanFighterPoints[0]);
        Assert.Equal(4, ClassSpawn.HumanFighterPoints.Length);
        Assert.Equal(new ClassSpawn.Point(-90875, 248162, -3570), ClassSpawn.Preview(ClassSpawn.HumanMystic));
        Assert.Equal(4, ClassSpawn.HumanMysticPoints.Length);
        Assert.Equal(ClassSpawn.HumanFighterPoints[0], ClassSpawn.Preview(ClassSpawn.HumanFighter));
    }

    [Fact]
    public void Pick_stays_inside_the_class_set()
    {
        var random = new Random(1);
        for (int i = 0; i < 20; i++)
        {
            var fighter = ClassSpawn.Pick(ClassSpawn.HumanFighter, random);
            var mystic = ClassSpawn.Pick(ClassSpawn.HumanMystic, random);
            Assert.Contains(fighter, ClassSpawn.HumanFighterPoints);
            Assert.Contains(mystic, ClassSpawn.HumanMysticPoints);
        }
    }
}
