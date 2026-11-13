using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Debuff matrix: Slow actually slows NPCs, Fear/Stun/Sleep/Root land as slots,
/// spoil shows on the AbnormalStatusUpdate snapshot used by TargetStatus.
/// </summary>
public sealed class DebuffApplyMatrixTests
{
    const int Slow = 1160;
    const int Fear = 1092;
    const int ShieldStun = 92;
    const int Sleep = 1069;
    const int DryadRoot = 1201;

    [Fact]
    public async Task Slow_reduces_npc_run_speed()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var npc = kit.World.AttackableForTest()!;
        await kit.EnterAsync(classId: 25, x: npc.X, y: npc.Y, z: npc.Z);
        float before = npc.RunSpeed;
        Assert.True(before > 0);

        ApplyForced(kit, npc, Slow, level: 1);
        Assert.Contains(npc.Abnormals, a => a.Id == Slow && a.Slot == "SPEED_DOWN");
        Assert.True(npc.RunSpeed < before,
            $"Slow should cut runSpd (before={before}, after={npc.RunSpeed})");
        Assert.Equal(before * 0.8f, npc.RunSpeed, precision: 2);
    }

    [Fact]
    public async Task Fear_stun_sleep_root_write_abnormal_slots()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var npc = kit.World.AttackableForTest()!;
        await kit.EnterAsync(classId: 25, x: npc.X, y: npc.Y, z: npc.Z);

        ApplyForced(kit, npc, Fear, level: 1);
        Assert.Contains(npc.Abnormals, a => a.Id == Fear && a.Slot == "TURN_FLEE");

        ApplyForced(kit, npc, ShieldStun, level: 1);
        Assert.Contains(npc.Abnormals, a => a.Id == ShieldStun && a.Slot == "STUN");

        ApplyForced(kit, npc, Sleep, level: 1);
        Assert.Contains(npc.Abnormals, a => a.Id == Sleep && a.Slot == "SLEEP");

        ApplyForced(kit, npc, DryadRoot, level: 1);
        Assert.Contains(npc.Abnormals, a => a.Id == DryadRoot && a.Slot == "ROOT_MAGICALLY");
    }

    [Fact]
    public async Task Stun_aborts_the_swing_and_the_cast_sleep_leaves_the_cast_root_leaves_the_swing()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var (session, player) = await kit.EnterAsync(classId: 25, x: 0, y: 0, z: 0);
        long future = kit.Now.ToUnixTimeMilliseconds() + 5_000;

        session.AutoAttackTargetId = 9;
        player.CastingUntilMs = future;
        ApplyForced(kit, player, ShieldStun, level: 1);
        Assert.Equal(0, session.AutoAttackTargetId);
        Assert.Equal(0, player.CastingUntilMs);

        session.AutoAttackTargetId = 9;
        player.CastingUntilMs = future;
        ApplyForced(kit, player, Sleep, level: 1);
        Assert.Equal(0, session.AutoAttackTargetId);
        Assert.Equal(future, player.CastingUntilMs);

        session.AutoAttackTargetId = 9;
        ApplyForced(kit, player, DryadRoot, level: 1);
        Assert.Equal(9, session.AutoAttackTargetId);
    }

    [Fact]
    public async Task Fear_runs_a_player_away_and_drops_a_magic_cast()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var (session, player) = await kit.EnterAsync(classId: 25, x: 0, y: 0, z: 0);
        var caster = new ZoneEntity { X = -100, Y = 0, Z = 0 };
        session.AutoAttackTargetId = 9;
        player.PendingHit = new Fineage.Zone.Combat.PendingSkillHit(1, 10, kit.Now.ToUnixTimeMilliseconds() + 5_000)
        {
            MagicType = 1
        };
        player.CastingUntilMs = kit.Now.ToUnixTimeMilliseconds() + 5_000;

        var template = kit.Game.Skills.Find(Fear) ?? throw new InvalidOperationException("fear missing");
        Assert.True(template.TryLevelData(1, out var data));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(template, data, player, force: true, attacker: caster));

        Assert.Equal(0, session.AutoAttackTargetId);
        Assert.Null(player.PendingHit);
        Assert.Equal(0, player.CastingUntilMs);
        Assert.True(player.MoveDestX > player.X, $"flee X {player.MoveDestX} should pass {player.X}");
        Assert.Equal(player.Y, player.MoveDestY);
    }

    [Fact]
    public async Task Fear_runs_a_mob_away_from_the_caster_not_its_hate_target()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        await kit.EnterAsync(classId: 25, x: 0, y: 0, z: 0);
        var npc = kit.World.AttackableForTest()!;
        int x = npc.X, y = npc.Y;
        npc.AggroTargetId = 9;
        var caster = new ZoneEntity { X = x - 400, Y = y, Z = npc.Z };

        var template = kit.Game.Skills.Find(Fear) ?? throw new InvalidOperationException("fear missing");
        Assert.True(template.TryLevelData(1, out var data));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(template, data, npc, force: true, attacker: caster));

        Assert.True(npc.MoveDestX > x, $"flee X {npc.MoveDestX} should pass {x}");
        Assert.Equal(y, npc.MoveDestY);
    }

    [Fact]
    public async Task Spoil_appears_in_abnormal_snapshot_for_target_window()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var npc = kit.World.AttackableForTest()!;
        await kit.EnterAsync(classId: 25, x: npc.X, y: npc.Y, z: npc.Z);

        var empty = kit.World.AbnormalSnapshotForTest(npc);
        Assert.DoesNotContain(empty.Effects, e => e.Id == 254);

        npc.SpoilerObjectId = 42;
        var spoiled = kit.World.AbnormalSnapshotForTest(npc);
        Assert.Contains(spoiled.Effects, e => e.Id == 254 && e.Value == 1);
    }

    [Fact]
    public void Cancel_and_mana_burn_route_via_skill_effects()
    {
        var effects = Fineage.Zone.Combat.SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryDispelByCategory(1056, out _));
        Assert.True(effects.HasMagicalAttackMp(1398));
        var cancel = new NetTestKit().Game.Skills.Find(1056);
        Assert.NotNull(cancel);
        Assert.False(cancel!.IsDebuffSkill);
        Assert.Equal(30_000, cancel.ReuseDelayMs);
    }

    static void ApplyForced(NetTestKit kit, ZoneEntity npc, int skillId, int level)
    {
        var template = kit.Game.Skills.Find(skillId)
            ?? throw new InvalidOperationException($"skill {skillId} missing");
        Assert.True(template.TryLevelData(level, out var data));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(template, data, npc, force: true));
        kit.World.RefreshRunSpeed(npc);
    }
}
