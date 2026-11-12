namespace Fineage.Game.Shops;

/// <summary>
/// Buy, sell, and multisell checks. Formulas follow L2J Mobius CT 0 Interlude
/// <c>RequestBuyItem</c>, <c>RequestSellItem</c>, <c>MultiSellChoose</c>,
/// <c>PreparedListContainer</c>, and <c>MerchantPriceConfig</c>.
/// Sell price is the item reference price divided by 2.
/// Buy price is <c>base * (1 + townBaseTax/100 + castleTax/100)</c>, truncated toward zero.
/// Castle tax is supplied by the caller and is 0 when the castle has no owner.
/// </summary>
public static partial class ShopTrade
{
    public const int AdenaItemId = 57;
    public const int AncientAdenaItemId = 5575;
    public const int MaxAdena = 2_000_000_000;
    public const int MaxCountPerLine = 10_000;
    public const int MaxLines = 250;
    public const int SlotsNoDwarf = 80;
    public const int SlotsDwarf = 100;
    public const int DwarfRace = 4;
    public const int WeightLimitBase = 69_000;
    public const int SiegeGuardFirst = 3960;
    public const int SiegeGuardLast = 4026;

    /// <summary>Mobius <c>RateSiegeGuardsPrice</c> default.</summary>
    public const double SiegeGuardPriceRate = 1;

    public static int SlotLimit(int race) => race == DwarfRace ? SlotsDwarf : SlotsNoDwarf;

    /// <summary>
    /// Mobius <c>Creature.getMaxLoad</c> without a weight-limit skill: <c>floor(CON bonus * 69000 * AltWeightLimit)</c>.
    /// Delegates to <see cref="WeightPenalty.BaseMaxLoad"/> so the shop guard and the overload level cannot drift apart;
    /// the live server uses <c>ZoneWorld.MaxLoadOf</c>, which folds <c>Stat.WEIGHT_LIMIT</c> on top of this base.
    /// </summary>
    public static int MaxLoad(ClassCatalog classes, int classId)
    {
        ArgumentNullException.ThrowIfNull(classes);
        if (!classes.TryGet(classId, out var template) || template == null)
            return WeightLimitBase;
        double bonus = classes.StatBonuses.Bonus(StatSlot.Con, template.Con);
        return WeightPenalty.BaseMaxLoad(bonus);
    }

    public static int TaxedUnitPrice(int basePrice, int baseTaxPercent, int castleTaxPercent)
    {
        if (basePrice < 0)
            return -1;
        if (basePrice == 0)
            return 0;
        double taxed = basePrice * (1d + (baseTaxPercent + castleTaxPercent) / 100d);
        if (taxed < 0 || taxed > MaxAdena)
            return -1;
        return (int)taxed;
    }
}
