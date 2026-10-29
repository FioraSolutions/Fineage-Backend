using Fineage.Protocol;
using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneCombatDamageChatTests
{
    [Fact]
    public void HitTarget_uses_mobius_catalog_id_and_params()
    {
        var line = CombatLines.HitTarget("Gremlin", 32);
        Assert.Equal(CombatProtocol.HitTargetForDamage, line.Id);
        Assert.Equal(["Gremlin", "32"], line.Params);
        Assert.Equal("You hit Gremlin for 32 damage.", line.Text);
    }
}
