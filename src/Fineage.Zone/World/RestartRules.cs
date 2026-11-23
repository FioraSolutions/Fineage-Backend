using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>Facts a restart gate can see. Filled once from the live player.</summary>
public readonly record struct RestartFacts(bool Enchanting, bool StoreOpen, bool InCombat, bool Festival);

/// <summary>
/// One restart refusal. Each gate names the condition it blocks; the restart path walks the list
/// and does not grow a branch per situation.
/// </summary>
public sealed class RestartGate
{
    public RestartGate(string code, Func<RestartFacts, bool> blocks)
    {
        Code = code;
        Blocks = blocks;
    }

    public string Code { get; }
    public Func<RestartFacts, bool> Blocks { get; }
}

public static class RestartRules
{
    public static readonly RestartGate[] Gates =
    [
        new(RestartProtocol.Enchant, facts => facts.Enchanting),
        new(RestartProtocol.Store, facts => facts.StoreOpen),
        new(RestartProtocol.Combat, facts => facts.InCombat),
        new(RestartProtocol.Festival, facts => facts.Festival),
    ];

    public static string? Refuse(RestartFacts facts)
    {
        foreach (var gate in Gates)
        {
            if (gate.Blocks(facts))
                return gate.Code;
        }
        return null;
    }
}
