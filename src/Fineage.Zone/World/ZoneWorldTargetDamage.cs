using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    static bool AttackerAbsorbsCp(ZoneEntity? attacker, ZoneEntity target) =>
        attacker is not null &&
        attacker.ObjectId != target.ObjectId &&
        (attacker.Kind == ZoneEntityKind.Player || attacker.Summon != null);

    /// <summary>
    /// Apply one damage amount to <paramref name="target"/> and return the actual CP/HP loss on that
    /// target. Player incoming damage first follows Mobius <c>PlayerStatus.reduceHp</c> redirects
    /// (Transfer Pain, Arcane Shield, transfer to a party caster) and only then the CP barrier.
    /// Summon incoming damage follows <c>SummonStatus.reduceHp</c>. Healing, death/respawn,
    /// max-vital setup and admin writes never call here.
    /// </summary>
    internal DamageSplit ApplyTargetDamage(ZoneEntity target, ZoneEntity? attacker, int damage, bool ignoreCp = false)
    {
        if (damage <= 0 || target.Dead)
            return DamageSplit.None;

        if (attacker is not null && attacker.ObjectId != target.ObjectId)
        {
            if (target.Kind == ZoneEntityKind.Player)
                damage = ApplyPlayerRedirects(target, attacker, damage);
            else if (target.Summon is not null)
                damage = ApplySummonRedirects(target, attacker, damage);

            if (damage <= 0)
                return DamageSplit.None;
        }

        bool cpAbsorbs = target.Kind == ZoneEntityKind.Player && AttackerAbsorbsCp(attacker, target);
        var split = TargetDamage.Split(damage, target.CurCp, target.CurHp, cpAbsorbs, ignoreCp);
        if (split.CpLoss > 0)
            target.CurCp = Math.Max(0, target.CurCp - split.CpLoss);
        if (split.HpLoss > 0)
            target.CurHp = Math.Max(0, target.CurHp - split.HpLoss);
        return split;
    }

    int ApplyPlayerRedirects(ZoneEntity target, ZoneEntity attacker, int damage)
    {
        var stats = PlayerStats(target);
        ZoneEntity? servitor = ServitorOf(target);
        bool servitorInRange = servitor is not null &&
            IncomingDamage.InRange3d(target.X, target.Y, target.Z, servitor.X, servitor.Y, servitor.Z);
        ZoneEntity? caster = EligiblePlayerTransferCaster(target);
        int members = caster is null ? 0 : PartyMembersInRangeOfCaster(caster, includeZ: false);

        var plan = IncomingDamage.PlanPlayer(
            damage,
            (int)stats.TransDam,
            servitorInRange ? servitor!.CurHp : 0,
            (int)stats.ManaShield,
            target.CurMp,
            (int)stats.TransDamToPlayer,
            caster is { Dead: false } ? caster.CurHp : 0,
            members,
            caster is { Dead: false } ? caster.CurCp : 0,
            caster is not null && AttackerAbsorbsCp(attacker, caster));

        if (plan.ToServitor > 0 && servitor is not null)
            ApplyTargetDamage(servitor, attacker, plan.ToServitor);

        if (plan.MpLoss > 0 || plan.ShieldBroken || plan.ShieldAbsorbed)
        {
            target.CurMp = Math.Max(0, target.CurMp - plan.MpLoss);
            if (plan.ShieldBroken)
                ZoneAbnormals.RemoveBySkillId(target.Abnormals, IncomingDamage.ArcaneShieldSkillId);
        }

        if (plan.ShieldAbsorbed)
            return 0;

        if ((plan.CpOnCaster > 0 || plan.PartyShare > 0) && caster is not null)
            ApplyTransferredToCaster(caster, attacker, plan);

        return plan.Remaining;
    }

    int ApplySummonRedirects(ZoneEntity summon, ZoneEntity attacker, int damage)
    {
        ZoneEntity? owner = OwnerOf(summon);
        // Mobius only transfers when getTransferingDamageTo() is set; there is no implicit owner fallback.
        ZoneEntity? caster = TransferCasterOf(summon);
        bool partyPresent = owner is not null && PartyMembersOf(owner).Count > 1;
        if (partyPresent && caster is not null && owner is not null)
        {
            bool sameParty = PartyMembersOf(owner).Exists(m => m.ObjectId == caster.ObjectId);
            if (!sameParty)
                caster = null;
        }
        else if (!partyPresent && caster is not null && owner is not null && caster.ObjectId != owner.ObjectId)
        {
            caster = null;
        }

        if (caster is not null &&
            !IncomingDamage.InRange3d(summon.X, summon.Y, summon.Z, caster.X, caster.Y, caster.Z))
        {
            caster = null;
        }

        int percent = (int)StatMods(summon).Apply(CombatStat.TransDamToPlayer, 0d);
        int members = partyPresent && caster is not null ? PartyMembersInRangeOfCaster(caster, includeZ: false) : 0;
        var plan = IncomingDamage.PlanSummon(
            damage,
            percent,
            caster is { Dead: false } ? caster.CurHp : 0,
            members,
            partyPresent,
            caster is { Dead: false } ? caster.CurCp : 0,
            caster is not null && AttackerAbsorbsCp(attacker, caster));

        if ((plan.CpOnCaster > 0 || plan.PartyShare > 0) && caster is not null)
            ApplyTransferredToCaster(caster, attacker, plan);

        return plan.Remaining;
    }

    void ApplyTransferredToCaster(ZoneEntity caster, ZoneEntity attacker, IncomingDamagePlan plan)
    {
        // reduceCp (and the transferDmg mutation) already ran inside the planner; just honour the results.
        if (plan.CpOnCaster > 0)
            ReduceCp(caster, plan.CpOnCaster);
        if (plan.PartyShare > 0)
            ApplyTargetDamage(caster, attacker, plan.PartyShare);
    }


    ZoneEntity? EligiblePlayerTransferCaster(ZoneEntity target)
    {
        ZoneEntity? caster = TransferCasterOf(target);
        if (caster is null)
            return null;
        if (!IncomingDamage.InRange3d(target.X, target.Y, target.Z, caster.X, caster.Y, caster.Z))
            return null;
        if (!PartyMembersOf(target).Exists(member => member.ObjectId == caster.ObjectId))
            return null;
        return caster;
    }

    ZoneEntity? ServitorOf(ZoneEntity player)
    {
        var summoned = SummonOf(player);
        return summoned?.Summon is { Kind: SummonKindNet.Servitor } ? summoned : null;
    }

    ZoneEntity? TransferCasterOf(ZoneEntity source)
    {
        if (source.TransferDamageToObjectId <= 0)
            return null;
        return _byId.TryGetValue(source.TransferDamageToObjectId, out var caster) &&
               caster.Kind == ZoneEntityKind.Player &&
               !caster.Dead &&
               caster.ObjectId != source.ObjectId
            ? caster
            : null;
    }

    int PartyMembersInRangeOfCaster(ZoneEntity caster, bool includeZ)
    {
        int count = 0;
        foreach (var member in PartyMembersOf(caster))
        {
            if (member.ObjectId == caster.ObjectId)
                continue;
            bool inRange = includeZ
                ? IncomingDamage.InRange3d(member.X, member.Y, member.Z, caster.X, caster.Y, caster.Z)
                : IncomingDamage.InRange2d(member.X, member.Y, caster.X, caster.Y);
            if (inRange)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Mobius <c>PlayerStatus.reduceCp</c>: a flat CP-only reduction used by transferred damage and CP-drain effects.
    /// Returns the amount actually removed (clamped at 0 CP).
    /// </summary>
    internal static int ReduceCp(ZoneEntity target, int value)
    {
        if (value <= 0 || target.Dead)
            return 0;
        int before = target.CurCp;
        if (before <= 0)
            return 0;
        target.CurCp = before > value ? before - value : 0;
        return before - target.CurCp;
    }

    internal DamageSplit ApplyTargetDamageForTest(ZoneEntity target, ZoneEntity? attacker, int damage, bool ignoreCp = false) =>
        ApplyTargetDamage(target, attacker, damage, ignoreCp);

    internal DamageSplit ApplySkillDamageToPlayerForTest(ZoneEntity attacker, ZoneEntity target, int damage, long now) =>
        ApplySkillDamageToPlayer(attacker, target, damage, now);

    internal static int ReduceCpForTest(ZoneEntity target, int value) => ReduceCp(target, value);
}
