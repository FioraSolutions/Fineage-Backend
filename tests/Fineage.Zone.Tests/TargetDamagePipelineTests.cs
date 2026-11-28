using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius CT0 <c>PlayerStatus.reduceHp</c> CP barrier (<c>PlayerStatus.java:212-222</c>) and the shared
/// <c>ApplyTargetDamage</c> path. CP belongs to players only; a playable attacker (player or summon) spends it
/// before HP, an NPC attacker bypasses it, and explicit callers can do the same.
/// </summary>
public sealed class TargetDamageSplitTests
{
    [Fact]
    public void Full_cp_absorb_leaves_hp_untouched()
    {
        var split = TargetDamage.Split(amount: 50, currentCp: 80, currentHp: 200, cpAbsorbs: true);
        Assert.Equal(new DamageSplit(50, 0), split);
    }

    [Fact]
    public void Cp_to_hp_overflow_splits_the_remainder()
    {
        var split = TargetDamage.Split(amount: 100, currentCp: 30, currentHp: 200, cpAbsorbs: true);
        Assert.Equal(new DamageSplit(30, 70), split);
    }

    [Fact]
    public void Exact_cp_hit_empties_the_pool()
    {
        Assert.Equal(new DamageSplit(30, 0), TargetDamage.Split(30, 30, 200, cpAbsorbs: true));
    }

    [Fact]
    public void Npc_attacker_bypasses_cp()
    {
        Assert.Equal(new DamageSplit(0, 50), TargetDamage.Split(50, 80, 200, cpAbsorbs: false));
    }

    [Fact]
    public void Explicit_ignore_cp_bypasses_the_pool()
    {
        Assert.Equal(new DamageSplit(0, 50), TargetDamage.Split(50, 80, 200, cpAbsorbs: true, ignoreCp: true));
    }

    [Fact]
    public void Hp_is_capped_at_zero_and_amount_is_never_negative()
    {
        Assert.Equal(new DamageSplit(30, 100), TargetDamage.Split(500, 30, 100, cpAbsorbs: true));
        Assert.Equal(DamageSplit.None, TargetDamage.Split(0, 100, 100, cpAbsorbs: true));
        Assert.Equal(DamageSplit.None, TargetDamage.Split(-1, 100, 100, cpAbsorbs: true));
    }
}

