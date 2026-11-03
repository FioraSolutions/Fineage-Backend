using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius runSpd stat (Wind Walk / Slow / Quick Step / Paralyze…): buffs change the live run speed, Net watchers get a
/// fresh ChangeMoveType, and the timed mover really travels faster. Mobius has no walkSpd stat and clamps at MaxRunSpeed 250.
/// </summary>
public sealed class ZoneRunSpeedTests
{
    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18); // Elven Fighter
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
    public async Task Wind_walk_raises_live_run_speed_and_move_speed()
    {
        var (kit, session, player, rec) = await SetupAsync();
        float baseRun = player.BaseRunSpeed;
        Assert.Equal(baseRun, player.RunSpeed, 3);
        Assert.Equal(baseRun, ZoneWorld.MoveSpeedOf(player), 3);

        // Mobius Wind Walk 1: <add stat="runSpd" val="20" />.
        ZoneAbnormals.Apply(player.Abnormals, 1204, 1, 1200, kit.Now.ToUnixTimeMilliseconds(), 1);
        Assert.True(kit.World.RefreshRunSpeed(player));
        Assert.Equal(baseRun + 20f, player.RunSpeed, 3);
        Assert.Equal(baseRun + 20f, ZoneWorld.MoveSpeedOf(player), 3);
        // Walk speed has no Mobius stat.
        Assert.Equal(baseRun, player.BaseRunSpeed, 3);

        ZoneAbnormals.TickExpired(player.Abnormals, long.MaxValue);
        Assert.True(kit.World.RefreshRunSpeed(player));
        Assert.Equal(baseRun, player.RunSpeed, 3);

        rec.Calls.Clear();
        Assert.Null(await kit.World.TryBuffRequestAsync(session, new BuffRequestMsg { EffectId = 1204 }, CancellationToken.None));
        var moves = rec.Of(nameof(INetPresenceBridge.FanoutChangeMoveType));
        Assert.Contains(moves, m => (int)m[1]! == player.ObjectId && (float)m[3]! == baseRun + 20f);
    }

    [Fact]
    public async Task Slow_debuff_lowers_speed_and_run_speed_never_reaches_zero()
    {
        var (kit, _, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed;

        ZoneAbnormals.Apply(player.Abnormals, 1160, 3, 120, kit.Now.ToUnixTimeMilliseconds(), 3); // Slow: runSpd ×0.7
        Assert.True(kit.World.RefreshRunSpeed(player));
        Assert.Equal((float)(baseRun * 0.7), player.RunSpeed, 3);
        Assert.True(ZoneWorld.MoveSpeedOf(player) < baseRun);

        // Mobius Creature.initStats floor: a pathological multiply can never leave the pawn unable to move.
        var mods = new CombatStatMods();
        mods.Add(new SkillStatFunc(CombatStat.RunSpd, 'm', [0.0009]), 1);
        Assert.Equal(ZoneCombat.MinRunSpeed, ZoneWorld.RunSpeedStat(player, mods));
    }

    [Fact]
    public async Task Run_speed_is_capped_at_mobius_max_run_speed()
    {
        var (kit, _, player, _) = await SetupAsync();
        // Super Haste 7029 puts runSpd at ×5 — the Mobius MaxRunSpeed clamp keeps the wire value sane.
        ZoneAbnormals.Apply(player.Abnormals, 7029, 5, 120, kit.Now.ToUnixTimeMilliseconds(), 5);
        kit.World.RefreshRunSpeed(player);
        Assert.Equal(ZoneCombat.MaxRunSpeed, player.RunSpeed);
    }

    [Fact]
    public async Task Wind_walk_shortens_the_travel_time_of_a_timed_move()
    {
        var (kit, session, player, _) = await SetupAsync();
        float baseRun = player.BaseRunSpeed;
        int destX = player.X + 600;

        Assert.Null(await kit.World.TryMoveAsync(session, new MoveToMsg
        {
            ObjectId = player.ObjectId, DestX = destX, DestY = player.Y, DestZ = player.Z,
            OriginX = player.X, OriginY = player.Y, OriginZ = player.Z
        }, CancellationToken.None));
        Assert.True(player.MoveActive);

        // Same tick budget: the buffed pawn covers more ground (applied through the real buff path).
        Assert.Null(await kit.World.TryBuffRequestAsync(session, new BuffRequestMsg { EffectId = 1204 }, CancellationToken.None));
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
