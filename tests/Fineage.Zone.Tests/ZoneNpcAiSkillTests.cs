using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius NPC AI attack skills (skillList ∩ skill-combat) — first slice of #259.</summary>
public sealed class ZoneNpcAiSkillTests
{
    [Fact]
    public void Catalog_loads_ant_patrol_mortal_blow()
    {
        var npc = NpcCatalog.LoadDefault().Get(20084);
        Assert.NotNull(npc.AiSkills);
        Assert.Contains(npc.AiSkills!, s => s.SkillId == 4067 && s.Level == 3);
        Assert.Equal(7, npc.MinSkillChance);
        Assert.Equal(15, npc.MaxSkillChance);
    }

    [Fact]
    public void Catalog_loads_dre_vanul_windstrike()
    {
        var npc = NpcCatalog.LoadDefault().Get(20056);
        Assert.Contains(npc.AiSkills!, s => s.SkillId == 4001 && s.Level == 2);
    }

    [Fact]
    public void Gremlin_has_no_ai_attack_skills()
    {
        Assert.Null(NpcCatalog.LoadDefault().Get(20001).AiSkills);
    }

    [Fact]
    public void Physical_npc_skill_damage_uses_power_formula()
    {
        Assert.True(SkillCombatCatalog.LoadDefault().TryGet(4032, out var combat));
        // lv3 power 219: 70*(pAtk + power)/pDef with pAtk=100, pDef=50, no ss → 70*319/50 = 446
        int dmg = ZoneCombat.PhysicalSkillDamage(100, 50, combat!.PowerAt(3), soulshot: false);
        Assert.Equal(446, dmg);
    }
}
