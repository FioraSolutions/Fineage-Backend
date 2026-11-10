namespace Fineage.Zone.Combat;

/// <summary>Server-side abnormal with absolute expiry. Wire rows are derived at fan-out time.</summary>
public sealed class ZoneAbnormalEntry
{
    public int Id { get; set; }
    public int Value { get; set; }
    /// <summary>Skill level that applied the abnormal (0 = unknown → derive from <see cref="Value"/>).</summary>
    public int SkillLevel { get; set; }
    /// <summary>
    /// Mobius <c>abnormalType</c> slot (POISON, BLEEDING, STUN, SPEED_UP …). Antidote/Bandage dispel by slot, so the
    /// antidote needs the same string Mobius compares. Empty when the skill carries no type.
    /// </summary>
    public string Slot { get; set; } = "";
    public long ExpiresAtUnixMs { get; set; }
    /// <summary>Next DamOverTime tick (unix ms). 0 = fire on the first abnormal tick.</summary>
    public long NextDotAtUnixMs { get; set; }
}
