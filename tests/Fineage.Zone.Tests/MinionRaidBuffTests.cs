using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius <c>AttackableAI</c> minion 1/30 buff roll while following the raid leader (#804).</summary>
public sealed class MinionRaidBuffTests
{
    [Fact]
    public void Raid_private_minion_25017_has_imported_follow_buff_skill()
    {
        var row = NpcAiSupportCatalog.LoadDefault().Get(25017);
        Assert.NotNull(row?.Buff);
        Assert.Contains(row!.Value.Buff!, s => s.SkillId == 4213);
    }

    [Fact]
    public async Task Minion_follow_buff_roll_casts_on_leader_when_self_already_buffed()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var (_, player) = await kit.EnterAsync(classId: 25, x: NetTestKit.ElfX, y: NetTestKit.ElfY, z: NetTestKit.ElfZ);
        var mobs = kit.World.AttackablesForTest(2);
        var leader = mobs[0];
        var minion = mobs[1];
        leader.X = leader.Y = player.X;
        leader.Z = player.Z;
        minion.LeaderObjectId = leader.ObjectId;
        minion.RaidMinion = true;
        minion.X = leader.X + 10;
        minion.Y = leader.Y;
        minion.Z = leader.Z;
        minion.CurMp = minion.MaxMp = 500;
        var imported = NpcAiSupportCatalog.LoadDefault().Get(25017)!.Value.Buff![0];
        var skill = imported with { HitTimeMs = 0 };
        minion.AiBuffSkills = [skill];
        minion.Abnormals.Add(new ZoneAbnormalEntry
        {
            Id = skill.SkillId,
            Value = skill.AbnormalLevel,
            SkillLevel = skill.Level,
            Slot = skill.Slot ?? ""
        });

        Assert.True(kit.World.TryNpcLeaderBuffForTest(minion, kit.Now.ToUnixTimeMilliseconds()));
        Assert.Contains(leader.Abnormals, a => a.Id == skill.SkillId);
    }
}