using Fineage.Game;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>Player physical/magic stats after Mobius base-stat, level, weapon and armor functions.</summary>
public readonly record struct PlayerCombatStats(
    int PAtk, int MAtk, int PDef, int MDef, int PAtkSpd, int MAtkSpd, int Accuracy, int CritRate,
    int AttackRange, WeaponType Weapon, int Evasion = 0, int RandomDamage = ZoneCombat.DefaultPlayerRandomDamage);

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

        return new PlayerCombatStats(
            PAtk: (int)(basePAtk * str * levelMod),
            MAtk: (int)(baseMAtk * intel * intel * levelMod * levelMod),
            PDef: Math.Max(1, (int)(pDefBase * levelMod)),
            MDef: Math.Max(1, (int)(mDefBase * men * levelMod)),
            PAtkSpd: Math.Max(1, (int)(baseSpd * dex)),
            MAtkSpd: (int)(t.MAtkSpd * wit),
            Accuracy: (int)(Math.Sqrt(t.Dex) * 6 + level),
            CritRate: (int)(crit * dex * 10),
            AttackRange: range,
            Weapon: armed ? weapon!.WeaponType : WeaponType.None,
            Evasion: (int)ZoneCombat.AccuracyOrEvasion(0, t.Dex, level),
            RandomDamage: armed && weapon!.RandomDamage > 0 ? weapon.RandomDamage : ZoneCombat.DefaultPlayerRandomDamage);
    }

    double NpcAccuracy(ZoneEntity npc) => ZoneCombat.AccuracyOrEvasion(npc.BaseAccuracy, npc.Dex, npc.Level);
    double NpcEvasion(ZoneEntity npc) => ZoneCombat.AccuracyOrEvasion(npc.BaseEvasion, npc.Dex, npc.Level);
    int NpcCritPermille(ZoneEntity npc) =>
        (int)(npc.BaseCritical * 10 * SafeBonus(PlayerClasses.StatBonuses, StatSlot.Dex, npc.Dex));

    static double SafeBonus(StatBonusTable table, StatSlot slot, int value)
    {
        try { return table.Bonus(slot, value); }
        catch (ArgumentOutOfRangeException) { return 1d; }
    }
}
