namespace Fineage.Game.Services;

/// <summary>Mobius <c>PartyDistributionType</c> (packet id 0–4).</summary>
public enum PartyDistributionType
{
    FindersKeepers = 0,
    Random = 1,
    RandomIncludingSpoil = 2,
    ByTurn = 3,
    ByTurnIncludingSpoil = 4
}

/// <summary>
/// Mobius <c>Party.getActualLooter</c> / <c>getCheckedRandomMember</c> / <c>getCheckedNextLooter</c>
/// for spoil and (later) ground loot.
/// </summary>
public static class PartyLootRules
{
    /// <summary>L2J/Mobius <c>Config.ALT_PARTY_RANGE</c> default.</summary>
    public const int PartyRange = 1600;

    public static bool IsValidDistribution(int id) => id is >= 0 and <= 4;

    /// <summary>
    /// Pick who receives one item stack. <paramref name="membersInOrder"/> is the party member list
    /// (Mobius <c>_members</c>). <paramref name="eligible"/> is in-range + can hold the item.
    /// When no eligible member (or finders-keepers / non-spoil RANDOM/BY_TURN), returns
    /// <paramref name="sweeperObjectId"/>.
    /// </summary>
    public static int ActualLooter(
        int sweeperObjectId,
        PartyDistributionType distribution,
        bool spoil,
        IReadOnlyList<int> membersInOrder,
        Func<int, bool> eligible,
        ref int itemLastLoot,
        Random rnd)
    {
        if (membersInOrder.Count == 0)
            return sweeperObjectId;

        bool useRandom = distribution == PartyDistributionType.RandomIncludingSpoil
                         || (distribution == PartyDistributionType.Random && !spoil);
        bool useTurn = distribution == PartyDistributionType.ByTurnIncludingSpoil
                       || (distribution == PartyDistributionType.ByTurn && !spoil);

        if (useRandom)
        {
            var available = new List<int>(membersInOrder.Count);
            foreach (int id in membersInOrder)
            {
                if (eligible(id))
                    available.Add(id);
            }
            return available.Count > 0 ? available[rnd.Next(available.Count)] : sweeperObjectId;
        }

        if (useTurn)
        {
            int n = membersInOrder.Count;
            for (int i = 0; i < n; i++)
            {
                itemLastLoot++;
                if (itemLastLoot >= n)
                    itemLastLoot = 0;
                int member = membersInOrder[itemLastLoot];
                if (eligible(member))
                    return member;
            }
            return sweeperObjectId;
        }

        return sweeperObjectId;
    }

    /// <summary>Mobius <c>distributeAdena</c>: equal integer share; remainder discarded.</summary>
    public static int AdenaShare(int adena, int memberCount) =>
        memberCount <= 0 ? 0 : adena / memberCount;
}
