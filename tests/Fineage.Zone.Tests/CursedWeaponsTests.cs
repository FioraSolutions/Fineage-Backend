using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.CursedWeapons;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class CursedWeaponsTests
{
    [Fact]
    public void Catalog_loads_zariche_and_akamanah()
    {
        var catalog = CursedWeaponsCatalog.LoadDefault();
        Assert.True(catalog.TryGet(OlympiadProtocol.Zariche, out var zariche));
        Assert.Equal(3603, zariche.SkillId);
        Assert.True(catalog.TryGet(OlympiadProtocol.Akamanah, out var akamanah));
        Assert.Equal(3629, akamanah.SkillId);
    }

    [Fact]
    public async Task Activate_sets_karma_skill_and_fanout()
    {
        var bench = new WorldBench();
        var kit = await bench.EnterAsync();
        Assert.Null(await kit.World.TryGiveItemAsync(kit.Session, OlympiadProtocol.Zariche, 1, default));
        kit.World.RunStepForTest(() => kit.World.ActivateCursedWeaponForTest(kit.Player, OlympiadProtocol.Zariche));

        Assert.Equal(CursedWeaponRuntime.KarmaWhileWielding, kit.Player.Karma);
        Assert.True(kit.Player.CursedSkills.ContainsKey(3603));
        Assert.NotEmpty(bench.Rec.Of("FanoutCursedWeaponList"));
        Assert.NotEmpty(bench.Rec.Of("FanoutCursedWeaponLocation"));
    }

    [Fact]
    public async Task Wields_blade_refuses_low_level_attacks()
    {
        var bench = new WorldBench();
        var kit = await bench.EnterAsync(level: 25);
        Assert.Null(await kit.World.TryGiveItemAsync(kit.Session, OlympiadProtocol.Zariche, 1, default));
        kit.World.RunStepForTest(() => kit.World.ActivateCursedWeaponForTest(kit.Player, OlympiadProtocol.Zariche));

        Assert.True(kit.World.WieldsCursedWeaponForTest(kit.Player));
        Assert.True(PlayableAttackRules.Refused(
            bothPlayers: true,
            attackerLevel: 25,
            targetLevel: 10,
            attackerKarma: kit.Player.Karma,
            targetKarma: 0,
            attackerBlessed: false,
            targetBlessed: false,
            attackerCursed: true,
            targetCursed: false,
            badSkill: false));
    }
}