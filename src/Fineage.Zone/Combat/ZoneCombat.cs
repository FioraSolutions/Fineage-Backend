using Fineage.Game;

namespace Fineage.Zone.Combat;

/// <summary>
/// Interlude combat constants and pure roll helpers (melee/magic/blow/lethal, ranges).
/// Split across partials: this file holds constants / naked slots / swing timing;
/// <c>ZoneCombat.Ranges</c> and <c>ZoneCombat.Rolls</c> hold geometry and damage rolls.
/// World orchestration stays on <c>ZoneWorld*</c>.
/// </summary>
public static partial class ZoneCombat
{
    /// <summary>
    /// Interlude player physical attack range: weapon atkRange 40 (fists/1h/2h) + collision radii
    /// (player ~9 + NPC ~10-25). Mobius checks <c>isInsideRadius(target, physRange + collisions)</c>;
    /// matches <see cref="MobMeleeRange"/>. Out of range the server walks the player in (moveToPawn).
    /// </summary>
    public const int MeleeRange = 80;
    /// <summary>
    /// Ground pickup gate. Mobius never picks loot up at range: <c>PlayerAI.thinkPickUp</c> calls
    /// <c>maybeMoveToPawn(target, 36)</c> and <c>CreatureAI.maybeMoveToPawn</c> only proceeds when
    /// <c>isInsideRadius2D(target, 36 + collisionRadius)</c>; a dropped <c>Item</c> is not a Creature, so the
    /// gate is 36 + the player's template collisionRadius (9) = <b>45</b>. Anything farther runs in first.
    /// </summary>
    public const int PickupRange = 45;
    /// <summary>Mobius AI_INTENTION_PICK_UP moveToPawn offset (36) — the approach stop distance base.</summary>
    public const int PickupReach = 36;
    /// <summary>Mobius RequestDropItem: isInsideRadius3D(x, y, z, 150) and |dz| &lt;= 50, else CANNOT_DISCARD_DISTANCE_TOO_FAR.</summary>
    public const int DropRange = 150;
    public const int DropMaxZDelta = 50;
    /// <summary>Mobius moveToPawn offset: stop a bit inside the attack range so the swing passes the check.</summary>
    public const int ApproachSlack = 20;
    /// <summary>Mobius <c>CreatureTemplate</c> default <c>baseRunSpd</c> when a template has none.</summary>
    public const float DefaultPlayerRunSpeed = 120f;
    /// <summary>Mobius <c>CreatureTemplate</c> default <c>baseWalkSpd</c> when a template has none.</summary>
    public const float DefaultWalkSpeed = 50f;
    /// <summary>
    /// Interlude <c>baseMoveSpd/slowSwim</c> and <c>fastSwim</c>: 50 in every player template. Kept as the template
    /// default for swim bases we have no data for — swim speed still goes through the runSpd stat pipeline.
    /// </summary>
    public const float DefaultSwimSpeed = 50f;
    /// <summary>
    /// Mobius <c>Character.ini MaxRunSpeed</c> (250): <c>PlayerStat.getRunSpeed/getWalkSpeed</c> cap the result after
    /// the stat fold and <c>Config.RUN_SPD_BOOST</c>. Players only — NPC templates (Valakas 300) are never capped.
    /// </summary>
    public const float MaxRunSpeed = 250f;
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
    public static readonly (PaperdollSlot Slot, int NakedMDef)[] NakedMDefBySlot =
    [
        (PaperdollSlot.Lear, 9),
        (PaperdollSlot.Rear, 9),
        (PaperdollSlot.LFinger, 5),
        (PaperdollSlot.RFinger, 5),
        (PaperdollSlot.Neck, 13),
    ];

    /// <summary>Mobius PlayerTemplate naked P.Def split (sum 80): equipping a slot replaces its share.</summary>
    public static readonly (PaperdollSlot Slot, int NakedPDef)[] NakedPDefBySlot =
    [
        (PaperdollSlot.Chest, 31),
        (PaperdollSlot.Legs, NakedLegsPDef),
        (PaperdollSlot.Head, 12),
        (PaperdollSlot.Feet, 7),
        (PaperdollSlot.Gloves, 8),
        (PaperdollSlot.Under, 3),
        (PaperdollSlot.Cloak, 1),
    ];

    /// <summary>Gremlin (20001) Mobius <c>defence physical</c>; reference for <see cref="SampleMeleeDamage"/>.</summary>
    public const double GremlinPDef = 44.444;

    /// <summary>Reference hit: base fighter (P.Atk 4) vs Gremlin through <see cref="PhysicalDamage"/> (= 6).</summary>
    public static readonly int SampleMeleeDamage = PhysicalDamage(DefaultPlayerPAtk, GremlinPDef);

    /// <summary>Mobius <c>Formulas.calculateTimeBetweenAttacks</c>: 500000 / Pspd (ms), floor 100.</summary>
    public static int AttackTimeMs(int pAtkSpd) => Math.Max(100, 500000 / Math.Max(1, pAtkSpd));

    /// <summary>Mobius <c>Creature.doAttack</c>: damage lands at timeAtk / 2.</summary>
    public static int HitTimeMs(int pAtkSpd) => AttackTimeMs(pAtkSpd) / 2;

    /// <summary>Interlude Attack hit flags (client Hit.cs): 0x20 crit, 0x80 miss.</summary>
    public const byte HitFlagCrit = 0x20;
    public const byte HitFlagMiss = 0x80;
    /// <summary>Interlude HITFLAG_USESS (0x10) | soulshot grade in the low bits.</summary>
    public const byte HitFlagSoulshot = 0x10;
    /// <summary>Interlude HITFLAG_SHLD: target blocked with a shield.</summary>
    public const byte HitFlagShield = 0x40;
    /// <summary>Mobius Config.ALT_PERFECT_SHLD_BLOCK (%).</summary>
    public const int PerfectShieldBlockPercent = 5;
    /// <summary>Mobius crit rate cap (MAX_PCRIT_RATE 500 ‰).</summary>
    public const int MaxCritPermille = 500;
    /// <summary>Mobius <c>baseRndDam</c> for every player template (fists / weapons without random_damage).</summary>
    public const int DefaultPlayerRandomDamage = 10;

    // Mobius dist/game/data/stats/hitConditionBonus.xml (Interlude).
    public const int HitBonusBack = 10, HitBonusFront = 0, HitBonusSide = 5, HitBonusHigh = 3, HitBonusLow = -3, HitBonusDark = -10;

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
}
