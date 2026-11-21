using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class QuestNpcMarkTests
{
    [Fact]
    public void New_character_sees_tutorial_helper_marker()
    {
        var data = GameData.LoadDefault();
        var machine = new QuestMachine(data.Quests, data.Items);
        var j = new QuestJournal();
        var actor = new QuestActor(0, 1, 0);
        var marks = machine.CollectNpcMarks(j, actor);
        Assert.Contains(marks, m => m.NpcId == 30009 && m.Mark == 1);
    }
}