namespace Fineage.Zone.Combat;

/// <summary>Minimal melee constants — not full Interlude formulas.</summary>
public static class ZoneCombat
{
    /// <summary>
    /// Interlude player physical attack range: weapon atkRange 40 (fists/1h/2h) + collision radii
    /// (player ~9 + NPC ~10-25). Mobius checks <c>isInsideRadius(target, physRange + collisions)</c>;
    /// matches <see cref="MobMeleeRange"/>. Out of range the server walks the player in (moveToPawn).
    /// </summary>
    public const int MeleeRange = 80;
    /// <summary>Ground pickup / interaction reach — client walks there itself, keep the old wide gate.</summary>
    public const int PickupRange = 900;
    /// <summary>Mobius moveToPawn offset: stop a bit inside the attack range so the swing passes the check.</summary>
    public const int ApproachSlack = 20;
    /// <summary>Fallback player run speed when the character has none (Interlude human fighter 115 → ~120).</summary>
    public const float DefaultPlayerRunSpeed = 120f;
    public const int SampleMeleeDamage = 32;
    public const int AutoAttackIntervalMs = 1100;
    public const int AutoAttackTickMs = 200;

    /// <summary>Mobius default NPC Pspd 253 -> ~1.2s per swing.</summary>
    public const int MobAttackIntervalMs = 1200;
    public const int AggroForgetRange = 3000;

    /// <summary>Mobius melee hitTime = attack time / 2: damage lands mid-swing (client AttackShot), not on packet.</summary>
    public const int PlayerMeleeHitDelayMs = AutoAttackIntervalMs / 2;
    public const int MobMeleeHitDelayMs = MobAttackIntervalMs / 2;

    /// <summary>Mobius NPC physical attack range 40 + collision radii (~gremlin/player). Mob walks in until this.</summary>
    public const int MobMeleeRange = 80;
    /// <summary>Mobius <c>NpcConfig.DEFAULT_CORPSE_TIME</c> = 7 s: corpse lies (death anim) before DeleteObject.</summary>
    public const int CorpseDecayMs = 7000;
    /// <summary>Fallback NPC run speed when the template has none.</summary>
    public const float DefaultNpcRunSpeed = 120f;

    public static bool InMobMeleeRange(int ax, int ay, int bx, int by)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)MobMeleeRange * MobMeleeRange;
    }

    /// <summary>Placeholder until NPC pAtk/pDef formulas are imported: scales with level.</summary>
    public static int MobMeleeDamage(int level) => Math.Max(1, 4 + level * 2);

    public static bool InAggroRange(int ax, int ay, int bx, int by)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)AggroForgetRange * AggroForgetRange;
    }

    public static bool InMeleeRange(int ax, int ay, int bx, int by)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)MeleeRange * MeleeRange;
    }

    public static bool InPickupRange(int ax, int ay, int bx, int by)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)PickupRange * PickupRange;
    }

    public static bool InCastRange(int ax, int ay, int bx, int by, int castRange)
    {
        int range = Math.Max(40, castRange);
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)range * range;
    }
}
