using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>operateType T</c> toggles and Fake Death (skill 60) through the zone world: activation cost, per-tick MP
/// drain, the second press switching off, the seated-only gate of Relax and the fake-death pose
/// (<c>WT_START_FAKEDEATH</c> / <c>WT_STOP_FAKEDEATH</c> + <c>Revive</c>).
/// </summary>
public sealed class ZoneToggleAndFakeDeathTests
{
    const int FakeDeath = 60;
    const int SilentMove = 221;
    const int Relax = 226;
    const int ViciousStance = 312;

    static async Task<(NetTestKit Kit, NetBridgeRecorder Rec, ZoneSession Session, ZoneEntity Player)> EnterWithBridgeAsync(
        int level = 40)
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (session, player) = await kit.EnterAsync(classId: 7, level: level); // Treasure Hunter
        return (kit, rec, session, player);
    }

    static IEnumerable<int> Messages(NetBridgeRecorder rec, int id) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Where(a => ((SystemMessageMsg)a[1]!).Id == id)
            .SelectMany(a => (IReadOnlyList<int>)a[0]!);

    [Fact]
    public void Toggle_effects_carry_power_and_ticks()
    {
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(Relax, out var relax));
        // Mobius 226: <effect name="Relax" ticks="3"><param power="1"/>
        Assert.Contains(relax, e => e.Name == "Relax" && e.Ticks == 3 && Math.Abs(e.Power - 1) < 1e-9);
        Assert.True(effects.TryGet(FakeDeath, out var fd));
        Assert.Contains(fd, e => e.Name == "FakeDeath" && e.Ticks == 5 && Math.Abs(e.Power - 10) < 1e-9);
        Assert.True(effects.TryGet(ViciousStance, out var vs));
        Assert.Contains(vs, e => e.Name == "MpConsumePerLevel" && e.Ticks == 5);
    }

    [Fact]
    public void Skill_data_has_toggle_rows_and_mp_initial_consume()
    {
        var skills = Fineage.Game.GameData.LoadDefault().Skills;
        foreach (int id in new[] { FakeDeath, SilentMove, Relax, ViciousStance })
        {
            var skill = skills.Find(id);
            Assert.NotNull(skill);
            Assert.Equal("T", skill!.OperateType, ignoreCase: true);
        }

        // Mobius 312 carries mpInitialConsume only (no mpConsume) and it rises with the level.
        var stance = skills.Find(ViciousStance)!;
        Assert.True(stance.TryLevelData(1, out var lv1));
        Assert.True(stance.TryLevelData(20, out var lv20));
        Assert.Equal(0, lv1.MpConsume);
        Assert.Equal(4, lv1.MpInitialConsume);
        Assert.Equal(14, lv20.MpInitialConsume);
    }

    [Fact]
    public async Task Toggle_switches_on_and_the_second_press_switches_it_off()
    {
        var (kit, rec, session, player) = await EnterWithBridgeAsync();
        player.KnownSkills[SilentMove] = 1;
        player.CurMp = 300;
        int mpBefore = player.CurMp;

        Assert.Null(await kit.World.TrySkillUseAsync(session, SilentMove, 0, CancellationToken.None));
        Assert.Contains(player.Abnormals, a => a.Id == SilentMove);
        Assert.Equal(mpBefore - 7, player.CurMp); // Mobius 221 mpConsume

        Assert.Null(await kit.World.TrySkillUseAsync(session, SilentMove, 0, CancellationToken.None));
        Assert.DoesNotContain(player.Abnormals, a => a.Id == SilentMove);
        // Mobius SystemMessageId.S1_HAS_BEEN_ABORTED (335) on the owner.
        Assert.Contains(player.ObjectId, Messages(rec, 335));
    }

    [Fact]
    public async Task Toggle_drains_mp_every_effect_tick()
    {
        var (kit, _, session, player) = await EnterWithBridgeAsync();
        player.KnownSkills[Relax] = 1;
        player.CurMp = 500;
        player.Sitting = true;

        Assert.Null(await kit.World.TrySkillUseAsync(session, Relax, 0, CancellationToken.None));
        int afterStart = player.CurMp;
        Assert.Equal(498, afterStart);

        // Relax ticks 3 → 3 · 666 ms ≈ 1998 ms, power 1.
        kit.Now = kit.Now.AddMilliseconds(2000);
        await kit.World.RunAbnormalTickAsync(CancellationToken.None);
        Assert.Equal(afterStart - 1, player.CurMp);
    }

    [Fact]
    public async Task Toggle_ends_when_mp_runs_out()
    {
        var (kit, rec, session, player) = await EnterWithBridgeAsync();
        player.KnownSkills[Relax] = 1;
        player.Sitting = true;
        player.CurMp = 2; // exactly the start cost; nothing left for the first tick.

        Assert.Null(await kit.World.TrySkillUseAsync(session, Relax, 0, CancellationToken.None));
        Assert.Contains(player.Abnormals, a => a.Id == Relax);

        kit.Now = kit.Now.AddMilliseconds(2000);
        await kit.World.RunAbnormalTickAsync(CancellationToken.None);

        Assert.DoesNotContain(player.Abnormals, a => a.Id == Relax);
        // Mobius YOUR_SKILL_WAS_DEACTIVATED_DUE_TO_LACK_OF_MP (140), not the generic abort.
        Assert.Contains(player.ObjectId, Messages(rec, 140));
    }

    [Fact]
    public async Task Relax_needs_a_seated_caster()
    {
        var (kit, _, session, player) = await EnterWithBridgeAsync();
        player.KnownSkills[Relax] = 1;
        player.CurMp = 500;

        Assert.Null(await kit.World.TrySkillUseAsync(session, Relax, 0, CancellationToken.None));
        Assert.DoesNotContain(player.Abnormals, a => a.Id == Relax);
    }

    [Fact]
    public async Task Mp_consume_per_level_toggle_scales_with_owner_level()
    {
        var (kit, _, session, player) = await EnterWithBridgeAsync(level: 80);
        player.KnownSkills[ViciousStance] = 20;
        player.CurMp = 1000;

        Assert.Null(await kit.World.TrySkillUseAsync(session, ViciousStance, 0, CancellationToken.None));
        Assert.Equal(1000 - 14, player.CurMp); // level 20 mpInitialConsume

        // power 0.8, ticks 5 (5 · 666 ms), level 80: 0.8 · 3.33 · (79 / 7.5) ≈ 28.06
        kit.Now = kit.Now.AddMilliseconds(3400);
        await kit.World.RunAbnormalTickAsync(CancellationToken.None);
        Assert.Equal(1000 - 14 - 28, player.CurMp);
    }

    [Fact]
    public async Task Fake_death_puts_the_pawn_in_the_death_pose_without_dying()
    {
        var (kit, rec, session, player) = await EnterWithBridgeAsync();
        player.KnownSkills[FakeDeath] = 1;
        player.CurMp = 400;

        Assert.Null(await kit.World.TrySkillUseAsync(session, FakeDeath, 0, CancellationToken.None));

        Assert.True(player.FakeDeath);
        Assert.False(player.Dead);
        Assert.Contains(player.Abnormals, a => a.Id == FakeDeath);
        Assert.Equal(200, player.CurMp); // Mobius 60 mpConsume

        var waits = rec.Of(nameof(IWorldEventPublisher.FanoutChangeWaitType));
        Assert.Contains(waits, a => (int)a[2]! == ActionProtocol.WaitStartFakeDeath);
    }

    [Fact]
    public async Task Fake_death_refuses_moves_attacks_and_other_skills()
    {
        var (kit, _, session, player) = await EnterWithBridgeAsync();
        player.KnownSkills[FakeDeath] = 1;
        player.KnownSkills[SilentMove] = 1;
        player.CurMp = 400;
        Assert.Null(await kit.World.TrySkillUseAsync(session, FakeDeath, 0, CancellationToken.None));

        Assert.Equal(ActionProtocol.Blocked,
            await kit.World.TryMoveAsync(session, player.X + 100, player.Y, player.Z, CancellationToken.None));

        // Another toggle is still refused while the pose is held (Mobius RequestMagicSkillUse isFakeDeath gate).
        Assert.Equal(ActionProtocol.Blocked,
            await kit.World.TrySkillUseAsync(session, SilentMove, 0, CancellationToken.None));
        Assert.True(player.FakeDeath);
    }

    [Fact]
    public async Task Fake_death_second_press_stands_the_pawn_up()
    {
        var (kit, rec, session, player) = await EnterWithBridgeAsync();
        player.KnownSkills[FakeDeath] = 1;
        player.CurMp = 400;
        Assert.Null(await kit.World.TrySkillUseAsync(session, FakeDeath, 0, CancellationToken.None));
        Assert.True(player.FakeDeath);

        // Mobius Player.checkUseMagicConditions resets the toggle before the isFakeDeath refuse, so the same skill
        // used again switches the pose off instead of being blocked.
        Assert.Null(await kit.World.TrySkillUseAsync(session, FakeDeath, 0, CancellationToken.None));

        Assert.False(player.FakeDeath);
        Assert.DoesNotContain(player.Abnormals, a => a.Id == FakeDeath);
        var waits = rec.Of(nameof(IWorldEventPublisher.FanoutChangeWaitType));
        Assert.Contains(waits, a => (int)a[2]! == ActionProtocol.WaitStopFakeDeath);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutRevive)), a => (int)a[1]! == player.ObjectId);
    }

    [Fact]
    public async Task Sit_action_stands_a_fake_dead_pawn_up()
    {
        var (kit, rec, session, player) = await EnterWithBridgeAsync();
        player.KnownSkills[FakeDeath] = 1;
        player.CurMp = 400;
        Assert.Null(await kit.World.TrySkillUseAsync(session, FakeDeath, 0, CancellationToken.None));

        Assert.Null(await kit.World.TryCharacterActionAsync(session, ActionProtocol.SitStand, 0, CancellationToken.None));

        Assert.False(player.FakeDeath);
        Assert.False(player.Sitting);
        Assert.DoesNotContain(player.Abnormals, a => a.Id == FakeDeath);
        var waits = rec.Of(nameof(IWorldEventPublisher.FanoutChangeWaitType));
        Assert.Contains(waits, a => (int)a[2]! == ActionProtocol.WaitStopFakeDeath);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutRevive)), a => (int)a[1]! == player.ObjectId);
    }

    [Fact]
    public async Task Fake_death_ticks_mp_and_ends_when_it_runs_out()
    {
        var (kit, rec, session, player) = await EnterWithBridgeAsync();
        player.KnownSkills[FakeDeath] = 1;
        player.CurMp = 210; // 200 start + 10 for the first tick

        Assert.Null(await kit.World.TrySkillUseAsync(session, FakeDeath, 0, CancellationToken.None));
        Assert.Equal(10, player.CurMp);

        kit.Now = kit.Now.AddMilliseconds(3400); // ticks 5 → 3330 ms, power 10 (33 MP) > the 10 left
        await kit.World.RunAbnormalTickAsync(CancellationToken.None);

        // Mobius FakeDeath.onActionTime: when the tick costs more than the owner has, nothing is deducted and the toggle
        // ends with YOUR_SKILL_WAS_DEACTIVATED_DUE_TO_LACK_OF_MP (140).
        Assert.Equal(10, player.CurMp);
        Assert.False(player.FakeDeath);
        Assert.DoesNotContain(player.Abnormals, a => a.Id == FakeDeath);
        Assert.Contains(player.ObjectId, Messages(rec, 140));
    }

    [Fact]
    public async Task Wake_up_hook_ends_the_pose()
    {
        var (kit, _, session, player) = await EnterWithBridgeAsync();
        player.KnownSkills[FakeDeath] = 1;
        player.CurMp = 400;
        Assert.Null(await kit.World.TrySkillUseAsync(session, FakeDeath, 0, CancellationToken.None));
        Assert.True(player.FakeDeath);

        Assert.True(kit.World.WakeUpFromFakeDeathForTest(player));

        Assert.False(player.FakeDeath);
        Assert.DoesNotContain(player.Abnormals, a => a.Id == FakeDeath);
        Assert.False(kit.World.WakeUpFromFakeDeathForTest(player)); // no-op when already up
    }
}
