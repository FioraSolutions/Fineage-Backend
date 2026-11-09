using Fineage.Game;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>Player physical/magic stats after Mobius base-stat, level, weapon and armor functions.</summary>
public readonly record struct PlayerCombatStats(
    int PAtk, int MAtk, int PDef, int MDef, int PAtkSpd, int MAtkSpd, int Accuracy, int CritRate,
    int AttackRange, WeaponType Weapon, int Evasion = 0, int RandomDamage = ZoneCombat.DefaultPlayerRandomDamage,
    int ShieldDef = 0, double ShieldRate = 0, int Soulshots = 0, CrystalGrade WeaponGrade = CrystalGrade.None,
    int MCritRate = 0, double MagicCritDmg = 1d, double CritDmg = 1d, double BlowRate = 1d);

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
            MCritRate: ZoneCombat.PlayerMCritRate(armed, wit, mods),
            MagicCritDmg: mods.Apply(CombatStat.MagicCritDmg, 1d),
            CritDmg: mods.Apply(CombatStat.CritDmg, 1d),
            BlowRate: mods.Apply(CombatStat.BlowRate, 1d));
    }

    /// <summary>Mobius GameTimeTaskManager.isNight for HitConditionBonus dark −10.</summary>
    internal bool IsNight => _gameTime.IsNight(_clock().ToUnixTimeMilliseconds());

    /// <summary>
    /// Buffs plus passive masteries (Mobius operateType P) whose <c>using</c> condition matches the gear, plus the
    /// virtual Weight Penalty 4270 (<see cref="FoldWeightPenalty"/>) and Grade Penalty 4267
    /// (<see cref="FoldGradePenalty"/>) sources. Internal so tests can assert the folds that the speed/regen/load
    /// getters share.
    /// </summary>
    internal CombatStatMods PlayerStatMods(ZoneEntity player)
    {
        var mods = player.Abnormals.Count == 0 ? null : _statEffects.Collect(player.Abnormals);
        if (mods != null && ReferenceEquals(mods, CombatStatMods.None))
            mods = null;
        mods = player.KnownSkills.Count == 0 ? mods : _passives.CollectPassives(player.KnownSkills, kinds => Wearing(player, kinds), mods);
        // Weight Penalty 4270 is added by refreshOverloaded instead of the skill book, so it is folded here; Grade
        // Penalty 4267 comes from refreshExpertisePenalty the same way, and Death Penalty 5076 from
        // increaseDeathPenaltyBuffLevel — none of the three live in the trainer skill book.
        mods = FoldWeightPenalty(player, mods);
        mods = FoldGradePenalty(player, mods);
        return FoldDeathPenalty(player, mods);
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

}
