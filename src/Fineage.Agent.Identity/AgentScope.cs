namespace Fineage.Agent.Identity;

/// <summary>Permission scopes an Agent may be granted. Unknown values are refused.</summary>
public readonly record struct AgentScope
{
    public const string WorldReadValue = "world.read";
    public const string NavigationExecuteValue = "navigation.execute";
    public const string CombatExecuteValue = "combat.execute";
    public const string QuestReadValue = "quest.read";
    public const string InventoryMutateValue = "inventory.mutate";
    public const string BillingReadValue = "billing.read";

    public static AgentScope WorldRead { get; } = new(WorldReadValue);
    public static AgentScope NavigationExecute { get; } = new(NavigationExecuteValue);
    public static AgentScope CombatExecute { get; } = new(CombatExecuteValue);
    public static AgentScope QuestRead { get; } = new(QuestReadValue);
    public static AgentScope InventoryMutate { get; } = new(InventoryMutateValue);
    public static AgentScope BillingRead { get; } = new(BillingReadValue);

    public static IReadOnlyList<AgentScope> All { get; } =
    [
        WorldRead,
        NavigationExecute,
        CombatExecute,
        QuestRead,
        InventoryMutate,
        BillingRead,
    ];

    static readonly HashSet<string> Allowed = new(All.Select(scope => scope.Value), StringComparer.Ordinal);

    AgentScope(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;

    public static bool TryParse(string? value, out AgentScope scope)
    {
        if (!string.IsNullOrWhiteSpace(value) && Allowed.Contains(value.Trim()))
        {
            scope = new AgentScope(value.Trim());
            return true;
        }

        scope = default;
        return false;
    }

    public static AgentScope Parse(string value)
    {
        if (!TryParse(value, out AgentScope scope))
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                "Unknown Agent scope.");
        }

        return scope;
    }
}
