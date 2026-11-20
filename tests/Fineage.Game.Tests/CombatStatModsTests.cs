using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class CombatStatModsTests
{
    [Fact]
    public void Apply_is_set_then_mul_then_add()
    {
        var mods = new CombatStatMods();
        mods.Add(new SkillStatFunc(CombatStat.PAtk, '=', [100]), 1);
        mods.Add(new SkillStatFunc(CombatStat.PAtk, 'm', [1.5]), 1);
        mods.Add(new SkillStatFunc(CombatStat.PAtk, 'a', [10]), 1);
        Assert.Equal(160, mods.Apply(CombatStat.PAtk, 1)); // (100 * 1.5) + 10
    }

    [Fact]
    public void Catalog_collects_abnormal_sources()
    {
        var cat = SkillStatEffectCatalog.LoadDefault();
        Assert.True(cat.TryGet(1085, out _)); // Acumen
        var mods = cat.Collect([new SkillStatSource(1085, SkillLevel: 3, AbnormalValue: 3)]);
        Assert.False(ReferenceEquals(mods, CombatStatMods.None));
        Assert.True(mods.Apply(CombatStat.MAtkSpd, 100) > 100);
    }

    [Fact]
    public void Spellcraft_halves_mAtkSpd_only_when_not_wearing_magic()
    {
        var cat = SkillStatEffectCatalog.LoadPassives();
        var known = new Dictionary<int, int> { [163] = 1 };
        // Mobius: <not><using kind="MAGIC"/></not> — penalty applies off robes, not on them.
        var robes = cat.CollectPassives(known, wearing: _ => true);
        Assert.Equal(400, robes.Apply(CombatStat.MAtkSpd, 400));
        var other = cat.CollectPassives(known, wearing: _ => false);
        Assert.Equal(200, other.Apply(CombatStat.MAtkSpd, 400));
    }

    [Fact]
    public void Magicians_movement_slows_pAtkSpd_only_when_not_wearing_magic()
    {
        var cat = SkillStatEffectCatalog.LoadPassives();
        var known = new Dictionary<int, int> { [118] = 1 };
        var robes = cat.CollectPassives(known, wearing: _ => true);
        Assert.Equal(300, robes.Apply(CombatStat.PAtkSpd, 300));
        var other = cat.CollectPassives(known, wearing: _ => false);
        Assert.Equal(240, other.Apply(CombatStat.PAtkSpd, 300));
    }
}
