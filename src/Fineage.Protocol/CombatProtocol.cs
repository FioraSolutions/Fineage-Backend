namespace Fineage.Protocol;

/// <summary>
/// Combat-related <see cref="SystemMessageMsg"/> catalog ids (Mobius Interlude <c>SystemMessageId</c>).
/// Wire opcode stays <see cref="MsgType.SystemMessage"/> (1040); see <c>docs/chat-v2-protocol.md</c>.
/// </summary>
public static class CombatProtocol
{
    /// <summary><c>YOU_HIT_S1_FOR_S2_DAMAGE</c> — params: target name, damage.</summary>
    public const int HitTargetForDamage = 2264;

    /// <summary><c>YOU_HIT_FOR_S1_DAMAGE</c> — params: damage only (no target name).</summary>
    public const int HitForDamage = 2261;

    /// <summary><c>S1_HIT_YOU_FOR_S2_DAMAGE</c> (Interlude 36) — params: attacker name, damage. Victim only.</summary>
    public const int HitYouForDamage = 36;

    /// <summary><c>YOU_HAVE_AVOIDED_S1_S_ATTACK</c> (Interlude 42) — params: attacker name. Victim of a miss.</summary>
    public const int AvoidedAttack = 42;

    /// <summary><c>YOU_HAVE_MISSED</c> (Interlude 43). Attacker of a miss.</summary>
    public const int YouMissed = 43;

    /// <summary><c>CRITICAL_HIT</c> (Interlude 44). Attacker of a critical hit.</summary>
    public const int CriticalHit = 44;

    /// <summary><c>YOUR_SHIELD_DEFENSE_HAS_SUCCEEDED</c> (Interlude 111). Defender whose shield blocked.</summary>
    public const int ShieldDefenseSucceeded = 111;
}
