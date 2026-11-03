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
    /// Mobius <c>Creature.getStat().getRunSpeed()</c>: base template run speed plus active buff/debuff and passive
    /// <c>runSpd</c> funcs (Wind Walk, Quick Step, Slow, Paralyze…), clamped to Mobius <c>MaxRunSpeed</c> 250.
    /// Walk speed has no Mobius stat, so it is never modified by buffs.
    /// </summary>
    internal static float RunSpeedStat(ZoneEntity entity, CombatStatMods mods)
    {
        float baseSpeed = entity.BaseRunSpeed > 0 ? entity.BaseRunSpeed : ZoneCombat.DefaultPlayerRunSpeed;
        double speed = mods.Apply(CombatStat.RunSpd, baseSpeed);
        return Math.Clamp((float)speed, ZoneCombat.MinRunSpeed, ZoneCombat.MaxRunSpeed);
    }

    /// <summary>
    /// Recomputes the cached live <see cref="ZoneEntity.RunSpeed"/> from base + current stat funcs and reports whether it
    /// moved, so callers can re-send ChangeMoveType (Mobius Player.updateStats → broadcast move speeds).
    /// Players fold passive masteries too (Quick Step / Agile Movement / armor-set runSpd).
    /// </summary>
    internal bool RefreshRunSpeed(ZoneEntity entity)
    {
        float before = entity.RunSpeed;
        var mods = entity.Kind == ZoneEntityKind.Player ? PlayerStatMods(entity) : StatMods(entity);
        entity.SetLiveRunSpeed(RunSpeedStat(entity, mods));
        return Math.Abs(before - entity.RunSpeed) > 0.001f;
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
