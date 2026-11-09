using Fineage.Protocol;

namespace Fineage.Zone.Craft;

/// <summary>
/// Pure Mobius self-craft checks (busy / learned / skill / MP).
/// World owns materials scan, weight/slots, consume, and success roll under the gate.
/// </summary>
public static class CraftRules
{
    /// <summary>
    /// Refusal before material / capacity mutation. Recipe existence and KnownRecipes are caller-supplied flags.
    /// </summary>
    public static string? MakeSelfError(
        bool dead,
        bool privateStoreOpen,
        bool inTrade,
        bool recipeKnown,
        bool recipeLearned,
        bool skillLevelOk,
        int curMp,
        int mpConsume)
    {
        if (dead)
            return InventoryProtocol.Dead;
        if (privateStoreOpen || inTrade)
            return CraftProtocol.Busy;
        if (!recipeKnown)
            return CraftProtocol.Unknown;
        if (!recipeLearned)
            return CraftProtocol.NotLearned;
        if (!skillLevelOk)
            return CraftProtocol.Skill;
        if (curMp < mpConsume)
            return CraftProtocol.Mp;
        return null;
    }

    /// <summary>Refusal when using a recipe scroll that is already in the book.</summary>
    public static string? LearnScrollError(bool recipeResolved, bool alreadyKnown)
    {
        if (!recipeResolved)
            return InventoryProtocol.Blocked;
        if (alreadyKnown)
            return CraftProtocol.Already;
        return null;
    }

    /// <summary>Mobius: successRate &gt;= 100 always succeeds; else roll in [0,100).</summary>
    public static bool Succeeds(int successRate, int roll0To99) =>
        successRate >= 100 || roll0To99 < successRate;
}
