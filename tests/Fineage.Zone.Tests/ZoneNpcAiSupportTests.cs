using Fineage.Zone.Npcs;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius AISkillScope HEAL / BUFF / DEBUFF — second #259 slice.</summary>
public sealed class ZoneNpcAiSupportTests
{
    [Fact]
    public void Catalog_loads_beast_seer_heal()
    {
        var row = NpcAiSupportCatalog.LoadDefault().Get(21060);
        Assert.NotNull(row);
        Assert.NotNull(row!.Value.Heal);
        Assert.Contains(row.Value.Heal!, s => s.SkillId == 4065 && s.Level == 5 && s.Amount == 494);
    }

    [Fact]
    public void Catalog_loads_scavenger_bat_debuff()
    {
        var row = NpcAiSupportCatalog.LoadDefault().Get(20411);
        Assert.NotNull(row);
        Assert.Contains(row!.Value.Debuff!, s => s.SkillId == 4038);
    }

    [Fact]
    public void Catalog_loads_dismal_pole_buff()
    {
        var row = NpcAiSupportCatalog.LoadDefault().Get(21004);
        Assert.Contains(row!.Value.Buff!, s => s.SkillId == 4028 && s.Level == 3);
    }

    [Fact]
    public async Task Npc_heal_skill_restores_hp()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        mob.AiHealSkills =
        [
            new NpcAiSupportSkill(4065, 5, HitTimeMs: 0, ReuseDelayMs: 8000, Mp: 64, MpInitial: 16, Amount: 494)
        ];
        mob.CurHp = Math.Max(1, mob.MaxHp / 4);
        mob.CurMp = mob.MaxMp;
        int before = mob.CurHp;

        Assert.True(kit.World.CastNpcSupportForTest(mob, mob, mob.AiHealSkills![0], "heal"));
        Assert.True(mob.CurHp > before);
        Assert.True(mob.CurHp <= mob.MaxHp);
    }

    [Fact]
    public async Task Npc_self_buff_applies_abnormal()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var skill = new NpcAiSupportSkill(4028, 3, HitTimeMs: 0, ReuseDelayMs: 8000, Mp: 33, MpInitial: 0,
            AbnormalTimeSeconds: 300, AbnormalLevel: 3);
        mob.AiBuffSkills = [skill];
        mob.CurMp = mob.MaxMp;

        Assert.True(kit.World.CastNpcSupportForTest(mob, mob, skill, "buff"));
        Assert.Contains(mob.Abnormals, a => a.Id == 4028);
    }

    [Fact]
    public async Task Npc_debuff_applies_to_player()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var (_, player) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        var skill = new NpcAiSupportSkill(4038, 2, HitTimeMs: 0, ReuseDelayMs: 8000, Mp: 20, MpInitial: 0,
            AbnormalTimeSeconds: 30, AbnormalLevel: 2);
        mob.AiDebuffSkills = [skill];
        mob.CurMp = mob.MaxMp;

        Assert.True(kit.World.CastNpcSupportForTest(mob, player, skill, "debuff"));
        Assert.Contains(player.Abnormals, a => a.Id == 4038);
    }
}