public sealed class TargetDamageWorldTests
{
    static async Task<(NetTestKit Kit, ZoneEntity Attacker, ZoneEntity Target, ZoneSession TargetSession)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (_, attacker) = await kit.EnterAsync(classId: 0, level: 40, x: NetTestKit.ElfX - 100);
        var (session, target) = await kit.EnterAsync(classId: 25, level: 40);
        target.MaxHp = 1_000;
        target.CurHp = 1_000;
        target.MaxCp = 200;
        target.CurCp = 200;
        return (kit, attacker, target, session);
    }

    [Fact]
    public async Task Player_attacker_spends_cp_first_and_reports_actual_losses()
    {
        var (kit, attacker, target, _) = await SetupAsync();
        var dealt = kit.World.ApplyTargetDamageForTest(target, attacker, 150);

        Assert.Equal(new DamageSplit(150, 0), dealt);
        Assert.Equal(50, target.CurCp);
        Assert.Equal(1_000, target.CurHp);
    }

    [Fact]
    public async Task Overflow_after_cp_lands_on_hp()
    {
        var (kit, attacker, target, _) = await SetupAsync();
        target.CurCp = 40;

        var dealt = kit.World.ApplyTargetDamageForTest(target, attacker, 100);

        Assert.Equal(new DamageSplit(40, 60), dealt);
        Assert.Equal(0, target.CurCp);
        Assert.Equal(940, target.CurHp);
    }

    [Fact]
    public async Task Npc_attacker_bypasses_the_cp_pool()
    {
        var (kit, _, target, _) = await SetupAsync();
        var npc = kit.World.AttackableForTest()!;

        var dealt = kit.World.ApplyTargetDamageForTest(target, npc, 100);

        Assert.Equal(new DamageSplit(0, 100), dealt);
        Assert.Equal(target.MaxCp, target.CurCp);
        Assert.Equal(900, target.CurHp);
    }

    [Fact]
    public async Task Missing_attacker_bypasses_cp_for_hp_consumption_and_environment_injury()
    {
        var (kit, _, target, _) = await SetupAsync();

        var dealt = kit.World.ApplyTargetDamageForTest(target, attacker: null, 100);

        Assert.Equal(new DamageSplit(0, 100), dealt);
        Assert.Equal(target.MaxCp, target.CurCp);
    }

    [Fact]
    public async Task Explicit_ignore_cp_bypasses_the_pool_for_special_effects()
    {
        var (kit, attacker, target, _) = await SetupAsync();

        var dealt = kit.World.ApplyTargetDamageForTest(target, attacker, 100, ignoreCp: true);

        Assert.Equal(new DamageSplit(0, 100), dealt);
        Assert.Equal(target.MaxCp, target.CurCp);
    }

    [Fact]
    public async Task ReduceCp_is_the_transfer_cp_helper_and_clamps_at_zero()
    {
        var (kit, _, target, _) = await SetupAsync();
        target.CurCp = 200;

        Assert.Equal(80, ZoneWorld.ReduceCpForTest(target, 80));
        Assert.Equal(120, target.CurCp);
        Assert.Equal(120, ZoneWorld.ReduceCpForTest(target, 500));
        Assert.Equal(0, target.CurCp);
        Assert.Equal(0, ZoneWorld.ReduceCpForTest(target, 10));
    }

    [Fact]
    public async Task Dot_without_effector_bypasses_cp_and_keeps_the_no_kill_floor()
    {
        var (kit, _, target, _) = await SetupAsync();
        target.MaxHp = 50;
        target.CurHp = 10;
        target.Abnormals.Add(new ZoneAbnormalEntry
        {
            Id = 96, // Bleed lv1: 25 HP per tick
            SkillLevel = 1,
            Slot = "BLEEDING",
            ExpiresAtUnixMs = kit.Now.ToUnixTimeMilliseconds() + 60_000,
            NextDotAtUnixMs = 0
        });

        kit.World.TickDotDamageForTest(target, kit.Now.ToUnixTimeMilliseconds() + 1);

        Assert.Equal(target.MaxCp, target.CurCp);
        Assert.Equal(1, target.CurHp); // canKill=false floor
    }

    [Fact]
    public async Task Dot_with_a_playable_effector_spends_cp_like_the_five_arg_reduceHp()
    {
        var (kit, attacker, target, _) = await SetupAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        target.MaxHp = 50;
        target.CurHp = 10;
        target.Abnormals.Add(new ZoneAbnormalEntry
        {
            Id = 96,
            SkillLevel = 1,
            Slot = "BLEEDING",
            ExpiresAtUnixMs = kit.Now.ToUnixTimeMilliseconds() + 60_000,
            NextDotAtUnixMs = 0,
            EffectorObjectId = attacker.ObjectId
        });
        rec.Calls.Clear();

        kit.World.TickDotDamageForTest(target, kit.Now.ToUnixTimeMilliseconds() + 1);

        // DamOverTime first clamps the tick to currentHp - 1 (9), then reduceHp spends that clamped amount from CP.
        Assert.Equal(200 - 9, target.CurCp);
        Assert.Equal(10, target.CurHp);
        var update = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)));
        var status = Assert.IsType<StatusUpdateMsg>(update[1]);
        int cpIndex = Array.IndexOf(status.AttributeIds, InterludeStatus.CurCp);
        Assert.True(cpIndex >= 0);
        Assert.Equal(target.CurCp, status.Values[cpIndex]);
    }

    [Fact]
    public async Task Death_then_respawn_keeps_the_spent_cp_and_restores_hp()
    {
        var (kit, attacker, target, session) = await SetupAsync();
        target.CurHp = 50;

        var dealt = kit.World.ApplyTargetDamageForTest(target, attacker, 250);
        Assert.Equal(new DamageSplit(200, 50), dealt);
        Assert.Equal(0, target.CurCp);
        Assert.Equal(0, target.CurHp);

        kit.World.ApplyPlayerDeathForTest(target, killerObjectId: 0);
        Assert.True(target.Dead);
        Assert.Equal(0, target.CurCp);

        Assert.Null(await kit.World.TryReviveAsync(session, DeathProtocol.ReviveToVillage, CancellationToken.None));
        Assert.False(target.Dead);
        Assert.True(target.CurHp > 0);
        Assert.Equal(0, target.CurCp);
    }

    [Fact]
    public async Task Cp_absorbed_skill_hit_still_breaks_a_magic_cast()
    {
        // Default NetTestKit map is ti_cedric; the elf-start enter of SetupAsync is nowhere
        // near an attackable, and MovePlayerCoordsForTests does not rebuild the knownlist.
        // SkillCastConditionsMet then fails silently (returns null) and never arms PendingHit.
        // Mirror CastInterruptTests: elven_starting, enter already next to the mob.
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var (_, attacker) = await kit.EnterAsync(classId: 0, level: 40, x: mob.X + 200, y: mob.Y, z: mob.Z);
        var (session, target) = await kit.EnterAsync(classId: 25, level: 40, x: mob.X + 100, y: mob.Y, z: mob.Z);
        target.MaxHp = target.CurHp = 1_000_000;
        target.MaxCp = target.CurCp = 10_000_000;
        target.KnownSkills[1177] = 1; // Wind Strike
        target.CurMp = 1_000;
        Assert.Null(await kit.World.TrySkillUseAsync(session, 1177, mob.ObjectId, CancellationToken.None));
        Assert.NotNull(target.PendingHit);

        int hp = target.CurHp;
        long now = kit.Now.ToUnixTimeMilliseconds();
        for (int i = 0; i < 50 && target.PendingHit != null; i++)
            kit.World.ApplySkillDamageToPlayerForTest(attacker, target, damage: 100_000, now: now);

        Assert.Null(target.PendingHit);
        Assert.Equal(hp, target.CurHp); // every hit was absorbed by CP
        Assert.True(target.CurCp < 10_000_000);
    }

    [Fact]
    public async Task Cp_regenerates_after_the_shared_bar_was_spent()
    {
        var (kit, attacker, target, _) = await SetupAsync();
        target.CurHp = target.MaxHp;
        target.CurCp = 10;
        target.RegenCarryCp = 0;
        kit.World.ApplyTargetDamageForTest(target, attacker, 190);
        Assert.Equal(0, target.CurCp);

        await kit.World.RunRegenTickAsync(CancellationToken.None);

        Assert.True(target.CurCp > 0);
    }
}
