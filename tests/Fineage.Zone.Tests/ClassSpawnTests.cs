using Fineage.Login;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ClassSpawnTests
{
    [Fact]
    public void Fighter_and_mystic_points_match_the_checked_creation_set()
    {
        var fighter = ClassTemplates.Points(ClassSpawn.HumanFighter);
        var mystic = ClassTemplates.Points(ClassSpawn.HumanMystic);
        Assert.Equal(new ClassSpawn.Point(-71338, 258271, -3104), fighter[0]);
        Assert.Equal(4, fighter.Length);
        Assert.Equal(new ClassSpawn.Point(-90875, 248162, -3570), mystic[0]);
        Assert.Equal(4, mystic.Length);
        Assert.Equal(fighter[0], ClassTemplates.Preview(ClassSpawn.HumanFighter));
    }

    [Fact]
    public void Pick_stays_inside_the_class_set()
    {
        var random = new Random(1);
        var fighterPoints = ClassTemplates.Points(ClassSpawn.HumanFighter);
        var mysticPoints = ClassTemplates.Points(ClassSpawn.HumanMystic);
        for (int i = 0; i < 20; i++)
        {
            var fighter = ClassTemplates.Pick(ClassSpawn.HumanFighter, random);
            var mystic = ClassTemplates.Pick(ClassSpawn.HumanMystic, random);
            Assert.Contains(fighter, fighterPoints);
            Assert.Contains(mystic, mysticPoints);
        }
    }
}
