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
    /// <summary>Interlude Attack hit flags (client Hit.cs): 0x20 crit, 0x80 miss.</summary>
    public const byte HitFlagCrit = 0x20;
    public const byte HitFlagMiss = 0x80;
    /// <summary>Interlude HITFLAG_USESS (0x10) | soulshot grade in the low bits.</summary>
    public const byte HitFlagSoulshot = 0x10;
    /// <summary>Interlude HITFLAG_SHLD: target blocked with a shield.</summary>
    public const byte HitFlagShield = 0x40;
    /// <summary>Mobius Config.ALT_PERFECT_SHLD_BLOCK (%).</summary>
    public const int PerfectShieldBlockPercent = 5;

    public enum ShieldResult { None, Block, Perfect }

    /// <summary>
    /// Mobius <c>Formulas.calcShldUse</c> (no facing-angle check yet): rate = rShld · DEX bonus (×1.3 vs bows);
    /// perfect block when Rnd(100) &gt; 100 − 5, else block when rate &gt; Rnd(100).
    /// </summary>
    public static ShieldResult RollShield(Random rnd, double shieldRate, bool attackerBow)
    {
        if (shieldRate <= 1e-6)
            return ShieldResult.None;
        if (attackerBow)
            shieldRate *= 1.3;
        if (100 - PerfectShieldBlockPercent < rnd.Next(100))
            return ShieldResult.Perfect;
        return shieldRate > rnd.Next(100) ? ShieldResult.Block : ShieldResult.None;
    }
    /// <summary>Mobius crit rate cap (MAX_PCRIT_RATE 500 ‰).</summary>
    public const int MaxCritPermille = 500;
    /// <summary>Mobius <c>baseRndDam</c> for every player template (fists / weapons without random_damage).</summary>
    public const int DefaultPlayerRandomDamage = 10;

    /// <summary>Mobius <c>Formulas.calcHitMiss</c>: (80 + 2·(acc − eva))·10 ‰, clamped 200..980.</summary>
    public static int HitChancePermille(double accuracy, double evasion) =>
        Math.Clamp((int)((80 + 2 * (accuracy - evasion)) * 10), 200, 980);

    /// <summary>Mobius FuncAtkAccuracy / FuncAtkEvasion: base + sqrt(DEX)·6 + level.</summary>
    public static double AccuracyOrEvasion(double baseValue, int dex, int level) =>
        baseValue + Math.Sqrt(Math.Max(0, dex)) * 6 + Math.Max(1, level);

    /// <summary>
    /// One melee swing (Mobius doAttack → calcHitMiss, calcCrit, calcPhysDam): miss → (0, Miss);
    /// crit doubles; damage × (1 + Rnd[−random, random] / 100).
    /// </summary>
    public static (int Damage, byte Flags) RollMelee(Random rnd, double pAtk, double pDef, double accuracy, double evasion,
        int critPermille, int randomPercent) =>
        RollMelee(rnd, pAtk, pDef, accuracy, evasion, critPermille, randomPercent, false, 0, 0, 0, false);

    /// <summary>
    /// Full swing: miss → shield → crit → calcPhysDam. Soulshot doubles P.Atk (flag 0x10 | grade); shield block
    /// adds sDef to P.Def, perfect block deals 1 (flag 0x40).
    /// </summary>
    public static (int Damage, byte Flags) RollMelee(Random rnd, double pAtk, double pDef, double accuracy, double evasion,
        int critPermille, int randomPercent, bool soulshot, int shotGrade, double shieldRate, int shieldDef, bool attackerBow)
    {
        byte ssFlags = soulshot ? (byte)(HitFlagSoulshot | (shotGrade & 0x0F)) : (byte)0;
        if (rnd.Next(1000) >= HitChancePermille(accuracy, evasion))
            return (0, (byte)(HitFlagMiss | ssFlags));
        var shield = RollShield(rnd, shieldRate, attackerBow);
        byte flags = (byte)(ssFlags | (shield != ShieldResult.None ? HitFlagShield : 0));
        if (shield == ShieldResult.Perfect)
            return (1, flags);
        bool crit = Math.Clamp(critPermille, 0, MaxCritPermille) > rnd.Next(1000);
        if (soulshot)
            pAtk *= 2;
        if (shield == ShieldResult.Block)
            pDef += shieldDef;
        double damage = 70d * Math.Max(0d, pAtk) / Math.Max(1d, pDef);
        if (crit)
            damage *= 2;
        if (randomPercent > 0)
            damage *= 1 + rnd.Next(-randomPercent, randomPercent + 1) / 100d;
        return (Math.Max(1, (int)damage), (byte)(flags | (crit ? HitFlagCrit : 0)));
    }

    /// <summary>Mobius <c>Formulas.calcAtkSpd</c> for skills: magic time·333/Cspd, physical time·300/Pspd.</summary>
    public static int SkillTimeMs(int baseMs, bool magic, int mAtkSpd, int pAtkSpd) =>
        baseMs <= 0 ? 0 : (int)(magic ? baseMs * 333d / Math.Max(1, mAtkSpd) : baseMs * 300d / Math.Max(1, pAtkSpd));

    /// <summary>Interlude reuse scaling: reuse·333 / (magic ? Cspd : Pspd).</summary>
    public static int SkillReuseMs(int baseMs, bool magic, int mAtkSpd, int pAtkSpd) =>
        baseMs <= 0 ? 0 : (int)(baseMs * 333d / Math.Max(1, magic ? mAtkSpd : pAtkSpd));

    /// <summary>Interlude <c>calcPhysDam</c> with skill: 70·(pAtk·(ss?2:1) + power) / (pDef [+ sDef]).</summary>
    public static int PhysicalSkillDamage(double pAtk, double pDef, double power, bool soulshot) =>
        Math.Max(1, (int)(70d * (Math.Max(0, pAtk) * (soulshot ? 2 : 1) + Math.Max(0, power)) / Math.Max(1d, pDef)));

    /// <summary>
    /// Mobius <c>CreatureStat.getMCriticalHit</c>: (int)(MCRITICAL_RATE 1 · WIT bonus) · 10 ‰; FuncMAtkCritical applies
    /// WIT only for NPCs or armed players (unarmed player stays 10 ‰).
    /// </summary>
    public static int PlayerMCritRate(bool armed, double witBonus) => (int)(armed ? witBonus : 1d) * 10;

    /// <summary>Mobius <c>calcMCrit</c>: rate ‰ &gt; Rnd(1000).</summary>
    public static bool RollMCrit(Random rnd, int rate) => rate > rnd.Next(1000);

    /// <summary>
    /// Mobius <c>calcMagicSuccess</c> (MAGIC_SUCCESS_RES 1): rate = 100 − round(1.3^(targetLvl − magicLvl)); success when Rnd(100) &lt; rate.
    /// </summary>
    public static bool RollMagicSuccess(Random rnd, int targetLevel, int magicLevel) =>
        rnd.Next(100) < 100 - (int)Math.Round((float)Math.Pow(1.3, targetLevel - magicLevel), MidpointRounding.AwayFromZero);

    public enum MagicOutcome { Normal, Critical, HalfFail, Resisted }

    /// <summary>
    /// Mobius <c>calcMagicDam</c> failure / crit branch for a player caster: failed first roll → second roll succeeds and
    /// level gap ≤ 9 → half damage ("Your attack has failed"), else resisted → 1 damage; otherwise mcrit ×3 vs NPC (2.5 PvP).
    /// </summary>
    public static (int Damage, MagicOutcome Outcome) RollMagicDamage(Random rnd, double mAtk, double mDef, double power,
        int casterLevel, int magicLevel, int targetLevel, int mCritRate, bool targetIsPlayer = false)
    {
        // Mobius rolls mcrit before calcMagicDam (MagicalAttack effect), then the failure check.
        bool mcrit = RollMCrit(rnd, mCritRate);
        double damage = 91d * Math.Sqrt(Math.Max(0, mAtk)) / Math.Max(1d, mDef) * Math.Max(0, power);
        var outcome = MagicOutcome.Normal;
        if (!RollMagicSuccess(rnd, targetLevel, magicLevel))
        {
            if (RollMagicSuccess(rnd, targetLevel, magicLevel) && targetLevel - casterLevel <= 9)
            {
                damage /= 2;
                outcome = MagicOutcome.HalfFail;
            }
            else
            {
                damage = 1;
                outcome = MagicOutcome.Resisted;
            }
        }
        else if (mcrit)
        {
            damage *= targetIsPlayer ? 2.5 : 3;
            outcome = MagicOutcome.Critical;
        }
        return (Math.Max(1, (int)damage), outcome);
    }

    /// <summary>Interlude <c>calcMagicDam</c> core: 91·√mAtk / mDef · power (no fail / crit).</summary>
    /// <summary>Mobius calcMagicDam M.Atk multiplier: blessed spiritshot ×4, spiritshot ×2.</summary>
    public static double SpiritshotMAtkMultiplier(int charge) => charge >= 2 ? 4d : charge == 1 ? 2d : 1d;

    public static int MagicDamage(double mAtk, double mDef, double power) =>
        Math.Max(1, (int)(91d * Math.Sqrt(Math.Max(0, mAtk)) / Math.Max(1d, mDef) * Math.Max(0, power)));

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

    public static bool InMobMeleeRange(int ax, int ay, int bx, int by) => InMobReach(ax, ay, bx, by, MobMeleeRange);

    /// <summary>NPC reach for its template atkRange (Mobius physRange + collisions): max(40, range) + collision allowance.</summary>
    public static int MobAttackReach(int attackRange) => PlayerAttackReach(attackRange);

    public static bool InMobReach(int ax, int ay, int bx, int by, int reach)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)reach * reach;
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
