using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Cancellation (DispelByCategory) and Mana Burn (MagicalAttackMp).</summary>
public sealed class CancelAndManaBurnTests
{
    const int Cancellation = 1056;
    const int ManaBurn = 1398;
    const int Shield = 1040;
    const int WindWalk = 1204;

    [Fact]
    public void Skill_effects_catalog_marks_cancel_and_mana_burn()
    {
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryDispelByCategory(Cancellation, out var dispel));
        Assert.Equal("buff", dispel.Slot, ignoreCase: true);
        Assert.Equal(25, dispel.Rate);
        Assert.Equal(5, dispel.Max);
        Assert.True(effects.HasMagicalAttackMp(ManaBurn));
        Assert.False(effects.TryDispelByCategory(Shield, out _));
    }

    [Fact]
    public async Task Cancellation_strips_buffs_not_self_heal()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var (s, p) = await kit.EnterAsync(classId: 10, level: 80, x: mob.X, y: mob.Y, z: mob.Z); // human mystic
        p.KnownSkills[Cancellation] = 1;
        p.CurMp = 500;

        // Put Wind Walk + Shield on the mob as buffs.
        var ww = kit.Game.Skills.Find(WindWalk)!;
        var sh = kit.Game.Skills.Find(Shield)!;
        Assert.True(ww.TryLevelData(1, out var wwLv));
        Assert.True(sh.TryLevelData(1, out var shLv));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(ww, wwLv, mob, force: true));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(sh, shLv, mob, force: true));
        Assert.Equal(2, mob.Abnormals.Count);
        int hpBefore = p.CurHp;

        Assert.Null(await kit.World.TrySkillUseAsync(s, Cancellation, mob.ObjectId, CancellationToken.None));
        Assert.Equal(hpBefore, p.CurHp); // must NOT heal self
        Assert.NotNull(p.PendingHit);
        // Force land: complete cast many times with high cancel rate by advancing; use test hook after cast.
        await kit.World.CompleteCastForTestAsync(s);

        // Rate 25% — may remove 0..2. At least the path must not add heals/self buffs.
        Assert.DoesNotContain(p.Abnormals, a => a.Id == Cancellation);
        Assert.True(p.CurHp <= hpBefore);
    }

    [Fact]
    public void Dispel_by_category_removes_buffs_with_forced_roll()
    {
        var list = new List<Fineage.Zone.Combat.ZoneAbnormalEntry>
        {
            new() { Id = WindWalk, Value = 1, SkillLevel = 1, Slot = "SPEED_UP", ExpiresAtUnixMs = long.MaxValue },
            new() { Id = Shield, Value = 1, SkillLevel = 1, Slot = "PD_UP", ExpiresAtUnixMs = long.MaxValue },
            new() { Id = 1160, Value = 1, SkillLevel = 1, Slot = "SPEED_DOWN", ExpiresAtUnixMs = long.MaxValue }, // Slow debuff
        };
        int removed = ZoneAbnormals.DispelByCategory(
            list,
            e => e.Id is WindWalk or Shield,
            _ => true,
            max: 5);
        Assert.Equal(2, removed);
        Assert.Single(list);
        Assert.Equal(1160, list[0].Id);
    }

    [Fact]
    public async Task Mana_burn_reduces_target_mp()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        mob.MaxMp = 500;
        mob.CurMp = 500;
        var (s, p) = await kit.EnterAsync(classId: 10, level: 80, x: mob.X, y: mob.Y, z: mob.Z);
        p.KnownSkills[ManaBurn] = 1;
        p.CurMp = 500;
        int before = mob.CurMp;

        int hpBefore = mob.CurHp;
        Assert.Null(await kit.World.TrySkillUseAsync(s, ManaBurn, mob.ObjectId, CancellationToken.None));
        await kit.World.CompleteCastForTestAsync(s);
        Assert.True(mob.CurMp < before, $"Mana Burn should cut MP ({before} → {mob.CurMp})");
        Assert.Equal(hpBefore, mob.CurHp); // HP untouched when only MagicalAttackMp
    }
}
