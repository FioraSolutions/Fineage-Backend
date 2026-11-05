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
}
