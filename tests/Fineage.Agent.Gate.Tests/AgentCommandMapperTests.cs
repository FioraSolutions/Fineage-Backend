using System.Text.Json;
using Fineage.Agent.Gate;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Agent.Gate.Tests;

public sealed class AgentCommandMapperTests
{
    [Fact]
    public void Move_and_navigate_map_to_move_command()
    {
        var args = JsonDocument.Parse("""{"destX":1,"destY":2,"destZ":3}""").RootElement;
        Assert.True(AgentCommandMapper.TryMap("move", args, out ZoneCommand? move, out _));
        var moveCommand = Assert.IsType<MoveCommand>(move);
        Assert.Equal((1, 2, 3), (moveCommand.DestX, moveCommand.DestY, moveCommand.DestZ));
        Assert.True(AgentCommandMapper.TryMap("navigate", args, out ZoneCommand? navigate, out _));
        Assert.IsType<MoveCommand>(navigate);
    }

    [Fact]
    public void Attack_use_item_and_drop_item_map_to_existing_commands()
    {
        Assert.True(AgentCommandMapper.TryMap(
            "attack", JsonDocument.Parse("""{"targetObjectId":9}""").RootElement, out ZoneCommand? attack, out _));
        Assert.Equal(9, Assert.IsType<AttackCommand>(attack).TargetObjectId);
        Assert.False(Assert.IsType<AttackCommand>(attack).Forced);

        Assert.True(AgentCommandMapper.TryMap(
            "use_item", JsonDocument.Parse("""{"objectId":11}""").RootElement, out ZoneCommand? use, out _));
        Assert.Equal(11, Assert.IsType<UseItemCommand>(use).ObjectId);

        Assert.True(AgentCommandMapper.TryMap(
            "drop_item",
            JsonDocument.Parse("""{"objectId":11,"count":2,"x":3,"y":4,"z":5}""").RootElement,
            out ZoneCommand? drop,
            out _));
        var dropCommand = Assert.IsType<DropItemCommand>(drop);
        Assert.Equal((11, 2, 3, 4, 5), (dropCommand.ObjectId, dropCommand.Count, dropCommand.X, dropCommand.Y, dropCommand.Z));
    }

    [Fact]
    public void Missing_arguments_and_unknown_tools_are_refused()
    {
        Assert.False(AgentCommandMapper.TryMap("move", JsonDocument.Parse("{}").RootElement, out _, out string? error));
        Assert.Contains("destX", error);
        Assert.False(AgentCommandMapper.TryMap("look", JsonDocument.Parse("{}").RootElement, out _, out _));
        Assert.False(AgentCommandMapper.IsWriteTool("look"));
        Assert.True(AgentCommandMapper.IsWriteTool("move"));
    }
}
