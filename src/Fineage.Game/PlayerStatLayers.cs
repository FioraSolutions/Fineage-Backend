namespace Fineage.Game;

/// <summary>
/// Template base stats + henna deltas, with STR/DEX/… bonus-table multipliers already resolved.
/// Mobius order: CharTemplate → FuncHenna → BaseStat.calcBonus.
/// </summary>
public readonly record struct PlayerEffectiveAttributes(
    int Str,
    int Dex,
    int Con,
    int Int,
    int Wit,
    int Men,
    double StrBonus,
    double DexBonus,
    double ConBonus,
    double IntBonus,
    double WitBonus,
    double MenBonus)
{
    /// <summary>
    /// Build from a class template and henna sum. <paramref name="bonus"/> clamps out-of-range values;
    /// <paramref name="onClamped"/> is invoked once per clamped slot (fail-loud logging hook).
    /// </summary>
    public static PlayerEffectiveAttributes From(
        ClassTemplate template,
        HennaStatBonus henna,
        StatBonusTable bonus,
        Action<StatSlot, int>? onClamped = null)
    {
        int str = template.Str + henna.Str;
        int dex = template.Dex + henna.Dex;
        int con = template.Con + henna.Con;
        int intel = template.Int + henna.Int;
        int wit = template.Wit + henna.Wit;
        int men = template.Men + henna.Men;
        return new PlayerEffectiveAttributes(
            str, dex, con, intel, wit, men,
            Lookup(bonus, StatSlot.Str, str, onClamped),
            Lookup(bonus, StatSlot.Dex, dex, onClamped),
            Lookup(bonus, StatSlot.Con, con, onClamped),
            Lookup(bonus, StatSlot.Int, intel, onClamped),
            Lookup(bonus, StatSlot.Wit, wit, onClamped),
            Lookup(bonus, StatSlot.Men, men, onClamped));
    }

    static double Lookup(StatBonusTable table, StatSlot slot, int value, Action<StatSlot, int>? onClamped)
    {
        double result = table.BonusClamped(slot, value, out bool clamped);
        if (clamped)
            onClamped?.Invoke(slot, value);
        return result;
    }
}

/// <summary>
/// Weapon / armor / jewel contribution after Mobius <c>FuncEnchant</c>, before STR/level/buff folds.
/// </summary>
public readonly record struct PlayerGearBases(
    double PAtk,
    double MAtk,
    double PAtkSpd,
    int CritRate,
    int AttackRange,
    int RandomDamage,
    double PDef,
    double MDef,
    bool Armed,
    WeaponType Weapon,
    CrystalGrade WeaponGrade,
    int Soulshots,
    bool Shielded,
    int ShieldDef,
    double ShieldRate,
    int ShieldEvasionPenalty)
{
    /// <summary>Mobius levelMod = (level + 89) / 100.</summary>
    public static double LevelMod(int level) => (Math.Max(1, level) + 89) / 100d;

    /// <summary>
    /// Sum naked-slot replacement for armor/jewels and weapon set-stats, including enchant bonuses.
    /// <paramref name="equipped"/> yields the worn item for a paperdoll slot (null = empty).
    /// </summary>
    public static PlayerGearBases From(
        ClassTemplate template,
        Func<PaperdollSlot, ItemInstance?> equipped,
        int defaultWeaponRange,
        int defaultRandomDamage,
        IReadOnlyList<(PaperdollSlot Slot, int NakedPDef)> nakedPDefBySlot,
        IReadOnlyList<(PaperdollSlot Slot, int NakedMDef)> nakedMDefBySlot,
        int mysticNakedChestPDef,
        int mysticNakedLegsPDef,
        int nakedLegsPDef,
        int defaultPlayerPDef)
    {
        var weaponItem = equipped(PaperdollSlot.RHand);
        var weapon = weaponItem?.Template;
        bool armed = weapon != null && weapon.Kind == ItemKind.Weapon;
        int weaponEnchant = armed ? weaponItem!.EnchantLevel : 0;
        double basePAtk = armed && weapon!.PAtk > 0
            ? weapon.PAtk + EnchantCombatBonus.PhysicalAttack(weapon, weaponEnchant)
            : template.PAtk;
        double baseMAtk = armed && weapon!.MAtk > 0
            ? weapon.MAtk + EnchantCombatBonus.MagicAttack(weapon.Crystal, weaponEnchant)
            : template.MAtk;
        double baseSpd = armed && weapon!.PAtkSpd > 0 ? weapon.PAtkSpd : template.PAtkSpd;
        int range = armed && weapon!.AttackRange > 0 ? weapon.AttackRange : defaultWeaponRange;
        int crit = armed && weapon!.CritRate > 0 ? weapon.CritRate : template.CritRate;
        int random = armed && weapon!.RandomDamage > 0 ? weapon.RandomDamage : defaultRandomDamage;

        double pDefBase = template.PDef;
        double mDefBase = template.MDef;
        bool mystic = template.PDef < defaultPlayerPDef;
        foreach (var (slot, fighterDef) in nakedPDefBySlot)
        {
            int nakedDef = !mystic ? fighterDef
                : slot == PaperdollSlot.Chest ? mysticNakedChestPDef
                : slot == PaperdollSlot.Legs ? mysticNakedLegsPDef
                : fighterDef;
            var armorItem = equipped(slot);
            var armor = armorItem?.Template;
            if (armor == null || armor.Kind != ItemKind.Armor)
                continue;
            pDefBase += armor.PDef + EnchantCombatBonus.Defence(armorItem!.EnchantLevel) - nakedDef;
            if (slot == PaperdollSlot.Chest && armor.BodySlot == BodySlot.FullArmor)
                pDefBase -= mystic ? mysticNakedLegsPDef : nakedLegsPDef;
        }
        foreach (var (slot, nakedDef) in nakedMDefBySlot)
        {
            var jewelItem = equipped(slot);
            var jewel = jewelItem?.Template;
            if (jewel == null || jewel.Kind != ItemKind.Armor)
                continue;
            mDefBase += jewel.MDef + EnchantCombatBonus.Defence(jewelItem!.EnchantLevel) - nakedDef;
        }

        var shieldItem = equipped(PaperdollSlot.LHand);
        var shield = shieldItem?.Template;
        bool shielded = shield != null && shield.Kind == ItemKind.Armor && shield.ShieldDef > 0;

        return new PlayerGearBases(
            basePAtk,
            baseMAtk,
            baseSpd,
            crit,
            range,
            random,
            pDefBase,
            mDefBase,
            armed,
            armed ? weapon!.WeaponType : WeaponType.None,
            armed ? weapon!.Crystal : CrystalGrade.None,
            armed ? weapon!.Soulshots : 0,
            shielded,
            shielded ? shield!.ShieldDef : 0,
            shielded ? shield!.ShieldRate : 0,
            shielded ? shield!.ShieldEvasionPenalty : 0);
    }
}
