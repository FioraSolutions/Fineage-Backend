using Fineage.Game.Skills;
using Fineage.Game.Services;
using Fineage.Zone;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius character_skills_save / characters.hp-mp-cp: logout persist and enter restore.</summary>
[Collection("Zone admin gate")]
public sealed class ZoneEffectPersistTests : IDisposable
{
    readonly string? _prevEnv;

    public ZoneEffectPersistTests()
    {
        _prevEnv = Environment.GetEnvironmentVariable(ZoneAdmin.EnvVar);
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");
        ZoneAdmin.SetNamesForTest(["KuK"]);
    }

    public void Dispose()
    {
        ZoneAdmin.SetNamesForTest(null);
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, _prevEnv);
    }

    [Fact]
    public async Task Mbuff_abnormals_restore_remaining_time_on_relog()
    {
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(account: "buffs", name: "KuK");
        Assert.Null(await me.BypassAsync(0, "admin_mbuff"));
        Assert.Contains(me.Player.Abnormals, a => a.Id == ZoneBuffs.SampleWindWalk);

        await kit.World.LeaveAsync(me.Session, CancellationToken.None);
        var saved = kit.Store.FindById("buffs", me.Session.ObjectId)!.Effects!;
        Assert.Contains(saved, e => e.SkillId == ZoneBuffs.SampleWindWalk && e.RemainingSeconds > 0);

        BenchPlayer again = await kit.EnterAsync(account: "buffs", name: "KuK");
        Assert.Contains(again.Player.Abnormals, a => a.Id == ZoneBuffs.SampleWindWalk);
        Assert.True(kit.World.TryBuildNetAbnormalStatus(again.Session, out var wire));
        Assert.Contains(wire.Effects, e => e.Id == ZoneBuffs.SampleWindWalk && e.DurationSeconds > 0);
    }

    [Fact]
    public async Task Relog_restores_hp_mp_cp_including_zero_hp()
    {
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(account: "vitals", name: "HeroV");
        Assert.True(me.Player.MaxHp > 1);
        Assert.True(me.Player.MaxMp > 1);
        Assert.True(me.Player.MaxCp > 0);
        me.Player.CurHp = Math.Max(1, me.Player.MaxHp / 3);
        me.Player.CurMp = Math.Max(1, me.Player.MaxMp / 4);
        me.Player.CurCp = Math.Max(1, me.Player.MaxCp / 2);
        int hp = me.Player.CurHp, mp = me.Player.CurMp, cp = me.Player.CurCp;

        await kit.World.LeaveAsync(me.Session, CancellationToken.None);
        var stored = kit.Store.FindById("vitals", me.Session.ObjectId)!;
        Assert.Equal(hp, stored.Hp);
        Assert.Equal(mp, stored.Mp);
        Assert.Equal(cp, stored.Cp);

        BenchPlayer again = await kit.EnterAsync(account: "vitals", name: "HeroV");
        Assert.Equal(hp, again.Player.CurHp);
        Assert.Equal(mp, again.Player.CurMp);
        Assert.Equal(cp, again.Player.CurCp);
        Assert.True(again.Player.CurHp < again.Player.MaxHp);

        again.Player.CurHp = 0;
        await kit.World.LeaveAsync(again.Session, CancellationToken.None);
        BenchPlayer dead = await kit.EnterAsync(account: "vitals", name: "HeroV");
        Assert.Equal(0, dead.Player.CurHp);
    }

    [Fact]
    public async Task Buff_remaining_time_does_not_tick_offline_but_reuse_systime_does()
    {
        long nowMs = 1_800_000_000_000;
        var kit = new WorldBench(clock: () => DateTimeOffset.FromUnixTimeMilliseconds(Volatile.Read(ref nowMs)));
        BenchPlayer me = await kit.EnterAsync(account: "time", name: "HeroT");
        long now = kit.Now().ToUnixTimeMilliseconds();
        Assert.True(ZoneAbnormals.Apply(me.Player.Abnormals, ZoneBuffs.SampleWindWalk, value: 2, durationSeconds: 1000, nowUnixMs: now, skillLevel: 2, slot: "SPEED_UP"));
        me.Player.SkillReuseUntilMs[ZoneBuffs.SampleWindWalk] = now + 60_000;

        await kit.World.LeaveAsync(me.Session, CancellationToken.None);
        var saved = kit.Store.FindById("time", me.Session.ObjectId)!.Effects!;
        Assert.Contains(saved, e => e.SkillId == ZoneBuffs.SampleWindWalk && e.IsEffect && e.RemainingSeconds >= 1000);
        Assert.Contains(saved, e => e.SkillId == ZoneBuffs.SampleWindWalk && e.SystimeMs == now + 60_000);

        Interlocked.Add(ref nowMs, 10_000);
        BenchPlayer again = await kit.EnterAsync(account: "time", name: "HeroT");
        var wind = Assert.Single(again.Player.Abnormals, a => a.Id == ZoneBuffs.SampleWindWalk);
        Assert.True(ZoneAbnormals.RemainingSeconds(kit.Now().ToUnixTimeMilliseconds(), wind) >= 1000,
            "buff remaining time is frozen while logged out");
        Assert.True(again.Player.SkillReuseUntilMs.TryGetValue(ZoneBuffs.SampleWindWalk, out long until));
        Assert.Equal(now + 60_000, until);
        Assert.Equal(50_000, until - kit.Now().ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Toggles_dances_and_herbs_are_not_restored()
    {
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(account: "skip", name: "HeroS");
        long now = kit.Now().ToUnixTimeMilliseconds();
        Assert.True(ZoneAbnormals.Apply(me.Player.Abnormals, 226, value: 1, durationSeconds: 3600, nowUnixMs: now, skillLevel: 1)); // Relax toggle
        Assert.True(ZoneAbnormals.Apply(me.Player.Abnormals, 268, value: 1, durationSeconds: 120, nowUnixMs: now, skillLevel: 1, slot: "SONG_OF_WIND"));
        Assert.True(ZoneAbnormals.Apply(me.Player.Abnormals, 2280, value: 3, durationSeconds: 300, nowUnixMs: now, skillLevel: 1));
        Assert.True(SkillPersistRules.IsToggle(kit.Game.Skills.Find(226)));
        Assert.True(SkillPersistRules.IsDance(268));
        Assert.True(HerbCatalog.LoadDefault().IsHerbSkill(2280));

        await kit.World.LeaveAsync(me.Session, CancellationToken.None);
        var saved = kit.Store.FindById("skip", me.Session.ObjectId)?.Effects ?? [];
        Assert.DoesNotContain(saved, e => e.SkillId is 226 or 268 or 2280);

        BenchPlayer again = await kit.EnterAsync(account: "skip", name: "HeroS");
        Assert.DoesNotContain(again.Player.Abnormals, a => a.Id is 226 or 268 or 2280);
    }
}
