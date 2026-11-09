using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>Stat.MOVE_SPEED("runSpd")</c> (Wind Walk / Slow / Quick Step / Super Haste…). One speed stat feeds run,
/// walk and swim alike (<c>CreatureStat.java:426-495</c>), the <c>FuncMoveSpeed</c> DEX bonus is multiplied in first
/// (<c>FuncMoveSpeed.java:34-45</c>), there is no lower floor, and only players hit <c>MaxRunSpeed</c> 250
/// (<c>PlayerStat.java:512-553</c>).
/// </summary>
public sealed class ZoneRunSpeedTests
{
    /// <summary>Elven Fighter 18: base run 125 / walk 90, DEX 35 → statBonus 1.15.</summary>
    const float ElfDexBonus = 1.15f;

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, session, player, rec);
    }

    [Fact]
    public void Catalog_carries_mobius_runSpd_funcs()
    {
        var effects = SkillStatEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(1204, out var windWalk), "Wind Walk 1204 must be in skill-stat-effects.json");
        Assert.Equal(20d, windWalk.Funcs.Single(f => f.Stat == CombatStat.RunSpd).ValueAt(1), 3);

        Assert.True(effects.TryGet(1160, out var slow), "Slow 1160 must be in skill-stat-effects.json");
        Assert.Contains(slow.Funcs, f => f.Stat == CombatStat.RunSpd && f.Op == 'm');

        var passives = SkillStatEffectCatalog.LoadPassives();
        Assert.True(passives.TryGet(169, out var quickStep), "Quick Step 169 must be in skill-passives.json");
        Assert.Contains(quickStep.Funcs, f => f.Stat == CombatStat.RunSpd && f.Op == 'a');

        // Stat funcs we already had keep their values (the regeneration only added runSpd).
        Assert.True(effects.TryGet(1068, out var might));
        Assert.Equal(1.15, might.Funcs[0].ValueAt(3), 3);
    }

    [Fact]
    public async Task FuncMoveSpeed_applies_the_dex_bonus_to_run_and_walk()
    {
        var (kit, _, player, _) = await SetupAsync();

        // Mobius: 125 * DEX(35) 1.15 = 143.75 run, 90 * 1.15 = 103.5 walk, before any buff.
        Assert.Equal(143.75f, player.RunSpeed, 3);
        Assert.Equal(103.5f, player.WalkSpeed, 3);
        Assert.Equal(125f, player.BaseRunSpeed, 3);
        Assert.Equal(90f, player.BaseWalkSpeed, 3);
        Assert.Equal(35, player.Dex);
        Assert.Equal(player.RunSpeed, ZoneWorld.MoveSpeedOf(player), 3);
    }

    [Fact]
    public async Task Wind_walk_raises_live_run_and_walk_speed()
    {
        var (kit, session, player, rec) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;
        float baseWalk = player.BaseWalkSpeed * ElfDexBonus;

        // Mobius Wind Walk 1: <add stat="runSpd" val="20" /> — added after the DEX factor, so walk moves too
        // (CreatureStat.getWalkSpeed folds the same Stat.MOVE_SPEED calculator).
        ZoneAbnormals.Apply(player.Abnormals, 1204, 1, 1200, kit.Now.ToUnixTimeMilliseconds(), 1);
        Assert.True(kit.World.RefreshRunSpeed(player));
        Assert.Equal(baseRun + 20f, player.RunSpeed, 3);
        Assert.Equal(baseWalk + 20f, player.WalkSpeed, 3);
        Assert.Equal(baseRun + 20f, ZoneWorld.MoveSpeedOf(player), 2);
        Assert.Equal(125f, player.BaseRunSpeed, 3); // the template base is never overwritten by a buff

        ZoneAbnormals.TickExpired(player.Abnormals, long.MaxValue);
        Assert.True(kit.World.RefreshRunSpeed(player));
        Assert.Equal(baseRun, player.RunSpeed, 3);
        Assert.Equal(baseWalk, player.WalkSpeed, 3);

        rec.Calls.Clear();
        Assert.Null(await kit.World.TryBuffRequestAsync(session, 0, 1204, false, CancellationToken.None));
        var moves = rec.Of(nameof(IWorldEventPublisher.FanoutChangeMoveType));
        // ChangeMoveType carries both gaits: the client animates walk speed from the same packet.
        Assert.Contains(moves, m => (int)m[1]! == player.ObjectId
            && (float)m[3]! == baseRun + 20f && (float)m[4]! == baseWalk + 20f);
    }

    [Fact]
    public async Task Walk_only_change_still_reports_a_speed_change()
    {
        var (kit, _, player, _) = await SetupAsync();

        // Drag the cached walk value out of line while leaving run untouched: RefreshRunSpeed recomputes both and must
        // report the walk-only delta, otherwise FanoutAbnormal would skip the ChangeMoveType for a walk-speed buff.
        player.SetLiveRunSpeed(player.RunSpeed);
        player.SetLiveWalkSpeed(player.WalkSpeed - 10f);

        Assert.True(kit.World.RefreshRunSpeed(player));
        Assert.Equal(103.5f, player.WalkSpeed, 3);
        Assert.Equal(143.75f, player.RunSpeed, 3);
    }

    [Fact]
    public async Task Slow_multiplies_run_walk_and_swim_speed()
    {
        var (kit, _, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;
        float baseWalk = player.BaseWalkSpeed * ElfDexBonus;

        // Mobius Slow 3: runSpd ×0.7 — a mul func, so it scales every base the one speed stat folds onto.
        ZoneAbnormals.Apply(player.Abnormals, 1160, 3, 120, kit.Now.ToUnixTimeMilliseconds(), 3);
        Assert.True(kit.World.RefreshRunSpeed(player));
        Assert.Equal(baseRun * 0.7f, player.RunSpeed, 3);
        Assert.Equal(baseWalk * 0.7f, player.WalkSpeed, 3);
        Assert.True(ZoneWorld.MoveSpeedOf(player) < baseRun);

        // Swim bases are 50 in every Interlude player template: 50 × 1.15 × 0.7 = 40.25.
        player.InWater = true;
        kit.World.RefreshRunSpeed(player);
        Assert.Equal(50f * ElfDexBonus * 0.7f, player.SwimRunSpeed, 3);
        Assert.Equal(50f * ElfDexBonus * 0.7f, player.SwimWalkSpeed, 3);
        Assert.True(ZoneWorld.MoveSpeedOf(player) < 50f);
    }

    [Fact]
    public async Task Swim_speed_uses_the_template_base_and_the_runSpd_pipeline()
    {
        var (kit, _, player, _) = await SetupAsync();

        // Mobius baseMoveSpd slowSwim/fastSwim differ per movement type: swim run and swim walk are not one constant.
        player.BaseSwimRunSpeed = 50f;
        player.BaseSwimWalkSpeed = 40f;
        player.InWater = true;
        kit.World.RefreshRunSpeed(player);

        Assert.Equal(50f * ElfDexBonus, player.SwimRunSpeed, 3);
        Assert.Equal(40f * ElfDexBonus, player.SwimWalkSpeed, 3);

        player.Running = true;
        Assert.Equal(57.5f, ZoneWorld.MoveSpeedOf(player), 3);
        player.Running = false;
        Assert.Equal(46f, ZoneWorld.MoveSpeedOf(player), 3);

        // Wind Walk changes swim speed too (same stat), which the old hardcoded 50 never allowed.
        ZoneAbnormals.Apply(player.Abnormals, 1204, 1, 1200, kit.Now.ToUnixTimeMilliseconds(), 1);
        kit.World.RefreshRunSpeed(player);
        Assert.Equal(57.5f + 20f, player.SwimRunSpeed, 3);
        Assert.Equal(46f + 20f, player.SwimWalkSpeed, 3);
    }

    [Fact]
    public async Task Speed_has_no_lower_floor_so_zero_is_legal()
    {
        var (kit, _, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;

        // Mobius calcStat leaves MOVE_SPEED alone in the "never <= 0" switch (CreatureStat.java:99-125):
        // a tiny multiplier is NOT clamped up to 1.
        var tiny = new CombatStatMods();
        tiny.Add(new SkillStatFunc(CombatStat.RunSpd, 'm', [0.0009]), 1);
        float almost = kit.World.RunSpeedStat(player, tiny);
        Assert.True(almost > 0f && almost < 1f);
        Assert.Equal(baseRun * 0.0009f, almost, 3);

        // Weight Penalty 4 is runSpd ×0 — the pawn stands still and the wire value stays 0.
        var stop = new CombatStatMods();
        stop.Add(new SkillStatFunc(CombatStat.RunSpd, 'm', [0d]), 1);
        Assert.Equal(0f, kit.World.RunSpeedStat(player, stop));
        Assert.Equal(0f, kit.World.WalkSpeedStat(player, stop));
    }

    [Fact]
    public async Task Player_run_speed_is_capped_at_mobius_max_run_speed()
    {
        var (kit, _, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;

        // Super Haste 7029 puts runSpd at ×5 — PlayerStat.getRunSpeed caps the result at Config.MAX_RUN_SPEED.
        ZoneAbnormals.Apply(player.Abnormals, 7029, 5, 120, kit.Now.ToUnixTimeMilliseconds(), 5);
        kit.World.RefreshRunSpeed(player);
        Assert.Equal(ZoneCombat.MaxRunSpeed, player.RunSpeed);
        Assert.Equal(ZoneCombat.MaxRunSpeed, player.WalkSpeed);
        Assert.True(baseRun * 5f > ZoneCombat.MaxRunSpeed);
    }

    [Fact]
    public async Task Npc_speed_is_not_capped_and_not_dex_scaled()
    {
        var (kit, _, _, _) = await SetupAsync();

        // NpcStat inherits CreatureStat without PlayerStat's cap; NPC calculators also have no MOVE_SPEED entry,
        // so Formulas never registers FuncMoveSpeed (Formulas.java:206 is players/summons only).
        var boss = new ZoneEntity
        {
            ObjectId = 90_001, Kind = ZoneEntityKind.Npc, NpcId = 29019, Name = "Valakas",
            RunSpeed = 300f, WalkSpeed = 180f, Dex = 30, Level = 80
        };
        kit.World.RefreshRunSpeed(boss);
        Assert.Equal(300f, boss.RunSpeed, 3);
        Assert.Equal(180f, boss.WalkSpeed, 3);

        // Buffs still apply, but no cap and no DEX multiplier.
        ZoneAbnormals.Apply(boss.Abnormals, 1204, 1, 1200, kit.Now.ToUnixTimeMilliseconds(), 1);
        kit.World.RefreshRunSpeed(boss);
        Assert.Equal(320f, boss.RunSpeed, 3);
    }

    [Fact]
    public async Task Run_speed_boost_is_a_flat_bonus_before_the_cap()
    {
        var (kit, _, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;
        float baseWalk = player.BaseWalkSpeed * ElfDexBonus;

        // Mobius PlayerStat: val = super.getRunSpeed() + Config.RUN_SPD_BOOST, then the MaxRunSpeed check.
        Assert.Equal(baseRun + 30f, kit.World.RunSpeedStat(player, CombatStatMods.None, runSpeedBoost: 30), 3);
        Assert.Equal(baseWalk + 30f, kit.World.WalkSpeedStat(player, CombatStatMods.None, runSpeedBoost: 30), 3);

        // The boost is added before the clamp, so a silly value simply saturates at 250.
        Assert.Equal(ZoneCombat.MaxRunSpeed, kit.World.RunSpeedStat(player, CombatStatMods.None, runSpeedBoost: 500), 3);

        // NPCs never get the player-only boost.
        var npc = new ZoneEntity { ObjectId = 90_002, Kind = ZoneEntityKind.Npc, RunSpeed = 100f, Dex = 30 };
        Assert.Equal(100f, kit.World.RunSpeedStat(npc, CombatStatMods.None, runSpeedBoost: 30), 3);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData(" 25 ", 25)]
    [InlineData("0", 0)]
    [InlineData("-5", 0)]
    [InlineData("nope", 0)]
    [InlineData("9999", 250)]
    public void Run_speed_boost_setting_matches_the_ini_default_and_parsing(string? text, int expected)
    {
        Assert.Equal(expected, ZoneSpeedSettings.ParseRunSpeedBoost(text));
    }

    [Fact]
    public async Task Wind_walk_shortens_the_travel_time_of_a_timed_move()
    {
        var (kit, session, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;
        int destX = player.X + 600;

        Assert.Null(await kit.World.TryMoveAsync(session, destX, player.Y, player.Z, CancellationToken.None));
        Assert.True(player.MoveActive);

        // Same tick budget: the buffed pawn covers more ground (applied through the real buff path).
        Assert.Null(await kit.World.TryBuffRequestAsync(session, 0, 1204, false, CancellationToken.None));
        player.Abnormals.Clear();
        ZoneAbnormals.Apply(player.Abnormals, 1204, 2, 1200, kit.Now.ToUnixTimeMilliseconds(), 2); // +33
        kit.World.RefreshRunSpeed(player);
        Assert.Equal(baseRun + 33f, player.RunSpeed, 3);

        int startX = player.X;
        kit.Now = kit.Now.AddSeconds(1);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        int travelled = player.X - startX;
        Assert.InRange(travelled, (int)(baseRun + 10), (int)(baseRun + 40));
    }
}
