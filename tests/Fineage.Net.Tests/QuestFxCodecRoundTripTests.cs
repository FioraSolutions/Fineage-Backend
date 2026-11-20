using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class QuestFxCodecRoundTripTests
{
    [Fact]
    public void Quest_direction_round_trips()
    {
        var msg = new QuestDirectionNetMsg { QuestId = 1, Show = true, X = -84436, Y = 242793, Z = -3729 };
        var again = NetCodec.DecodeQuestDirection(NetCodec.EncodeQuestDirection(msg));
        Assert.Equal(1, again.QuestId);
        Assert.True(again.Show);
        Assert.Equal(-84436, again.X);
    }

    [Fact]
    public void Npc_marks_round_trip()
    {
        var msg = new QuestNpcMarksNetMsg
        {
            Marks = [new QuestNpcMarkNet { NpcId = 30009, Mark = 1 }, new QuestNpcMarkNet { NpcId = 30008, Mark = 2 }]
        };
        var again = NetCodec.DecodeQuestNpcMarks(NetCodec.EncodeQuestNpcMarks(msg));
        Assert.Equal(2, again.Marks.Length);
        Assert.Equal(30008, again.Marks[1].NpcId);
    }
}