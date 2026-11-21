using Fineage.Game;
using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class QuestTutorialAndRewardCatalogTests
{
    [Fact]
    public void Tutorial_ui_and_journal_rewards_load_from_pack()
    {
        var data = GameData.LoadDefault();
        var q999 = data.Quests.Quests.FirstOrDefault(q => q.Id == 999);
        Assert.NotNull(q999);
        Assert.True(q999!.CondClientFx.TryGetValue(1, out var fx));
        Assert.Contains(fx, o => o is TutorialUiOp { Kind: TutorialUiWireKind.QuestionMark, On: true });
        Assert.Contains(fx, o => o is SoundOp { Name: "tutorial_voice_007" });

        var q998 = data.Quests.Quests.FirstOrDefault(q => q.Id == 998);
        Assert.NotNull(q998);
        Assert.Equal(4, q998!.JournalRewards.Count);
    }
}