using Fineage.Game;
using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class MissingQuestRank2Tests
{
    static readonly GameData Data = GameData.LoadDefault();

    [Theory]
    [InlineData(385, "Yoke of the Past")]
    [InlineData(426, "Quest for Fishing Shot")]
    [InlineData(505, "Blood Offering")]
    [InlineData(646, "Signs of Revolt")]
    public void Pack_loads_with_a_start_path(int questId, string name)
    {
        var quest = Data.Quests.Quests.First(q => q.Id == questId);
        Assert.Equal(name, quest.Name);
        Assert.Contains(quest.Dialogs, d =>
            d.Ops.Any(o => o is CreateOp) && d.Ops.Any(o => o is StartOp));
    }

    [Fact]
    public void Yoke_of_the_past_exchanges_ancient_scrolls()
    {
        var quest = Data.Quests.Quests.First(q => q.Id == 385);
        var turnIn = quest.Dialogs.First(d =>
            d.Hook == QuestHook.Talk
            && d.Ops.Any(o => o is ExchangeAllOp ex && ex.ItemId == 5902 && ex.RewardItemId == 5965));
        Assert.NotNull(turnIn);
    }
}