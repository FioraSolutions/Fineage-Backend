namespace Fineage.Zone.Combat;

/// <summary>
/// Pre-CP redirects from Mobius CT0 <c>PlayerStatus.reduceHp</c> and <c>SummonStatus.reduceHp</c>.
/// Integer arithmetic matches the Java casts: <c>((int)amount * (int)percent) / 100</c>.
/// </summary>
/// <param name="ToServitor">Transfer Pain amount already diverted to the servitor.</param>
/// <param name="MpLoss">Arcane Shield MP spent (cost, or all remaining MP when the shield breaks).</param>
/// <param name="ShieldAbsorbed">Shield paid the cost from MP and Mobius returns (no party transfer, no CP/HP).</param>
/// <param name="ShieldBroken">Shield could not pay the cost; leftover continues down the pipe.</param>
/// <param name="ToPartyCaster">Raw <c>min(casterHp-1, amount*pct/100)</c> before the reduceCp mutation.</param>
/// <param name="PartyShare">Amount handed to <c>reduceCurrentHp</c> on the caster (post-mutation / membersInRange).</param>
/// <param name="Remaining">What the original target still takes after redirects.</param>
/// <param name="CpOnCaster">Amount of the caster's CP removed by the first <c>reduceCp</c> (playable attacker only).</param>
public readonly record struct IncomingDamagePlan(
    int ToServitor,
    int MpLoss,
    bool ShieldAbsorbed,
    bool ShieldBroken,
    int ToPartyCaster,
    int PartyShare,
    int Remaining,
    int CpOnCaster = 0);

public static class IncomingDamage
{
    public const int TransferRange = 1000;
    public const int ArcaneShieldSkillId = 1556;

    /// <summary>
    /// Player incoming path, in Mobius order: Transfer Pain to a servitor, Arcane Shield MP,
    /// then transfer to a designated party caster. A fully absorbing shield returns before the
    /// party transfer. <paramref name="servitorHp"/> is 0 when no in-range servitor is present.
    /// <paramref name="membersInRange"/> is the Mobius count of other party members within 1000
    /// of the caster (excluding the caster). When it is 0 the HP share and the deduction from the
    /// original target do not fire, but a playable attacker still spends caster CP.
    /// </summary>
    public static IncomingDamagePlan PlanPlayer(
        int amount,
        int transDamPercent,
        int servitorHp,
        int manaShieldPercent,
        int currentMp,
        int transDamToPlayerPercent,
        int casterHp,
        int membersInRange,
        int casterCp = 0,
        bool absorbCp = true)
    {
        int remaining = Math.Max(0, amount);
        int toServitor = 0;
        if (servitorHp > 1 && transDamPercent > 0)
        {
            toServitor = Math.Min(servitorHp - 1, (remaining * transDamPercent) / 100);
            if (toServitor > 0)
                remaining -= toServitor;
            else
                toServitor = 0;
        }

        int mpLoss = 0;
        bool broken = false;
        if (manaShieldPercent > 0)
        {
            int raw = (remaining * manaShieldPercent) / 100;
            if (raw > 0)
            {
                int cost = remaining - raw;
                if (cost > currentMp)
                {
                    broken = true;
                    mpLoss = Math.Max(0, currentMp);
                    remaining = cost - mpLoss;
                }
                else
                {
                    return new IncomingDamagePlan(toServitor, cost, true, false, 0, 0, 0);
                }
            }
        }

        var transfer = PlanTransferToPlayer(
            remaining, transDamToPlayerPercent, casterHp, membersInRange, partyPresent: true, casterCp, absorbCp);
        return new IncomingDamagePlan(
            toServitor, mpLoss, false, broken, transfer.Raw, transfer.Share, transfer.Remaining, transfer.CpOnCaster);
    }

    /// <summary>
    /// Summon incoming path from Mobius <c>SummonStatus.reduceHp</c>: transfer to the designated
    /// player (party members split when a party is present; otherwise the whole amount can go to
    /// the owner). There is no servitor-to-servitor Transfer Pain and no Arcane Shield here.
    /// </summary>
    public static IncomingDamagePlan PlanSummon(
        int amount,
        int transDamToPlayerPercent,
        int casterHp,
        int membersInRange,
        bool partyPresent,
        int casterCp = 0,
        bool absorbCp = true)
    {
        int remaining = Math.Max(0, amount);
        var transfer = PlanTransferToPlayer(
            remaining, transDamToPlayerPercent, casterHp, membersInRange, partyPresent, casterCp, absorbCp);
        return new IncomingDamagePlan(0, 0, false, false, transfer.Raw, transfer.Share, transfer.Remaining, transfer.CpOnCaster);
    }

    /// <summary>
    /// Shared Mobius transfer block: compute raw transfer, optionally spend caster CP (which
    /// mutates the amount when CP does not cover it), then apply the share and deduct from the
    /// original target only when the share actually fires.
    /// </summary>
    static (int Raw, int Share, int Remaining, int CpOnCaster) PlanTransferToPlayer(
        int remaining,
        int percent,
        int casterHp,
        int membersInRange,
        bool partyPresent,
        int casterCp,
        bool absorbCp)
    {
        if (percent <= 0 || casterHp <= 1)
            return (0, 0, remaining, 0);

        int raw = Math.Min(casterHp - 1, (remaining * percent) / 100);
        if (raw <= 0)
            return (0, 0, remaining, 0);

        int transferDmg = raw;
        int cpOnCaster = 0;
        if (absorbCp && casterCp > 0)
        {
            if (casterCp > transferDmg)
            {
                cpOnCaster = transferDmg;
            }
            else
            {
                cpOnCaster = casterCp;
                transferDmg -= casterCp;
            }
        }

        if (partyPresent)
        {
            if (membersInRange <= 0)
                return (raw, 0, remaining, cpOnCaster);
            return (raw, transferDmg / membersInRange, remaining - transferDmg, cpOnCaster);
        }

        return (raw, transferDmg, remaining - transferDmg, cpOnCaster);
    }

    public static bool InRange3d(int x1, int y1, int z1, int x2, int y2, int z2, int range = TransferRange)
    {
        long dx = x1 - (long)x2, dy = y1 - (long)y2, dz = z1 - (long)z2, limit = (long)range * range;
        return dx * dx + dy * dy + dz * dz <= limit;
    }

    public static bool InRange2d(int x1, int y1, int x2, int y2, int range = TransferRange)
    {
        long dx = x1 - (long)x2, dy = y1 - (long)y2, limit = (long)range * range;
        return dx * dx + dy * dy <= limit;
    }
}
