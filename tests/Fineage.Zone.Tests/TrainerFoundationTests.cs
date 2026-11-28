using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #1049: production default is Mobius SP learn; tests opt into free-grants via <see cref="TestHostDefaults"/>.
/// </summary>
public sealed class TrainerFoundationTests
{
    [Fact]
    public void Env_default_and_aliases_resolve_mobius_on()
    {
        string? previous = Environment.GetEnvironmentVariable("FINEAGE_TRAINER_SP_LEARN");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_TRAINER_SP_LEARN", null);
            Assert.True(ZoneWorld.ResolveTrainerSpLearnFromEnv());
            Environment.SetEnvironmentVariable("FINEAGE_TRAINER_SP_LEARN", "1");
            Assert.True(ZoneWorld.ResolveTrainerSpLearnFromEnv());
            Environment.SetEnvironmentVariable("FINEAGE_TRAINER_SP_LEARN", "true");
            Assert.True(ZoneWorld.ResolveTrainerSpLearnFromEnv());
            Environment.SetEnvironmentVariable("FINEAGE_TRAINER_SP_LEARN", "0");
            Assert.False(ZoneWorld.ResolveTrainerSpLearnFromEnv());
            Environment.SetEnvironmentVariable("FINEAGE_TRAINER_SP_LEARN", "false");
            Assert.False(ZoneWorld.ResolveTrainerSpLearnFromEnv());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_TRAINER_SP_LEARN", previous ?? "0");
        }
    }

    [Fact]
    public void Boot_diagnostic_names_the_effective_mode()
    {
        Assert.Contains("mobius", ZoneWorld.DescribeTrainerSpLearnMode(true), StringComparison.Ordinal);
        Assert.Contains("free-grants", ZoneWorld.DescribeTrainerSpLearnMode(false), StringComparison.Ordinal);
    }

    [Fact]
    public void Skill_trees_carry_mobius_levelUpSp()
    {
        var trees = Fineage.Game.GameData.LoadDefault().SkillTrees;
        var mystic = trees.FindClass(25)!; // Elven Mystic
        Assert.Contains(mystic.Skills, s => s.LearnedByNpc && s.LevelUpSp > 0);
    }

    [Fact]
    public async Task Free_grant_mode_keeps_npc_skills_and_refuses_paid_learn()
    {
        var kit = new NetTestKit();
        Assert.False(kit.World.TrainerSpLearn);
        var (session, elf) = await kit.EnterAsync();
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

    [Fact]
    public async Task Sp_mode_enter_does_not_auto_grant_npc_tree_skills()
    {
        var kit = new NetTestKit();
        kit.World.TrainerSpLearn = true;
        var (_, elf) = await kit.EnterAsync(level: 20);
        // Wind Strike 1 is autoGet; higher Wind Strike rows are learnedByNpc and must stay for the trainer.
        Assert.True(elf.KnownSkills.TryGetValue(1177, out int wind));
        Assert.Equal(1, wind);
        Assert.Empty(elf.LearnedSkills);
        var game = Fineage.Game.GameData.LoadDefault();
        Assert.Contains(
            TrainerSkills.Available(game.SkillTrees, game.Skills, elf.ClassId, elf.Level, elf.KnownSkills, game.Classes),
            row => row.SkillId == 1177 && row.Level > 1);
    }
}
