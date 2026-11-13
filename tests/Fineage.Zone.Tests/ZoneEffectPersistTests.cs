using Fineage.Zone;
using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius character_skills_save: //mbuff (and other abnormals) survive Leave → Enter.</summary>
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

        // Same account+name re-enters the persisted character (WorldBench keeps the record).
        BenchPlayer again = await kit.EnterAsync(account: "buffs", name: "KuK");
        Assert.Contains(again.Player.Abnormals, a => a.Id == ZoneBuffs.SampleWindWalk);
        Assert.True(kit.World.TryBuildNetAbnormalStatus(again.Session, out var wire));
        Assert.Contains(wire.Effects, e => e.Id == ZoneBuffs.SampleWindWalk && e.DurationSeconds > 0);
    }
}
