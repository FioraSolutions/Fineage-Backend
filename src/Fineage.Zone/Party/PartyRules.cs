namespace Fineage.Zone.Party;

/// <summary>Pure Interlude party size checks. Loot distribution tokens live in <c>Fineage.Game.Services.PartyLootRules</c>.</summary>
public static class PartyRules
{
    /// <summary>Mobius max party size (Interlude).</summary>
    public const int MaxMembers = 9;

    public static bool IsFull(int memberCount) => memberCount >= MaxMembers;
}
