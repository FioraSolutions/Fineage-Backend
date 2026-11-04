using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Game;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneCombatFormulaTests
{
    [Fact]
    public void AttackTimeFollowsMobiusPspd()
    {
        Assert.Equal(1666, ZoneCombat.AttackTimeMs(300));
        Assert.Equal(1976, ZoneCombat.AttackTimeMs(253));
        Assert.Equal(833, ZoneCombat.HitTimeMs(300));
        Assert.Equal(100, ZoneCombat.AttackTimeMs(100_000));
    }

    [Fact]
    public void PhysicalDamageIs70PAtkOverPDef()
    {
        Assert.Equal(6, ZoneCombat.PhysicalDamage(4, 44.444));
        Assert.Equal(7, ZoneCombat.PhysicalDamage(8.475, 80));
        Assert.Equal(1, ZoneCombat.PhysicalDamage(0.1, 1000));
        Assert.Equal(ZoneCombat.SampleMeleeDamage, ZoneCombat.PhysicalDamage(ZoneCombat.DefaultPlayerPAtk, ZoneCombat.GremlinPDef));
    }

    [Fact]
    public void GremlinTemplateCarriesMobiusAttackStats()
    {
        var dir = GameData.FindDataDirectory(AppContext.BaseDirectory)!;
        var gremlin = NpcCatalog.Load(dir).Get(20001);
        Assert.Equal(8.475, gremlin.PAtk, 3);
        Assert.Equal(44.444, gremlin.PDef, 3);
        Assert.Equal(253, gremlin.AttackSpeed);
        Assert.Equal(40, gremlin.AttackRange);
    }
}
