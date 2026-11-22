using Fineage.Game;
using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class NoblesseGrantTests
{
    [Fact]
    public void Q247_village_master_costs_match_set_noble_step()
    {
        var data = GameData.LoadDefault();
        var q247 = data.Quests.Quests.FirstOrDefault(q => q.Id == NoblesseGrant.GateQuestId);
        Assert.NotNull(q247);
        Assert.True(NoblesseGrant.TryReadVillageMasterCosts(q247, out var costs));
        Assert.Equal(93836, costs.Sp);
        Assert.Equal(938360, costs.Adena);
        Assert.Equal(7694, costs.TiaraItemId);
        Assert.Equal(93836L, costs.RewardExp);
    }
}
