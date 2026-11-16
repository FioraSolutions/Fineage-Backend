using Fineage.Protocol;

namespace Fineage.Zone.Combat;

/// <summary>System chat lines for combat (attacker-only fan-out).</summary>
public static class CombatLines
{
    public static SystemMessageMsg HitTarget(string? targetName, int damage)
    {
        string name = string.IsNullOrWhiteSpace(targetName) ? "target" : targetName.Trim();
        string amount = damage.ToString();
        return new SystemMessageMsg
        {
            Id = CombatProtocol.HitTargetForDamage,
            Params = [name, amount],
            Text = $"You hit {name} for {amount} damage."
        };
    }

    /// <summary>Mobius <c>S1_HIT_YOU_FOR_S2_DAMAGE</c> (36): victim only, any non-DOT damage (PlayerStatus.reduceHp).</summary>
    public static SystemMessageMsg HitYou(string? attackerName, int damage)
    {
        string name = string.IsNullOrWhiteSpace(attackerName) ? "Someone" : attackerName.Trim();
        string amount = damage.ToString();
        return new SystemMessageMsg { Id = CombatProtocol.HitYouForDamage, Params = [name, amount], Text = $"{name} hit you for {amount} damage." };
    }

    /// <summary>Mobius <c>YOU_HAVE_AVOIDED_S1_S_ATTACK</c> (42): victim of a missed swing (Creature.sendDamageMessage).</summary>
    public static SystemMessageMsg AvoidedAttack(string? attackerName)
    {
        string name = string.IsNullOrWhiteSpace(attackerName) ? "Someone" : attackerName.Trim();
        return new SystemMessageMsg { Id = CombatProtocol.AvoidedAttack, Params = [name], Text = $"You have avoided {name}'s attack." };
    }

    /// <summary>Mobius <c>YOU_HAVE_MISSED</c> (43): attacker of a missed swing (Player.sendDamageMessage).</summary>
    public static SystemMessageMsg Missed() => new() { Id = CombatProtocol.YouMissed, Params = [], Text = "You have missed." };

    /// <summary>Mobius <c>CRITICAL_HIT</c> (44): attacker of a critical swing.</summary>
    public static SystemMessageMsg CriticalHit() => new() { Id = CombatProtocol.CriticalHit, Params = [], Text = "Critical hit!" };

    /// <summary>Mobius <c>YOUR_SHIELD_DEFENSE_HAS_SUCCEEDED</c> (111): defender whose shield blocked (Formulas.calcShldUse).</summary>
    public static SystemMessageMsg ShieldDefense() => new() { Id = CombatProtocol.ShieldDefenseSucceeded, Params = [], Text = "Your shield defense has succeeded." };
}
