using Fineage.Game;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// BlockBuffSlot, NoblesseBless and ProtectionBlessing run off the skill-effect catalog.
/// The skill id is not a special case: a row without the effect name does not apply the rule.
/// </summary>
public sealed class BlessingEffectTests
{
    const int DefenseAura = 91;
    const int BlockShield = 1358;
    const int Noblesse = 1323;
    const int RetailProtection = 5182;

    [Fact]
    public async Task Blocked_slot_refuses_a_rebuff_until_the_blocker_expires()
    {
        var kit = new NetTestKit();
        var (_, player) = await kit.EnterAsync();
        var aura = Skill(kit, DefenseAura);
        var block = Skill(kit, BlockShield);
        Assert.True(aura.TryLevelData(1, out var auraLevel));
        Assert.True(block.TryLevelData(1, out var blockLevel));

        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(aura, auraLevel, player));
        Assert.Contains(player.Abnormals, a => a.Id == DefenseAura);

        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(block, blockLevel, player));
        Assert.Contains(player.Abnormals, a => a.Id == BlockShield);

        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(aura, auraLevel, player));
        Assert.DoesNotContain(player.Abnormals, a => a.Id == DefenseAura);
        Assert.Contains(player.Abnormals, a => a.Id == BlockShield);

        long expiry = player.Abnormals.Single(a => a.Id == BlockShield).ExpiresAtUnixMs;
        kit.AdvanceToTickCovering(expiry + 1);
        await kit.World.RunAbnormalTickAsync(CancellationToken.None);
        Assert.DoesNotContain(player.Abnormals, a => a.Id == BlockShield);

        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(aura, auraLevel, player));
        Assert.Contains(player.Abnormals, a => a.Id == DefenseAura);
    }

    [Fact]
    public async Task Empty_block_slot_blocks_nothing()
    {
        const int blocker = 900010;
        const int aura = 900011;
        var effects = SkillEffectCatalog.Parse(
            """
            {"skills":[
              {"skillId":900010,"effects":[{"name":"BlockBuffSlot","slot":""}]},
              {"skillId":900011,"effects":[{"name":"Buff"}]}
            ]}
            """);
        var kit = new NetTestKit(skillEffects: effects);
        var (_, player) = await kit.EnterAsync();

        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(Buff(blocker, "BLOCK"), Level(), player));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(Buff(aura, "PD_UP"), Level(), player));
        Assert.Contains(player.Abnormals, a => a.Id == aura);
    }

    [Fact]
    public async Task Noblesse_blessing_skips_exp_loss_and_is_consumed()
    {
        var kit = new NetTestKit(deathRules: DeathRuleCatalog.ForTest(100, 0.65, 0));
        var (session, player) = await kit.EnterAsync();
        var table = kit.Game.Classes!.Experience;
        long top = table.ExpToBeLevel(2) - 1;
        player.Exp = top;
        var noblesse = Skill(kit, Noblesse);
        Assert.True(noblesse.TryLevelData(1, out var level));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(noblesse, level, player));

        kit.World.KillPlayerByNpcForTest(player, Npc(kit));

        Assert.True(player.Dead);
        Assert.Equal(top, player.Exp);
        Assert.Equal(0, player.DeathPenaltyLevel);
        Assert.DoesNotContain(player.Abnormals, a => a.Id == Noblesse);
        Assert.NotNull(session);
    }

    [Fact]
    public async Task Noblesse_on_another_skill_id_still_consumes()
    {
        const int skillId = 900020;
        var effects = SkillEffectCatalog.Parse(
            """
            {"skills":[{"skillId":900020,"effects":[{"name":"NoblesseBless"}]}]}
            """);
        var kit = new NetTestKit(deathRules: DeathRuleCatalog.ForTest(100, 0.65, 0), skillEffects: effects);
        var (_, player) = await kit.EnterAsync();
        long top = kit.Game.Classes!.Experience.ExpToBeLevel(2) - 1;
        player.Exp = top;
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(Buff(skillId, "NOBLE"), Level(), player));

        kit.World.KillPlayerByNpcForTest(player, Npc(kit));

        Assert.Equal(top, player.Exp);
        Assert.Equal(0, player.DeathPenaltyLevel);
        Assert.DoesNotContain(player.Abnormals, a => a.Id == skillId);
    }

    [Fact]
    public async Task Protection_blessing_follows_the_effect_name()
    {
        var effects = SkillEffectCatalog.Parse(
            """
            {"skills":[
              {"skillId":900030,"effects":[{"name":"ProtectionBlessing"}]},
              {"skillId":5182,"effects":[{"name":"Buff"}]}
            ]}
            """);
        var kit = new NetTestKit(skillEffects: effects);
        var (_, low) = await kit.EnterAsync();
        var (_, high) = await kit.EnterAsync();
        high.Level = low.Level + PlayableAttackRules.BlessingLevelGap;
        high.Karma = 1;

        Assert.False(kit.World.ProtectionBlessingRefusesForTest(high, low));

        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(Buff(900030, "BLESS"), Level(), low));
        Assert.True(kit.World.ProtectionBlessingRefusesForTest(high, low));

        low.Abnormals.Clear();
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(Buff(RetailProtection, "OTHER"), Level(), low));
        Assert.False(kit.World.ProtectionBlessingRefusesForTest(high, low));
    }

    [Fact]
    public async Task Missing_effect_name_refuses_the_skill()
    {
        const int skillId = DefenseAura;
        var effects = SkillEffectCatalog.Parse(
            """
            {"skills":[{"skillId":91,"effects":[{"name":"NoSuchEffect"}]}]}
            """);
        var kit = new NetTestKit(skillEffects: effects);
        var (session, player) = await kit.EnterAsync();
        player.KnownSkills[skillId] = 1;

        Assert.Equal(SkillProtocol.EffectMissing, await kit.World.TrySkillUseAsync(session, skillId, 0, CancellationToken.None));
        Assert.Empty(player.Abnormals);
    }

    static SkillDefinition Skill(NetTestKit kit, int id) =>
        kit.Game.Skills.Find(id) ?? throw new InvalidOperationException($"skill {id} missing from skills.json");

    static SkillLevelData Level() => new(1, 0, 0, abnormalLevel: 1);

    static SkillDefinition Buff(int id, string slot) => new(
        id, "t", false, "A2", "SELF", 0, 0, 0, 30, false, [Level()])
    { AbnormalType = slot };

    static ZoneEntity Npc(NetTestKit kit)
    {
        int id = kit.World.NpcObjectIds.First();
        Assert.True(kit.World.TryGetEntityById(id, out var npc));
        return npc;
    }
}
