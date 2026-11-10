using Fineage.Protocol;
using Fineage.Zone.Skills;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class SkillUseRulesTests
{
    [Fact]
    public void Precheck_rejects_dead_muted_bad_unknown_passive_reuse_and_mp()
    {
        Assert.Equal(
            SkillProtocol.Dead,
            SkillUseRules.PrecheckError(true, false, 1, true, true, false, true, false, 10, 5));
        Assert.Equal(
            SkillProtocol.Muted,
            SkillUseRules.PrecheckError(false, true, 1, true, true, false, true, false, 10, 5));
        Assert.Equal(
            "bad_request",
            SkillUseRules.PrecheckError(false, false, 0, true, true, false, true, false, 10, 5));
        Assert.Equal(
            SkillProtocol.Unknown,
            SkillUseRules.PrecheckError(false, false, 1, false, true, false, true, false, 10, 5));
        Assert.Equal(
            SkillProtocol.Passive,
            SkillUseRules.PrecheckError(false, false, 1, true, true, true, true, false, 10, 5));
        Assert.Equal(
            SkillProtocol.Reuse,
            SkillUseRules.PrecheckError(false, false, 1, true, true, false, true, true, 10, 5));
        Assert.Equal(
            SkillProtocol.Mp,
            SkillUseRules.PrecheckError(false, false, 1, true, true, false, true, false, 4, 5));
        Assert.Null(
            SkillUseRules.PrecheckError(false, false, 1, true, true, false, true, false, 5, 5));
    }
}
