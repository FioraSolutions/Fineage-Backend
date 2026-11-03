namespace Fineage.Game.Combat;

/// <summary>Mobius CT0 cast interruption rules (Formulas.calcAtkBreak, Creature.breakCast / onMagicLaunchedTimer).</summary>
public static class CastRules
{
    /// <summary>System message "Your casting has been interrupted." (SystemMessageId.CASTING_INTERRUPTED).</summary>
    public const int CastingInterruptedMessage = 27;

    /// <summary>
    /// Mobius calcAtkBreak with AltGameCancelByHit=Cast: casting → init 15, + √(13·dmg), − (MEN bonus·100 − 100),
    /// clamped to 1..99 %. Zero damage never breaks.
    /// </summary>
    public static double AttackBreakRate(double damage, double menBonus)
    {
        double init = 15 + Math.Sqrt(13 * Math.Max(0, damage)) - (menBonus * 100 - 100);
        return Math.Clamp(init, 1, 99);
    }

    public static bool RollAttackBreak(Random rnd, double damage, double menBonus) =>
        damage > 0 && rnd.Next(100) < AttackBreakRate(damage, menBonus);

    /// <summary>Creature.breakCast: only magic and static skills are interruptible; physical skills keep going.</summary>
    public static bool Interruptible(bool magic) => magic;

    /// <summary>Creature.breakCast: <c>isMagic() || isStatic()</c> (isMagic 1 or 2).</summary>
    public static bool Interruptible(int magicType) => magicType is 1 or 2;

    /// <summary>onMagicLaunchedTimer escape range: effectRange when set, else castRange (≤ 0 = no range check).</summary>
    public static int EscapeRange(int castRange, int effectRange) => effectRange > 0 ? effectRange : castRange;
}
