using Fineage.Game.Skills;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Trainer foundation: free NPC skills stay the default; SP learn is behind TrainerSpLearn; learned skills persist.</summary>
public sealed class TrainerFoundationTests
{
    [Fact]
    public void Skill_trees_carry_mobius_levelUpSp()
    {
        var trees = Fineage.Game.GameData.LoadDefault().SkillTrees;
        var mystic = trees.FindClass(25)!; // Elven Mystic
        Assert.Contains(mystic.Skills, s => s.LearnedByNpc && s.LevelUpSp > 0);
    }

    [Fact]
    public async Task Default_mode_keeps_free_npc_skills_and_refuses_paid_learn()
    {
        var kit = new NetTestKit();
        var (session, elf) = await kit.EnterAsync();
        Assert.False(kit.World.TrainerSpLearn);
        int before = elf.KnownSkills.Count;
        long sp = elf.Sp;
        Assert.Null(kit.World.TrainerServiceForTest(session, NpcServiceIntent.SkillList));
        Assert.Null(kit.World.TrainerServiceForTest(session, NpcServiceIntent.LearnSkill, 1177));
        Assert.Equal(before, elf.KnownSkills.Count);
        Assert.Equal(sp, elf.Sp);
        Assert.Empty(elf.LearnedSkills);
    }

    [Fact]
    public async Task Sp_mode_learns_next_level_spends_sp_and_persists()
    {
        var kit = new NetTestKit();
        kit.World.TrainerSpLearn = true;
        var (session, elf) = await kit.EnterAsync();
        elf.Level = 20;
        elf.Sp = 1_000_000;
        var game = Fineage.Game.GameData.LoadDefault();
        var row = TrainerSkills.Available(game.SkillTrees, game.Skills, elf.ClassId, elf.Level, elf.KnownSkills, game.Classes)[0];

        Assert.Null(kit.World.TrainerServiceForTest(session, NpcServiceIntent.LearnSkill, row.SkillId));
        Assert.Equal(row.Level, elf.KnownSkills[row.SkillId]);
        Assert.Equal(1_000_000 - row.LevelUpSp, elf.Sp);
        var stored = kit.Store.FindById(session.Account, session.ObjectId)!;
        Assert.Contains(stored.LearnedSkills!, s => s.SkillId == row.SkillId && s.Level == row.Level);
    }

    [Fact]
    public async Task Sp_mode_refuses_without_sp()
    {
        var kit = new NetTestKit();
        kit.World.TrainerSpLearn = true;
        var (session, elf) = await kit.EnterAsync();
        elf.Level = 20;
        elf.Sp = 0;
        var game = Fineage.Game.GameData.LoadDefault();
        var row = TrainerSkills.Available(game.SkillTrees, game.Skills, elf.ClassId, elf.Level, elf.KnownSkills, game.Classes)
            .First(r => r.LevelUpSp > 0);
        kit.World.TrainerServiceForTest(session, NpcServiceIntent.LearnSkill, row.SkillId);
        Assert.Empty(elf.LearnedSkills);
    }
}
