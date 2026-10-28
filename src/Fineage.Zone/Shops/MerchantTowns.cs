namespace Fineage.Zone.Shops;

/// <summary>
/// Mobius <c>MerchantPriceConfig.xml</c> town tax. The base percent always applies.
/// Castle tax is added only when that castle has an owner; the caller reads <c>Castle.TaxPercent</c>.
/// </summary>
public static class MerchantTowns
{
    public readonly record struct TownTax(int BaseTaxPercent, int CastleId);

    public static TownTax For(string? mapId) => (mapId ?? "") switch
    {
        "ti_village" or "ti_cedric" or "ti_village_road" => new(15, 1),
        "elven_village" or "dark_elven_village" => new(15, 4),
        "orc_village" or "dwarven_village" => new(15, 9),
        "gludin" or "gludio" => new(20, 1),
        "dion" => new(20, 2),
        "giran" => new(10, 3),
        "oren" => new(15, 4),
        "hunters" => new(30, 5),
        "aden" => new(20, 5),
        "goddard" => new(20, 7),
        "rune" => new(20, 8),
        "heine" => new(20, 6),
        "schuttgart" => new(20, 9),
        "floran" => new(50, 2),
        _ => new(0, 0)
    };
}
