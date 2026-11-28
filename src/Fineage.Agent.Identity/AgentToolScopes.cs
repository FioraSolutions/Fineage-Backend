namespace Fineage.Agent.Identity;

/// <summary>
/// Maps a published tool name to the scope it requires. Unknown tools are refused;
/// they are not treated as allowed.
/// </summary>
public static class AgentToolScopes
{
    static readonly Dictionary<string, AgentScope> Required = new(StringComparer.Ordinal)
    {
        ["look"] = AgentScope.WorldRead,
        ["inspect"] = AgentScope.WorldRead,
        ["move"] = AgentScope.NavigationExecute,
        ["navigate"] = AgentScope.NavigationExecute,
        ["attack"] = AgentScope.CombatExecute,
        ["quest_log"] = AgentScope.QuestRead,
        ["use_item"] = AgentScope.InventoryMutate,
        ["drop_item"] = AgentScope.InventoryMutate,
        ["usage"] = AgentScope.BillingRead,
        ["quote"] = AgentScope.BillingRead,
    };

    public static bool TryGetRequiredScope(string toolName, out AgentScope scope) =>
        Required.TryGetValue(toolName, out scope);
}
