using System.Text.Json;
using Fineage.Agent.Contracts;
using Fineage.Zone.World;

namespace Fineage.Agent.Gate;

/// <summary>Maps an accepted Agent tool name onto an existing <see cref="ZoneCommand"/>. No second command hierarchy.</summary>
public static class AgentCommandMapper
{
    public static bool IsWriteTool(string toolName) => toolName is "move" or "navigate" or "attack" or "use_item" or "drop_item";

    public static bool TryMap(string toolName, JsonElement arguments, out ZoneCommand? command, out string? error)
    {
        command = null;
        error = null;
        try
        {
            command = toolName switch
            {
                "move" or "navigate" => new MoveCommand(
                    Int(arguments, "destX", "x"),
                    Int(arguments, "destY", "y"),
                    Int(arguments, "destZ", "z")),
                "attack" => new AttackCommand(Int(arguments, "targetObjectId", "objectId"), Bool(arguments, "forced")),
                "use_item" => new UseItemCommand(Int(arguments, "objectId")),
                "drop_item" => new DropItemCommand(
                    Int(arguments, "objectId"),
                    Int(arguments, "count"),
                    Int(arguments, "x", "destX"),
                    Int(arguments, "y", "destY"),
                    Int(arguments, "z", "destZ")),
                _ => null
            };
        }
        catch (AgentContractException ex)
        {
            error = ex.Message;
            return false;
        }

        if (command is null)
        {
            error = $"Tool '{toolName}' is not a Zone write tool.";
            return false;
        }

        return true;
    }

    static int Int(JsonElement arguments, params string[] names)
    {
        foreach (string name in names)
        {
            if (arguments.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number))
                return number;
        }

        throw new AgentContractException(
            AgentRefusalCodes.InvalidContract,
            $"Argument '{names[0]}' is required.");
    }

    static bool Bool(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True;
}
