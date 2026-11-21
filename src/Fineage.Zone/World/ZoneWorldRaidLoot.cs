using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
using Fineage.Zone.Party;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius raid death loot owner: top-damage party leader in <see cref="RaidLootRules.PartyRange"/>.</summary>
    internal static int RaidLootOwnerObjectId(
        ZoneEntity npc,
        ZoneEntity? killer,
        Func<int, ZoneEntity?> resolve,
        Func<int, ZoneParty?> partyOf) =>
        RaidLootRules.LootOwnerObjectId(npc, killer, resolve, partyOf);
}

internal static class RaidLootRules
{
    /// <summary>Mobius <c>ALT_PARTY_RANGE</c> (same as quest kill credit).</summary>
    public const int PartyRange = 1600;

    public static int LootOwnerObjectId(
        ZoneEntity npc,
        ZoneEntity? killer,
        Func<int, ZoneEntity?> resolve,
        Func<int, ZoneParty?> partyOf)
    {
        var partyTotals = new Dictionary<int, long>();
        long bestSolo = 0;
        int bestSoloId = 0;

        foreach (var (attackerId, dealt) in npc.DamageBy)
        {
            if (dealt <= 1
                || resolve(attackerId) is not { } attacker
                || attacker.Kind != ZoneEntityKind.Player
                || attacker.Session == null
                || attacker.Dead)
                continue;
            if (!ZoneCombat.Within(npc.X, npc.Y, attacker.X, attacker.Y, PartyRange))
                continue;

            var party = partyOf(attackerId);
            if (party == null)
            {
                if (dealt > bestSolo)
                {
                    bestSolo = dealt;
                    bestSoloId = attackerId;
                }

                continue;
            }

            int leader = party.LeaderObjectId;
            partyTotals[leader] = partyTotals.GetValueOrDefault(leader) + dealt;
        }

        if (partyTotals.Count > 0)
        {
            int topLeader = 0;
            long top = 0;
            foreach (var (leader, total) in partyTotals)
            {
                if (total > top)
                {
                    top = total;
                    topLeader = leader;
                }
            }

            if (topLeader != 0)
                return topLeader;
        }

        if (bestSoloId != 0)
            return bestSoloId;
        return killer?.Session != null ? killer.ObjectId : 0;
    }
}