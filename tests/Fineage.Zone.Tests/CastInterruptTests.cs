using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius abortCast at launch (target gone / escaped), breakCast on hit, abort on teleport.</summary>
public sealed class CastInterruptTests
{
    static async Task<(NetTestKit Kit, ZoneSession S, World.ZoneEntity P, World.ZoneEntity Mob)> CastWindStrikeAsync()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var (s, p) = await kit.EnterAsync(classId: 25, x: mob.X + 100, y: mob.Y, z: mob.Z);
        p.KnownSkills[1177] = 1;
        p.CurMp = 80;
        Assert.Null(await kit.World.TrySkillUseAsync(s, 1177, mob.ObjectId, CancellationToken.None));
        Assert.NotNull(p.PendingHit);
        return (kit, s, p, mob);
    }

    [Fact]
    public async Task Target_out_of_effect_range_at_launch_aborts_without_damage_or_mp()
    {
        var (kit, _, p, mob) = await CastWindStrikeAsync();
        int hp = mob.CurHp, mp = p.CurMp;
        mob.X += 3000; // past effectRange 1100
        kit.World.ResolvePendingHitForTest(p);
        Assert.Equal(hp, mob.CurHp);
        Assert.Equal(mp, p.CurMp); // mpConsume is only spent on a real launch
        Assert.Null(p.PendingHit);
        Assert.Equal(0, p.CastingUntilMs);
    }

    [Fact]
    public async Task Target_within_effect_range_still_takes_the_hit()
    {
        var (kit, _, p, mob) = await CastWindStrikeAsync();
        int hp = mob.CurHp;
        mob.X += 400; // castRange 600 < distance < effectRange 1100: still lands
        kit.World.ResolvePendingHitForTest(p);
        Assert.True(mob.CurHp < hp);
    }

    [Fact]
    public async Task Heavy_hit_breaks_a_magic_cast()
    {
        var (kit, s, p, _) = await CastWindStrikeAsync();
        p.CurHp = p.MaxHp = 100_000;
        kit.World.HitPlayerForTest(p, 100_000); // rate clamps to 99 %
        for (int i = 0; i < 20 && p.PendingHit != null; i++)
        {
            await kit.World.TrySkillUseAsync(s, 1177, 0, CancellationToken.None);
            kit.World.HitPlayerForTest(p, 100_000);
        }
        Assert.Null(p.PendingHit);

        // Abort hook is idempotent once nothing is in flight.
        Assert.False(kit.World.AbortCastForTest(p));
    }
}
