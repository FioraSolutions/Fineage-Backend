using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>Formulas.calcCpRegen</c> (template row × level mod × CON, then <c>REGENERATE_CP_RATE</c>, then posture)
/// and the generic recovery handlers (Braveheart 440 CpHeal, ManaHealByLevel factor).
/// </summary>
public sealed class ZoneCpRegenFormulaTests
{
    const double ConBonus = 1.58;

    [Fact]
    public void Base_cp_rows_follow_the_datapack_plateaus()
    {
        Assert.Equal(2.0, ZoneRegen.BaseCpRate(1));
        Assert.Equal(2.0, ZoneRegen.BaseCpRate(10));
        Assert.Equal(2.5, ZoneRegen.BaseCpRate(11));
        Assert.Equal(3.5, ZoneRegen.BaseCpRate(21));
        Assert.Equal(8.5, ZoneRegen.BaseCpRate(80));
    }

    [Fact]
    public void Cp_init_is_the_row_times_level_mod_times_CON_floored_at_one()
    {
        // Level 1: 2.0 × 0.9 × 1.58 = 2.844, already above the max(1, …) floor.
        Assert.Equal(2.0 * 0.9 * ConBonus, ZoneRegen.CpInit(1, ConBonus), 6);
        Assert.Equal(1.0, ZoneRegen.CpInit(1, conBonus: 0.1), 6);
    }

    [Fact]
    public void Mana_heal_by_level_follows_the_Mobius_step()
    {
        Assert.Equal(1.0, ZoneWorld.ManaHealByLevelFactor(targetLevel: 40, magicLevel: 40));
        Assert.Equal(1.0, ZoneWorld.ManaHealByLevelFactor(targetLevel: 45, magicLevel: 40));
        Assert.Equal(0.9, ZoneWorld.ManaHealByLevelFactor(targetLevel: 46, magicLevel: 40), 6);
        Assert.Equal(0.1, ZoneWorld.ManaHealByLevelFactor(targetLevel: 54, magicLevel: 40), 6);
        Assert.Equal(0.0, ZoneWorld.ManaHealByLevelFactor(targetLevel: 55, magicLevel: 40));
        Assert.Equal(1.0, ZoneWorld.ManaHealByLevelFactor(targetLevel: 80, magicLevel: 0));
    }
}

public sealed class ZoneCpRegenTickTests
{
    static async Task<(NetTestKit Kit, ZoneEntity Player, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (_, player) = await kit.EnterAsync(classId: 0, level: 40);
        player.CurHp = player.MaxHp;
        player.CurMp = player.MaxMp;
        player.MaxCp = Math.Max(player.MaxCp, 200);
        player.CurCp = 1;
        player.RegenCarryCp = 0;
        player.RegenCarryHp = 0;
        player.RegenCarryMp = 0;
        rec.Calls.Clear();
        return (kit, player, rec);
    }

    [Fact]
    public async Task Wounded_cp_regenerates_in_the_shared_tick_and_fans_CurCp()
    {
        var (kit, player, rec) = await SetupAsync();
        double rate = kit.World.CpRegenRate(player);
        Assert.True(rate > 0, $"expected a positive CP rate, got {rate}");

        await kit.World.RunRegenTickAsync(CancellationToken.None);

        int gained = (int)Math.Floor(rate);
        Assert.Equal(1 + gained, player.CurCp);
        Assert.Equal(rate - gained, player.RegenCarryCp, 6);

        var update = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)));
        var status = Assert.IsType<StatusUpdateMsg>(update[1]);
        Assert.Contains(InterludeStatus.CurCp, status.AttributeIds);
        Assert.Equal(player.CurCp, status.Values[Array.IndexOf(status.AttributeIds, InterludeStatus.CurCp)]);
    }

    [Fact]
    public async Task Full_cp_skips_the_tick()
    {
        var (kit, player, rec) = await SetupAsync();
        player.CurCp = player.MaxCp;
        rec.Calls.Clear();

        await kit.World.RunRegenTickAsync(CancellationToken.None);

        Assert.Equal(player.MaxCp, player.CurCp);
        Assert.Equal(0d, player.RegenCarryCp);
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)));
    }

    [Fact]
    public async Task Sitting_raises_cp_regen_by_the_Mobius_posture_after_the_stat_fold()
    {
        var (kit, player, _) = await SetupAsync();
        player.Sitting = false;
        double standing = kit.World.CpRegenRate(player);
        player.Sitting = true;
        double sitting = kit.World.CpRegenRate(player);
        Assert.Equal(standing / 1.1 * 1.5, sitting, 6);
    }
}

public sealed class ZoneRecoveryHandlerTests
{
    const int Braveheart = 440;

    [Fact]
    public async Task Braveheart_restores_1000_cp_at_hit_time_and_starts_reuse_without_waiting()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (session, player) = await kit.EnterAsync(classId: 0, level: 78);
        player.KnownSkills[Braveheart] = 1;
        player.MaxCp = Math.Max(player.MaxCp, 2000);
        player.CurCp = 100;
        player.CurMp = Math.Max(player.CurMp, 200);
        int mpBefore = player.CurMp;
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySkillUseAsync(session, Braveheart, 0, CancellationToken.None));

        // Reuse and MP are paid when the cast starts, the bar moves at hitTime.
        Assert.True(player.SkillReuseUntilMs.TryGetValue(Braveheart, out long until));
        Assert.True(until > kit.Now.ToUnixTimeMilliseconds());
        Assert.Equal(100, player.CurCp);
        Assert.True(player.CurMp < mpBefore);

        await kit.World.CompleteCastForTestAsync(session);

        Assert.Equal(1100, player.CurCp);
        var updates = rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate));
        Assert.Contains(updates, args =>
        {
            if (args[1] is not StatusUpdateMsg status)
                return false;
            int i = Array.IndexOf(status.AttributeIds, InterludeStatus.CurCp);
            return i >= 0 && status.Values[i] == 1100;
        });
    }

    [Fact]
    public async Task Braveheart_does_not_raise_cp_past_the_maximum()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 0, level: 78);
        player.KnownSkills[Braveheart] = 1;
        player.MaxCp = 200;
        player.CurCp = 150;
        player.CurMp = Math.Max(player.CurMp, 200);

        Assert.Null(await kit.World.TrySkillUseAsync(session, Braveheart, 0, CancellationToken.None));
        await kit.World.CompleteCastForTestAsync(session);

        Assert.Equal(200, player.CurCp);
        Assert.Equal(0d, player.RegenCarryCp);
    }
}
