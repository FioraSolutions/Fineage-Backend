using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Buff / heal / debuff / spoil land at hitTime (Mobius onMagicHitTimer) and share the abort / break rules.</summary>
public sealed class DeferredEffectTests
{
    static Task<string?> Cast(NetTestKit kit, ZoneSession s, int skill, int target = 0) =>
        kit.World.TrySkillUseAsync(s, skill, target, CancellationToken.None);

    [Fact]
    public async Task Heal_raises_hp_only_when_the_cast_completes()
    {
        var kit = new NetTestKit();
        var (s, p) = await kit.EnterAsync(classId: 25);
        p.KnownSkills[1011] = 1;
        p.CurMp = 80;
        p.CurHp = 10;
        Assert.Null(await Cast(kit, s, 1011));
        Assert.Equal(10, p.CurHp);
        Assert.NotNull(p.PendingHit);
        await kit.World.CompleteCastForTestAsync(s);
        Assert.True(p.CurHp > 10);
    }

    [Fact]
    public async Task Magic_buff_is_lost_when_a_hit_breaks_the_cast()
    {
        var kit = new NetTestKit();
        var (s, p) = await kit.EnterAsync(classId: 25);
        p.KnownSkills[1040] = 1; // Shield, isMagic 1
        p.CurHp = p.MaxHp = 100_000;
        bool broken = false;
        for (int i = 0; i < 20 && !broken; i++)
        {
            p.CurMp = 500;
            kit.Now = kit.Now.AddSeconds(10);
            Assert.Null(await Cast(kit, s, 1040));
            kit.World.HitPlayerForTest(p, 100_000); // 99 % break
            broken = p.PendingHit == null;
            await kit.World.CompleteCastForTestAsync(s);
            if (broken)
                Assert.DoesNotContain(p.Abnormals, a => a.Id == 1040);
            else
                p.Abnormals.Clear();
        }

        Assert.True(broken);
    }

    [Fact]
    public async Task Debuff_on_a_target_that_escaped_is_aborted_and_spoil_ignores_hits()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var (s, p) = await kit.EnterAsync(classId: 25, x: mob.X + 100, y: mob.Y, z: mob.Z);
        p.KnownSkills[1164] = 1; // Curse: Weakness
        p.CurMp = 500;
        Assert.Null(await Cast(kit, s, 1164, mob.ObjectId));
        mob.X += 5000;
        await kit.World.CompleteCastForTestAsync(s);
        Assert.DoesNotContain(mob.Abnormals, a => a.Id == 1164);
        mob.X -= 5000;

        // Spoil is a physical skill (no isMagic): a hit never breaks it, the spoil flag lands at hitTime.
        p.KnownSkills[254] = 11;
        mob.Level = 1;
        kit.Now = kit.Now.AddSeconds(30);
        Assert.Null(await Cast(kit, s, 254, mob.ObjectId));
        Assert.Equal(0, mob.SpoilerObjectId);
        p.CurHp = p.MaxHp = 100_000;
        kit.World.HitPlayerForTest(p, 100_000);
        Assert.NotNull(p.PendingHit);
        await kit.World.CompleteCastForTestAsync(s);
        Assert.Equal(p.ObjectId, mob.SpoilerObjectId);
    }
}
