namespace Fineage.Zone.Shops;

/// <summary>
/// Mobius <c>MerchantPriceConfig.xml</c> town tax. The base percent always applies.
/// Castle tax is added only when that castle has an owner; the caller reads <c>Castle.TaxPercent</c>.
/// </summary>
public static class MerchantTowns
{
    public readonly record struct TownTax(int BaseTaxPercent, int CastleId);

    public static TownTax For(string? mapId)
    {
        var id = mapId ?? "";
        var exact = Exact(id);
        return exact ?? Regional(id);
    }

    static TownTax? Exact(string id) => id switch
    {
        "ti_village" or "ti_cedric" or "ti_village_road" => new TownTax(15, 1),
        "elven_village" or "dark_elven_village" => new TownTax(15, 4),
        "orc_village" or "dwarven_village" => new TownTax(15, 9),
        "gludin" or "gludio" => new TownTax(20, 1),
        "dion" => new TownTax(20, 2),
        "giran" => new TownTax(10, 3),
        "oren" => new TownTax(15, 4),
        "hunters" => new TownTax(30, 5),
        "aden" => new TownTax(20, 5),
        "goddard" => new TownTax(20, 7),
        "rune" => new TownTax(20, 8),
        "heine" => new TownTax(20, 6),
        "schuttgart" => new TownTax(20, 9),
        "floran" => new TownTax(50, 2),
        _ => null
    };

    /// <summary>
    /// Outdoor and dungeon maps use the same castle as the town they belong to.
    /// Grid files under <c>others_</c> and Seven Signs halls stay untaxed.
    /// </summary>
    static TownTax Regional(string id)
    {
        if (id.StartsWith("ti_", StringComparison.Ordinal))
            return new(15, 1);
        if (id.StartsWith("dark_elf", StringComparison.Ordinal) || id.StartsWith("elven_", StringComparison.Ordinal))
            return new(15, 4);
        if (id.StartsWith("orc_", StringComparison.Ordinal) || id.StartsWith("dwarven_", StringComparison.Ordinal))
            return new(15, 9);
        if (id.StartsWith("gludin_", StringComparison.Ordinal) || id.StartsWith("gludio_", StringComparison.Ordinal) ||
            id.StartsWith("castles_gludio", StringComparison.Ordinal))
            return new(20, 1);
        if (id.StartsWith("dion_", StringComparison.Ordinal) || id.StartsWith("castles_dion", StringComparison.Ordinal))
            return new(20, 2);
        if (id.StartsWith("giran_", StringComparison.Ordinal) || id.StartsWith("castles_giran", StringComparison.Ordinal))
            return new(10, 3);
        if (id.StartsWith("oren_", StringComparison.Ordinal) || id.StartsWith("castles_oren", StringComparison.Ordinal))
            return new(15, 4);
        if (id.StartsWith("hunters_", StringComparison.Ordinal))
            return new(30, 5);
        if (id.StartsWith("aden_", StringComparison.Ordinal) || id.StartsWith("castles_aden", StringComparison.Ordinal))
            return new(20, 5);
        if (id.StartsWith("innadril_", StringComparison.Ordinal) || id.StartsWith("castles_innadril", StringComparison.Ordinal))
            return new(20, 6);
        if (id.StartsWith("goddard_", StringComparison.Ordinal) || id.StartsWith("castles_goddard", StringComparison.Ordinal))
            return new(20, 7);
        if (id.StartsWith("rune_", StringComparison.Ordinal) || id.StartsWith("castles_rune", StringComparison.Ordinal))
            return new(20, 8);
        if (id.StartsWith("schuttgart_", StringComparison.Ordinal) || id.StartsWith("castles_schuttgart", StringComparison.Ordinal))
            return new(20, 9);
        return new(0, 0);
    }
}
