namespace Fineage.Zone;

/// <summary>
/// Optional spawn-map whitelist (FINEAGE_ZONE_MAPS=ti_village,ti_monsters,...) and XY bounds
/// (FINEAGE_ZONE_BOUNDS=minX,minY,maxX,maxY) so one Zone can host a region (e.g. Talking Island)
/// without loading the whole world. Unset = legacy behaviour (active map [+ enabled maps]).
/// </summary>
public static class ZoneMapFilter
{
    public readonly record struct Box(int MinX, int MinY, int MaxX, int MaxY);

    public static IReadOnlyList<string> Maps
    {
        get
        {
            string? text = Environment.GetEnvironmentVariable("FINEAGE_ZONE_MAPS");
            if (string.IsNullOrWhiteSpace(text))
                return [];
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal).ToArray();
        }
    }

    public static Box? Bounds
    {
        get
        {
            string? text = Environment.GetEnvironmentVariable("FINEAGE_ZONE_BOUNDS");
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var parts = text.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 4 || !parts.All(p => int.TryParse(p, out _)))
                return null;
            var v = parts.Select(int.Parse).ToArray();
            return new Box(Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Max(v[0], v[2]), Math.Max(v[1], v[3]));
        }
    }
}
