using Fineage.Zone.Movement;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class MoveRulesTests
{
    [Fact]
    public void Sanitize_dest_z_keeps_sane_slope_and_clamps_bogus()
    {
        Assert.Equal(100, MoveRules.SanitizeDestZ(0, 0, 100, 10, 0, 100));
        Assert.Equal(
            100 + MoveRules.MaxZFlat,
            MoveRules.SanitizeDestZ(0, 0, 100, 0, 0, 100 + MoveRules.MaxZFlat));
        Assert.Equal(100, MoveRules.SanitizeDestZ(0, 0, 100, 0, 0, 100 + MoveRules.MaxZFlat + 1));
        Assert.Equal(150, MoveRules.SanitizeDestZ(0, 0, 100, 100, 0, 150));
    }
}
