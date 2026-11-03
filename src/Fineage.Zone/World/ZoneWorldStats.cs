using Fineage.Game;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>Player physical/magic stats after Mobius base-stat, level, weapon and armor functions.</summary>
public readonly record struct PlayerCombatStats(
    int PAtk, int MAtk, int PDef, int MDef, int PAtkSpd, int MAtkSpd, int Accuracy, int CritRate,
    int AttackRange, WeaponType Weapon, int Evasion = 0, int RandomDamage = ZoneCombat.DefaultPlayerRandomDamage,
    int ShieldDef = 0, double ShieldRate = 0, int Soulshots = 0, CrystalGrade WeaponGrade = CrystalGrade.None, int MCritRate = 0);

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius Interlude player stat pipeline (subset):
    /// P.Atk = (weapon pAtk | fist base) * STR bonus * levelMod; M.Atk = (weapon mAtk | base) * INT² * levelMod²;
    /// P.Def = (base 80 − default of each equipped armor slot + armor pDef) * levelMod; M.Def = base * MEN * levelMod;
    /// Pspd = (weapon pAtkSpd | 300) * DEX bonus; Cspd = base * WIT bonus; accuracy = sqrt(DEX)*6 + level;
    /// M.Def swaps naked jewel shares for jewel mDef the same way.
    /// levelMod = (level + 89) / 100. Buffs, enchant, grade penalty and jewel M.Def are not applied yet.
    /// </summary>
    internal PlayerCombatStats PlayerStats(ZoneEntity player)
    {
        int level = Math.Max(1, player.Level);
        double levelMod = (level + 89) / 100d;
        if (!PlayerClasses.TryGet(player.ClassId, out var t) || t == null)
            return new PlayerCombatStats(ZoneCombat.DefaultPlayerPAtk, 0, ZoneCombat.DefaultPlayerPDef, 0,
                ZoneCombat.DefaultPlayerPAtkSpd, 0, 0, 0, ZoneCombat.DefaultWeaponRange, WeaponType.None);

        var bonus = PlayerClasses.StatBonuses;
        double str = SafeBonus(bonus, StatSlot.Str, t.Str);
        double dex = SafeBonus(bonus, StatSlot.Dex, t.Dex);
        double intel = SafeBonus(bonus, StatSlot.Int, t.Int);
        double wit = SafeBonus(bonus, StatSlot.Wit, t.Wit);
        double men = SafeBonus(bonus, StatSlot.Men, t.Men);

        var weapon = player.Inventory?.GetEquipped(PaperdollSlot.RHand)?.Template;
        bool armed = weapon != null && weapon.Kind == ItemKind.Weapon;
        double basePAtk = armed && weapon!.PAtk > 0 ? weapon.PAtk : t.PAtk;
        double baseMAtk = armed && weapon!.MAtk > 0 ? weapon.MAtk : t.MAtk;
        double baseSpd = armed && weapon!.PAtkSpd > 0 ? weapon.PAtkSpd : t.PAtkSpd;
        int range = armed && weapon!.AttackRange > 0 ? weapon.AttackRange : ZoneCombat.DefaultWeaponRange;
        int crit = armed && weapon!.CritRate > 0 ? weapon.CritRate : t.CritRate;

        double pDefBase = t.PDef;
        double mDefBase = t.MDef;
        if (player.Inventory != null)
        {
            bool mystic = t.PDef < ZoneCombat.DefaultPlayerPDef;
            foreach (var (slot, fighterDef) in ZoneCombat.NakedPDefBySlot)
            {
                // Mobius mystic templates: chest 15 / legs 8 (base 54) instead of 31 / 18 (base 80).
                int nakedDef = !mystic ? fighterDef
                    : slot == PaperdollSlot.Chest ? ZoneCombat.MysticNakedChestPDef
                    : slot == PaperdollSlot.Legs ? ZoneCombat.MysticNakedLegsPDef
                    : fighterDef;
                var armor = player.Inventory.GetEquipped(slot)?.Template;
                if (armor == null || armor.Kind != ItemKind.Armor)
                    continue;
                pDefBase += armor.PDef - nakedDef;
                // Full-body armor (Mobius bodypart fullarmor) also covers the legs slot.
                if (slot == PaperdollSlot.Chest && armor.BodySlot == BodySlot.FullArmor)
                    pDefBase -= mystic ? ZoneCombat.MysticNakedLegsPDef : ZoneCombat.NakedLegsPDef;
            }
            foreach (var (slot, nakedDef) in ZoneCombat.NakedMDefBySlot)
            {
                var jewel = player.Inventory.GetEquipped(slot)?.Template;
                if (jewel == null || jewel.Kind != ItemKind.Armor)
                    continue;
                mDefBase += jewel.MDef - nakedDef;
            }
        }

        var shield = player.Inventory?.GetEquipped(PaperdollSlot.LHand)?.Template;
        bool shielded = shield != null && shield.Kind == ItemKind.Armor && shield.ShieldDef > 0;
        var mods = PlayerStatMods(player);
        return new PlayerCombatStats(
            PAtk: mods.Apply(CombatStat.PAtk, (int)(basePAtk * str * levelMod)),
            MAtk: mods.Apply(CombatStat.MAtk, (int)(baseMAtk * intel * intel * levelMod * levelMod)),
            PDef: Math.Max(1, mods.Apply(CombatStat.PDef, (int)(pDefBase * levelMod))),
            MDef: Math.Max(1, mods.Apply(CombatStat.MDef, (int)(mDefBase * men * levelMod))),
            PAtkSpd: Math.Max(1, mods.Apply(CombatStat.PAtkSpd, (int)(baseSpd * dex))),
            MAtkSpd: Math.Max(1, mods.Apply(CombatStat.MAtkSpd, (int)(t.MAtkSpd * wit))),
            Accuracy: mods.Apply(CombatStat.Accuracy, (int)(Math.Sqrt(t.Dex) * 6 + level)),
            CritRate: mods.Apply(CombatStat.CritRate, (int)(crit * dex * 10)),
            AttackRange: range,
            Weapon: armed ? weapon!.WeaponType : WeaponType.None,
            Evasion: mods.Apply(CombatStat.Evasion, (int)ZoneCombat.AccuracyOrEvasion(0, t.Dex, level) - (shielded ? shield!.ShieldEvasionPenalty : 0)),
            RandomDamage: armed && weapon!.RandomDamage > 0 ? weapon.RandomDamage : ZoneCombat.DefaultPlayerRandomDamage,
            ShieldDef: shielded ? shield!.ShieldDef : 0,
            ShieldRate: shielded ? shield!.ShieldRate * dex : 0,
            Soulshots: armed ? weapon!.Soulshots : 0,
            WeaponGrade: armed ? weapon!.Crystal : CrystalGrade.None,
            MCritRate: ZoneCombat.PlayerMCritRate(armed, wit));
    }

    /// <summary>Buffs plus passive masteries (Mobius operateType P) whose <c>using</c> condition matches the gear.</summary>
    CombatStatMods PlayerStatMods(ZoneEntity player)
    {
        var mods = player.Abnormals.Count == 0 ? null : _statEffects.Collect(player.Abnormals);
        if (mods != null && ReferenceEquals(mods, CombatStatMods.None))
            mods = null;
        return player.KnownSkills.Count == 0 ? mods ?? CombatStatMods.None
            : _passives.CollectPassives(player.KnownSkills, kinds => Wearing(player, kinds), mods);
    }

    /// <summary>
    /// Mobius <c>CreatureStat.getRunSpeed()</c> (<c>CreatureStat.java:426-434</c>). Base is the template run speed, or
    /// the fast-swim base in water (<c>:428</c>); <c>baseRunSpd &lt;= 0</c> returns 0 without any stat fold.
    /// </summary>
    internal float RunSpeedStat(ZoneEntity entity, CombatStatMods mods, int runSpeedBoost = 0)
    {
        float baseSpeed = entity.InWater ? SwimRunBaseOf(entity) : RunBaseOf(entity);
        return baseSpeed <= 0 ? 0f : FoldSpeed(entity, baseSpeed, mods, runSpeedBoost);
    }

    /// <summary>
    /// Mobius <c>CreatureStat.getWalkSpeed()</c> (<c>CreatureStat.java:439-447</c>). Mobius has one speed stat
    /// (<c>Stat.MOVE_SPEED("runSpd")</c>, <c>Stat.java:99</c>) and the walk getter folds it onto the walk base too,
    /// so buffs and debuffs move walk speed just like run speed.
    /// </summary>
    internal float WalkSpeedStat(ZoneEntity entity, CombatStatMods mods, int runSpeedBoost = 0)
    {
        float baseSpeed = entity.InWater ? SwimWalkBaseOf(entity) : WalkBaseOf(entity);
        return baseSpeed <= 0 ? 0f : FoldSpeed(entity, baseSpeed, mods, runSpeedBoost);
    }

    /// <summary>Mobius <c>CreatureStat.getSwimRunSpeed()</c> (<c>CreatureStat.java:452-460</c>).</summary>
    internal float SwimRunSpeedStat(ZoneEntity entity, CombatStatMods mods, int runSpeedBoost = 0)
    {
        float baseSpeed = SwimRunBaseOf(entity);
        return baseSpeed <= 0 ? 0f : FoldSpeed(entity, baseSpeed, mods, runSpeedBoost);
    }

    /// <summary>Mobius <c>CreatureStat.getSwimWalkSpeed()</c> (<c>CreatureStat.java:465-473</c>).</summary>
    internal float SwimWalkSpeedStat(ZoneEntity entity, CombatStatMods mods, int runSpeedBoost = 0)
    {
        float baseSpeed = SwimWalkBaseOf(entity);
        return baseSpeed <= 0 ? 0f : FoldSpeed(entity, baseSpeed, mods, runSpeedBoost);
    }

    /// <summary>
    /// One <c>Stat.MOVE_SPEED</c> evaluation in Mobius order:
    /// <list type="number">
    /// <item><c>FuncMoveSpeed</c> × DEX bonus — order 1, so it runs before every XML func
    /// (<c>FuncMoveSpeed.java:34-45</c>). Registered only for players and summons
    /// (<c>Formulas.java:206,234</c>); an NPC's calculator set has no <c>MOVE_SPEED</c> entry
    /// (<c>Formulas.getStdNPCCalculators</c>), so NPC bases are never DEX-scaled.</item>
    /// <item>XML funcs by <c>order</c>: <c>SET</c> 0 → <c>DIV</c>/<c>MUL</c> 20 → <c>ADD</c>/<c>SUB</c> 30
    /// (<c>StatFunction.java:24-31</c>).</item>
    /// <item>Players only: <c>+Config.RUN_SPD_BOOST</c> then the <c>MaxRunSpeed</c> cap
    /// (<c>PlayerStat.java:512-525</c>, <c>:540-553</c>).</item>
    /// </list>
    /// There is <b>no lower floor</b>: <c>MOVE_SPEED</c> is absent from the <c>calcStat</c> "never ≤ 0" switch
    /// (<c>CreatureStat.java:99-125</c>), and 0 is exactly what Weight Penalty 4 produces (<c>runSpd</c> ×0).
    /// </summary>
    float FoldSpeed(ZoneEntity entity, float baseSpeed, CombatStatMods mods, int runSpeedBoost)
    {
        bool player = entity.Kind == ZoneEntityKind.Player;
        double speed = player ? baseSpeed * SafeBonus(PlayerClasses.StatBonuses, StatSlot.Dex, entity.Dex) : baseSpeed;
        speed = mods.Apply(CombatStat.RunSpd, speed);
        if (player)
        {
            speed += runSpeedBoost;
            if (speed > ZoneCombat.MaxRunSpeed)
                speed = ZoneCombat.MaxRunSpeed;
        }
        return (float)speed;
    }

    /// <summary>Mobius <c>CreatureTemplate.getBaseMoveSpeed(RUN)</c>, default 120 (<c>CreatureTemplate.java:145</c>).</summary>
    static float RunBaseOf(ZoneEntity entity) =>
        entity.BaseRunSpeed > 0 ? entity.BaseRunSpeed : ZoneCombat.DefaultPlayerRunSpeed;

    /// <summary>Mobius <c>getBaseMoveSpeed(WALK)</c>, default 50 (<c>CreatureTemplate.java:146</c>).</summary>
    static float WalkBaseOf(ZoneEntity entity) =>
        entity.BaseWalkSpeed > 0 ? entity.BaseWalkSpeed : ZoneCombat.DefaultWalkSpeed;

    /// <summary>
    /// <c>baseSwimRunSpd</c> falls back to <c>baseRunSpd</c> when a template omits it
    /// (<c>CreatureTemplate.java:147</c>). Player templates declare 50 in <c>baseMoveSpd/fastSwim</c>.
    /// </summary>
    static float SwimRunBaseOf(ZoneEntity entity) =>
        entity.BaseSwimRunSpeed > 0 ? entity.BaseSwimRunSpeed : RunBaseOf(entity);

    /// <summary><c>baseSwimWalkSpd</c> falls back to <c>baseWalkSpd</c> (<c>CreatureTemplate.java:148</c>).</summary>
    static float SwimWalkBaseOf(ZoneEntity entity) =>
        entity.BaseSwimWalkSpeed > 0 ? entity.BaseSwimWalkSpeed : WalkBaseOf(entity);

    /// <summary>
    /// Recomputes the cached live <see cref="ZoneEntity.RunSpeed"/> and <see cref="ZoneEntity.WalkSpeed"/> from the
    /// current stat funcs and reports whether either moved, so callers can re-send ChangeMoveType (Mobius
    /// <c>Player.updateStats</c> → broadcast move speeds).
    /// Players fold passive masteries too (Quick Step / Agile Movement / armor-set runSpd).
    /// </summary>
    internal bool RefreshRunSpeed(ZoneEntity entity)
    {
        float beforeRun = entity.RunSpeed;
        float beforeWalk = entity.WalkSpeed;
        float beforeSwimRun = entity.SwimRunSpeed;
        float beforeSwimWalk = entity.SwimWalkSpeed;
        var mods = entity.Kind == ZoneEntityKind.Player ? PlayerStatMods(entity) : StatMods(entity);
        int boost = ZoneSpeedSettings.RunSpeedBoost;
        entity.SetLiveRunSpeed(RunSpeedStat(entity, mods, boost));
        entity.SetLiveWalkSpeed(WalkSpeedStat(entity, mods, boost));
        entity.SetLiveSwimSpeed(SwimRunSpeedStat(entity, mods, boost), SwimWalkSpeedStat(entity, mods, boost));
        return Math.Abs(beforeRun - entity.RunSpeed) > 0.001f
            || Math.Abs(beforeWalk - entity.WalkSpeed) > 0.001f
            || Math.Abs(beforeSwimRun - entity.SwimRunSpeed) > 0.001f
            || Math.Abs(beforeSwimWalk - entity.SwimWalkSpeed) > 0.001f;
    }

    /// <summary>Mobius <c>ConditionUsingItemType</c>: weapon kinds test the right-hand weapon type; armor kinds (LIGHT/HEAVY/MAGIC)    /// need the chest armor type and, unless the chest is full-body, matching legs; SHIELD needs a left-hand shield.
    /// </summary>
    internal static bool Wearing(ZoneEntity player, string[] kinds)
    {
        var inv = player.Inventory;
        if (inv == null)
            return false;
        var weapon = inv.GetEquipped(PaperdollSlot.RHand)?.Template;
        var chest = inv.GetEquipped(PaperdollSlot.Chest)?.Template;
        var legs = inv.GetEquipped(PaperdollSlot.Legs)?.Template;
        var lhand = inv.GetEquipped(PaperdollSlot.LHand)?.Template;
        foreach (var kind in kinds)
        {
            switch (kind)
            {
                case "LIGHT" or "HEAVY" or "MAGIC":
                    var want = kind == "LIGHT" ? ArmorType.Light : kind == "HEAVY" ? ArmorType.Heavy : ArmorType.Magic;
                    if (chest != null && chest.ArmorType == want
                        && (chest.BodySlot == BodySlot.FullArmor || (legs != null && legs.ArmorType == want)))
                        return true;
                    break;
                case "SHIELD":
                    if (lhand != null && lhand.Kind == ItemKind.Armor && lhand.ShieldDef > 0)
                        return true;
                    break;
                default:
                    if (weapon == null || weapon.Kind != ItemKind.Weapon)
                        break;
                    var wt = weapon.WeaponType;
                    if (kind switch
                        {
                            "SWORD" => wt == WeaponType.Sword, "BLUNT" => wt == WeaponType.Blunt,
                            "DAGGER" or "DUALDAGGER" => wt == WeaponType.Dagger, "BOW" => wt == WeaponType.Bow,
                            "POLE" => wt == WeaponType.Pole, "DUAL" => wt == WeaponType.Dual,
                            "FIST" or "DUALFIST" => wt == WeaponType.Fist, "FISHINGROD" => wt == WeaponType.FishingRod,
                            "ETC" => wt == WeaponType.Etc, _ => false
                        })
                        return true;
                    break;
            }
        }
        return false;
    }

    /// <summary>Live buff/debuff stat funcs on <paramref name="entity"/> (players and NPCs).</summary>
    internal CombatStatMods StatMods(ZoneEntity entity) =>
        entity.Abnormals.Count == 0 ? CombatStatMods.None : _statEffects.Collect(entity.Abnormals);

    double NpcAccuracy(ZoneEntity npc) =>
        StatMods(npc).Apply(CombatStat.Accuracy, ZoneCombat.AccuracyOrEvasion(npc.BaseAccuracy, npc.Dex, npc.Level));
    double NpcEvasion(ZoneEntity npc) =>
        StatMods(npc).Apply(CombatStat.Evasion, ZoneCombat.AccuracyOrEvasion(npc.BaseEvasion, npc.Dex, npc.Level));
    int NpcCritPermille(ZoneEntity npc) =>
        StatMods(npc).Apply(CombatStat.CritRate, (int)(npc.BaseCritical * 10 * SafeBonus(PlayerClasses.StatBonuses, StatSlot.Dex, npc.Dex)));

    double NpcShieldRate(ZoneEntity npc) =>
        npc.ShieldRate * SafeBonus(PlayerClasses.StatBonuses, StatSlot.Dex, npc.Dex);

    static double SafeBonus(StatBonusTable table, StatSlot slot, int value)
    {
        try { return table.Bonus(slot, value); }
        catch (ArgumentOutOfRangeException) { return 1d; }
    }
}
