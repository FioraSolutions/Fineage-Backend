namespace Fineage.Game;

/// <summary>What a failed roll does to the target. Scrolls do not share one penalty.</summary>
public enum EnchantFail
{
    /// <summary>Normal scroll. The item is destroyed and may return crystals.</summary>
    Crystallize,

    /// <summary>Blessed scroll. The item stays and its enchant becomes 0.</summary>
    Reset,

    /// <summary>Crystal scroll. The item and its enchant are kept.</summary>
    Keep
}

/// <summary>
/// One enchant scroll definition. Target grade, weapon versus armor, fail mode,
/// max enchant, and bonus rate come from the scroll entry, not from the item's own crystal grade.
/// </summary>
public sealed record EnchantScroll(
    bool Weapon,
    CrystalGrade Grade,
    EnchantFail Fail,
    int MaxEnchant,
    double BonusRate)
{
    public const int DefaultMaxEnchant = 65535;
}
