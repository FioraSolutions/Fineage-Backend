using Fineage.Login;
using Fineage.Zone.Bosses;
using Fineage.Zone.Interest;
using Fineage.Zone.Olympiad;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.World;

namespace Fineage.Zone;

public static class ZoneAoiSettings
{
    /// <summary>0 = whole zone (<see cref="SingleCellAoi"/>). &gt;0 = <see cref="RangeAoi"/> radius in world units.</summary>
    public const int DefaultInterestRange = 0;

    /// <summary>Hosted default grid size — full Interlude datapack must not use SingleCellAoi.</summary>
    public const int DefaultAoiCell = 3000;

    public static int InterestRange => ReadInt("FINEAGE_ZONE_INTEREST_RANGE", DefaultInterestRange, min: 0, max: 500_000);

    /// <summary>
    /// When &gt;0, use <see cref="GridAoi"/> with this cell size (ARCHITECTURE_NET Phase 1).
    /// Takes precedence over <see cref="InterestRange"/>. Default <see cref="DefaultAoiCell"/>;
    /// set <c>FINEAGE_ZONE_AOI_CELL=0</c> only for tiny single-map test hosts.
    /// </summary>
    public static int AoiCellSize => ReadInt("FINEAGE_ZONE_AOI_CELL", DefaultAoiCell, min: 0, max: 500_000);

    /// <summary>
    /// The AOI the world will actually be built with, as one banner-safe token (grid:3000 / range:3500 /
    /// single-cell). <see cref="AoiCellSize"/> wins over <see cref="InterestRange"/>, so reporting the raw
    /// range alone would claim "whole zone" while a grid is live.
    /// </summary>
    public static string DescribeEffective()
    {
        int cell = AoiCellSize;
        if (cell > 0)
            return $"grid:{cell}";
        int range = InterestRange;
        return range > 0 ? $"range:{range}" : "single-cell";
    }

    public static ZoneWorld CreateWorld(
        IAoi? aoi = null,
        IAccountStore? accounts = null,
        SevenSignsEngine? sevenSigns = null,
        bool includeEnabledMaps = true,
        BossSchedule? bosses = null,
        OlympiadBoard? olympiad = null)
    {
        if (aoi != null)
        {
            return new ZoneWorld(
                aoi,
                accounts: accounts,
                sevenSigns: sevenSigns,
                includeEnabledMaps: includeEnabledMaps,
                bosses: bosses,
                olympiad: olympiad);
        }

        int cell = AoiCellSize;
        if (cell > 0)
        {
            return new ZoneWorld(
                new GridAoi(cell),
                accounts: accounts,
                sevenSigns: sevenSigns,
                includeEnabledMaps: includeEnabledMaps,
                bosses: bosses,
                olympiad: olympiad);
        }

        int range = InterestRange;
        return range <= 0
            ? new ZoneWorld(accounts: accounts, sevenSigns: sevenSigns, includeEnabledMaps: includeEnabledMaps, bosses: bosses, olympiad: olympiad)
            : new ZoneWorld(new RangeAoi(range), accounts: accounts, sevenSigns: sevenSigns, includeEnabledMaps: includeEnabledMaps, bosses: bosses, olympiad: olympiad);
    }

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }
}
