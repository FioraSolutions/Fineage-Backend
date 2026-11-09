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
    /// levelMod = (level + 89) / 100. Then <see cref="PlayerStatMods"/> folds buffs, passives, armor-set skills,
    /// weight/grade/death penalties. Henna adjusts base STR/DEX/… before the bonus table; enchant adds on weapon/armor
    /// fields (Mobius <c>FuncEnchant</c>). A thin <c>IStatCalculator</c> is deferred until these layers start to
    /// duplicate — do not start a Calculator/AbstractFunction big-bang here.
    /// </summary>
    internal PlayerCombatStats PlayerStats(ZoneEntity player)
    {
        int level = Math.Max(1, player.Level);
        double levelMod = (level + 89) / 100d;
        if (!PlayerClasses.TryGet(player.ClassId, out var t) || t == null)
            throw new InvalidOperationException($"PlayerStats: unknown classId={player.ClassId} object={player.ObjectId}");

        var bonus = PlayerClasses.StatBonuses;
        int strStat = t.Str + player.HennaStr;
        int dexStat = t.Dex + player.HennaDex;
        int intStat = t.Int + player.HennaInt;
        int witStat = t.Wit + player.HennaWit;
        int menStat = t.Men + player.HennaMen;
        double str = StatBonus(bonus, StatSlot.Str, strStat);
        double dex = StatBonus(bonus, StatSlot.Dex, dexStat);
        double intel = StatBonus(bonus, StatSlot.Int, intStat);
        double wit = StatBonus(bonus, StatSlot.Wit, witStat);
        double men = StatBonus(bonus, StatSlot.Men, menStat);

        var weaponItem = player.Inventory?.GetEquipped(PaperdollSlot.RHand);
        var weapon = weaponItem?.Template;
        bool armed = weapon != null && weapon.Kind == ItemKind.Weapon;
        int weaponEnchant = armed ? weaponItem!.EnchantLevel : 0;
        double basePAtk = armed && weapon!.PAtk > 0
            ? weapon.PAtk + EnchantCombatBonus.PhysicalAttack(weapon, weaponEnchant)
            : t.PAtk;
        double baseMAtk = armed && weapon!.MAtk > 0
            ? weapon.MAtk + EnchantCombatBonus.MagicAttack(weapon.Crystal, weaponEnchant)
            : t.MAtk;
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
                var armorItem = player.Inventory.GetEquipped(slot);
                var armor = armorItem?.Template;
                if (armor == null || armor.Kind != ItemKind.Armor)
                    continue;
                pDefBase += armor.PDef + EnchantCombatBonus.Defence(armorItem!.EnchantLevel) - nakedDef;
                // Full-body armor (Mobius bodypart fullarmor) also covers the legs slot.
                if (slot == PaperdollSlot.Chest && armor.BodySlot == BodySlot.FullArmor)
                    pDefBase -= mystic ? ZoneCombat.MysticNakedLegsPDef : ZoneCombat.NakedLegsPDef;
            }
            foreach (var (slot, nakedDef) in ZoneCombat.NakedMDefBySlot)
            {
                var jewelItem = player.Inventory.GetEquipped(slot);
                var jewel = jewelItem?.Template;
                if (jewel == null || jewel.Kind != ItemKind.Armor)
                    continue;
                mDefBase += jewel.MDef + EnchantCombatBonus.Defence(jewelItem!.EnchantLevel) - nakedDef;
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
            Accuracy: mods.Apply(CombatStat.Accuracy, (int)(Math.Sqrt(dexStat) * 6 + level)),
            CritRate: mods.Apply(CombatStat.CritRate, (int)(crit * dex * 10)),
            AttackRange: range,
            Weapon: armed ? weapon!.WeaponType : WeaponType.None,
            Evasion: mods.Apply(CombatStat.Evasion, (int)ZoneCombat.AccuracyOrEvasion(0, dexStat, level) - (shielded ? shield!.ShieldEvasionPenalty : 0)),
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
    /// <para>
    /// Passive sources merged by <see cref="PassiveSkillLevels"/>:
    /// trainer/auto <see cref="ZoneEntity.KnownSkills"/> (includes skill-enchant levels once EnchantSkill writes them),
    /// and <see cref="ZoneEntity.ClanSkills"/> (Mobius <c>Clan.addSkillEffects</c> — grant path still TBD).
    /// </para>
    /// </summary>
    internal CombatStatMods PlayerStatMods(ZoneEntity player)
    {
        var mods = player.Abnormals.Count == 0 ? null : _statEffects.Collect(player.Abnormals);
        if (mods != null && ReferenceEquals(mods, CombatStatMods.None))
            mods = null;
        var passiveLevels = PassiveSkillLevels(player);
        mods = passiveLevels.Count == 0
            ? mods
            : _passives.CollectPassives(passiveLevels, kinds => Wearing(player, kinds), mods);
        // Weight Penalty 4270 is added by refreshOverloaded instead of the skill book, so it is folded here; Grade
        // Penalty 4267 comes from refreshExpertisePenalty the same way, and Death Penalty 5076 from
        // increaseDeathPenaltyBuffLevel — none of the three live in the trainer skill book.
        mods = FoldArmorSets(player, mods);
        mods = FoldWeightPenalty(player, mods);
        mods = FoldGradePenalty(player, mods);
        return FoldDeathPenalty(player, mods);
    }

    /// <summary>
    /// Skill id → level fed into <c>skill-passives.json</c> for combat mods.
    /// <list type="bullet">
    /// <item>KnownSkills: auto-tree + learned. EnchantSkill (Mobius) bumps the learned level in place — no extra map.</item>
    /// <item>ClanSkills: clan book effects (376 Might, 377 Aegis, …). Populate when clan skill grant is wired;
    /// funcs already live in <c>skill-passives.json</c>.</item>
    /// </list>
    /// Higher level wins on id collision.
    /// </summary>
    internal static Dictionary<int, int> PassiveSkillLevels(ZoneEntity player)
    {
        var map = new Dictionary<int, int>(player.KnownSkills.Count + player.ClanSkills.Count);
        foreach (var (id, level) in player.KnownSkills)
            map[id] = level;
        foreach (var (id, level) in player.ClanSkills)
        {
            if (!map.TryGetValue(id, out int have) || level > have)
                map[id] = level;
        }
        return map;
    }

    /// <summary>
    /// Mobius <c>ArmorSetListener</c>: when the worn chest matches a set and every required part is on,
    /// fold the set's skills (and optional shield / +6 skills) through the passive catalog.
    /// </summary>
    CombatStatMods FoldArmorSets(ZoneEntity player, CombatStatMods? mods)
    {
        if (player.Inventory == null || _armorSets.Count == 0)
            return mods ?? CombatStatMods.None;
        var inv = player.Inventory;
        var chest = inv.GetEquipped(PaperdollSlot.Chest);
        if (chest == null)
            return mods ?? CombatStatMods.None;
        int? ItemId(PaperdollSlot slot) => inv.GetEquipped(slot)?.ItemId;
        int EnchantOf(int itemId)
        {
            foreach (PaperdollSlot slot in new[]
                     {
                         PaperdollSlot.Chest, PaperdollSlot.Legs, PaperdollSlot.Head,
                         PaperdollSlot.Gloves, PaperdollSlot.Feet, PaperdollSlot.LHand
                     })
            {
                var item = inv.GetEquipped(slot);
                if (item != null && item.ItemId == itemId)
                    return item.EnchantLevel;
            }
            return 0;
        }
        var skills = _armorSets.ActiveSkills(
            chest.ItemId,
            ItemId(PaperdollSlot.Legs),
            ItemId(PaperdollSlot.Head),
            ItemId(PaperdollSlot.Gloves),
            ItemId(PaperdollSlot.Feet),
            ItemId(PaperdollSlot.LHand),
            EnchantOf);
        return skills.Count == 0 ? mods ?? CombatStatMods.None : _passives.CollectPassives(skills, kinds => Wearing(player, kinds), mods);
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
