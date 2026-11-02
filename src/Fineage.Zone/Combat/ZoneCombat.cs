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
    /// <summary>Target drift that forces a fresh MoveToPawn during an approach (Mobius follow re-issue).</summary>
    public const int ApproachRepathDistance = 40;
    /// <summary>Tolerance at hitTime: swing lands only if attacker is still within melee range + this.</summary>
    public const int HitRangeTolerance = 40;
    /// <summary>Client MoveTo within this of the approach path is a position sync, not a cancel.</summary>
    public const int ApproachPathTolerance = 80;
    /// <summary>Fallback physical stats when a template has none (Interlude fighter base / Gremlin).</summary>
    public const int DefaultPlayerPAtk = 4;
    public const int DefaultPlayerPDef = 80;
    public const int DefaultPlayerPAtkSpd = 300;
    public const int DefaultNpcPAtkSpd = 253;
    public const int AutoAttackTickMs = 200;

    /// <summary>Fists / 1h / 2h Mobius <c>attack_range</c>.</summary>
    public const int DefaultWeaponRange = 40;
    /// <summary>Collision allowance added to the weapon range (player ~9 + NPC radius); 40 + 40 = <see cref="MeleeRange"/>.</summary>
    public const int CollisionAllowance = MeleeRange - DefaultWeaponRange;

    /// <summary>Player reach for an auto-attack with a weapon of <paramref name="weaponRange"/>.</summary>
    public static int PlayerAttackReach(int weaponRange) => Math.Max(DefaultWeaponRange, weaponRange) + CollisionAllowance;

    public const int NakedLegsPDef = 18;
    public const int MysticNakedChestPDef = 15;
    public const int MysticNakedLegsPDef = 8;

    /// <summary>Mobius PlayerTemplate naked M.Def split (sum 41): ears 9, fingers 5, neck 13.</summary>
    public static readonly (Fineage.Game.PaperdollSlot Slot, int NakedMDef)[] NakedMDefBySlot =
    [
        (Fineage.Game.PaperdollSlot.Lear, 9),
        (Fineage.Game.PaperdollSlot.Rear, 9),
        (Fineage.Game.PaperdollSlot.LFinger, 5),
        (Fineage.Game.PaperdollSlot.RFinger, 5),
        (Fineage.Game.PaperdollSlot.Neck, 13),
    ];

    /// <summary>Mobius PlayerTemplate naked P.Def split (sum 80): equipping a slot replaces its share.</summary>
    public static readonly (Fineage.Game.PaperdollSlot Slot, int NakedPDef)[] NakedPDefBySlot =
    [
        (Fineage.Game.PaperdollSlot.Chest, 31),
        (Fineage.Game.PaperdollSlot.Legs, NakedLegsPDef),
        (Fineage.Game.PaperdollSlot.Head, 12),
        (Fineage.Game.PaperdollSlot.Feet, 7),
        (Fineage.Game.PaperdollSlot.Gloves, 8),
        (Fineage.Game.PaperdollSlot.Under, 3),
        (Fineage.Game.PaperdollSlot.Cloak, 1),
    ];

    /// <summary>Gremlin (20001) Mobius <c>defence physical</c>; reference for <see cref="SampleMeleeDamage"/>.</summary>
    public const double GremlinPDef = 44.444;

    /// <summary>Reference hit: base fighter (P.Atk 4) vs Gremlin through <see cref="PhysicalDamage"/> (= 6).</summary>
    public static readonly int SampleMeleeDamage = PhysicalDamage(DefaultPlayerPAtk, GremlinPDef);

    /// <summary>Flat bonus the stub skill-damage path adds on top of skill power (was SampleMeleeDamage/3).</summary>
    public const int LegacySkillDamageBonus = 10;

    /// <summary>Mobius <c>Formulas.calculateTimeBetweenAttacks</c>: 500000 / Pspd (ms), floor 100.</summary>
    public static int AttackTimeMs(int pAtkSpd) => Math.Max(100, 500000 / Math.Max(1, pAtkSpd));

    /// <summary>Mobius <c>Creature.doAttack</c>: damage lands at timeAtk / 2.</summary>
    public static int HitTimeMs(int pAtkSpd) => AttackTimeMs(pAtkSpd) / 2;

    /// <summary>
    /// Mobius <c>Formulas.calcPhysDam</c> core (no crit/shots/random/elements yet): 70 * pAtk / pDef, min 1.
    /// </summary>
    public static int PhysicalDamage(double pAtk, double pDef) =>
        Math.Max(1, (int)(70d * Math.Max(0d, pAtk) / Math.Max(1d, pDef)));

    /// <summary>Default fighter swing (Pspd 300 → 1666 ms).</summary>
    public static readonly int AutoAttackIntervalMs = AttackTimeMs(DefaultPlayerPAtkSpd);

    /// <summary>Mobius default NPC Pspd 253 → 1976 ms per swing.</summary>
    public static readonly int MobAttackIntervalMs = AttackTimeMs(DefaultNpcPAtkSpd);
    public const int AggroForgetRange = 3000;

    /// <summary>Mobius melee hitTime = attack time / 2: damage lands mid-swing (client AttackShot), not on packet.</summary>
    public static readonly int PlayerMeleeHitDelayMs = HitTimeMs(DefaultPlayerPAtkSpd);
    public static readonly int MobMeleeHitDelayMs = HitTimeMs(DefaultNpcPAtkSpd);

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

    public static bool Within(int ax, int ay, int bx, int by, int range)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)range * range;
    }

    /// <summary>Distance from point P to segment AB (approach path).</summary>
    public static double DistanceToSegment(int px, int py, int ax, int ay, int bx, int by)
    {
        double vx = bx - ax, vy = by - ay;
        double len2 = vx * vx + vy * vy;
        double t = len2 <= 0 ? 0 : Math.Clamp(((px - ax) * vx + (py - ay) * vy) / len2, 0, 1);
        double cx = ax + t * vx - px, cy = ay + t * vy - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }
}
