using Fineage.Game;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// The one target-damage application path. It is Mobius <c>PlayerStatus.reduceHp</c>'s CP barrier plus the
/// final HP write, shared by melee, skills, DOT, reflect, channel ticks, environment damage and summons.
/// <para>
/// CP only exists on a player and only absorbs when the credited attacker is playable (player or summon) and the
/// caller did not opt out. NPC attackers, missing attackers and HP-consumption/special effects bypass CP exactly
/// like CT0. Callers keep their own gates (invulnerable, duel, fake death) and fan-out: this function changes
/// vitals only and returns the actual losses so reflect/absorb/drain can use what really landed.
/// </para>
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius <c>!ignoreCP &amp;&amp; (attacker.isPlayable() || attacker.isFakePlayer())</c> for the attacker model this
    /// zone carries. A summon <see cref="ZoneEntity.Summon"/> is playable; the rider is its owner's credit path.
    /// </summary>
    internal static bool AttackerAbsorbsCp(ZoneEntity? attacker, ZoneEntity target) =>
        attacker != null && !ReferenceEquals(attacker, target) &&
        (attacker.Kind == ZoneEntityKind.Player || attacker.Summon != null);

    /// <summary>
    /// Apply one damage amount to <paramref name="target"/> (CP first for players, then HP) and return the actual
    /// CP/HP loss. No CP is applied to healing, death/respawn, max-vital setup or admin writes — those never call here.
    /// </summary>
    internal DamageSplit ApplyTargetDamage(ZoneEntity target, ZoneEntity? attacker, int damage, bool ignoreCp = false)
    {
        if (damage <= 0 || target.Dead)
            return DamageSplit.None;

        bool cpAbsorbs = target.Kind == ZoneEntityKind.Player && AttackerAbsorbsCp(attacker, target);
        var split = TargetDamage.Split(damage, target.CurCp, target.CurHp, cpAbsorbs, ignoreCp);
        if (split.CpLoss > 0)
            target.CurCp = Math.Max(0, target.CurCp - split.CpLoss);
        if (split.HpLoss > 0)
            target.CurHp = Math.Max(0, target.CurHp - split.HpLoss);
        return split;
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
