using Fineage.Game;
using Fineage.Game.Skills;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class InterludeSkillWhitelistTests
{
    [Fact]
    public void Every_datapack_skill_reference_is_on_the_interlude_whitelist()
    {
        string? data = GameData.FindDataDirectory(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("data directory not found");
        var whitelist = InterludeSkillWhitelist.Load(Path.Combine(data, InterludeSkillWhitelist.FileName));
        var violations = InterludeSkillReferenceValidator.ViolationsUnderDataDirectory(data, whitelist);
        Assert.True(
            violations.Count == 0,
            violations.Count == 0 ? null : string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Skills_json_rows_are_subset_of_interlude_whitelist()
    {
        string? data = GameData.FindDataDirectory(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("data directory not found");
        var whitelist = InterludeSkillWhitelist.Load(Path.Combine(data, InterludeSkillWhitelist.FileName));
        var skills = SkillCatalog.Load(Path.Combine(data, SkillCatalog.FileName));
        var extras = skills.Skills.Where(s => !whitelist.Contains(s.SkillId)).Select(s => s.SkillId).Order().ToList();
        Assert.Empty(extras);
    }

    [Fact]
    public void GameData_load_passes_interlude_skill_validation()
    {
        string? data = GameData.FindDataDirectory(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("data directory not found");
        _ = GameData.Load(data);
    }
}