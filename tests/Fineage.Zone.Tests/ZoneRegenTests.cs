using Fineage.Login;
using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneRegenFormulaTests
{
    [Fact]
    public void Standing_fighter_level_one_rates()
    {
        Assert.Equal(1, ZoneRegen.HpPerTick(ClassTemplates.HumanFighterHp, level: 1, sitting: false));
        Assert.Equal(1, ZoneRegen.MpPerTick(ClassTemplates.HumanFighterMp, level: 1, sitting: false));
    }

    [Fact]
    public void Sitting_doubles_standing_rates()
    {
        int hp = ZoneRegen.HpPerTick(ClassTemplates.HumanFighterHp, level: 1, sitting: false);
        int mp = ZoneRegen.MpPerTick(ClassTemplates.HumanFighterMp, level: 1, sitting: false);
        Assert.Equal(hp * 2, ZoneRegen.HpPerTick(ClassTemplates.HumanFighterHp, level: 1, sitting: true));
        Assert.Equal(mp * 2, ZoneRegen.MpPerTick(ClassTemplates.HumanFighterMp, level: 1, sitting: true));
    }
}

public sealed class ZoneRegenSharedTests
{
    [Fact]
    public async Task Two_clients_see_same_regen_StatusUpdate()
    {
        await ZoneMultiplayerAcceptance.RunRegenSharedAsync();
    }
}
