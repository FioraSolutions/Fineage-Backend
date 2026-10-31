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

    /// <summary>First box only (legacy). Prefer <see cref="BoundsList"/>.</summary>
    public static Box? Bounds => BoundsList.Count > 0 ? BoundsList[0] : null;

    /// <summary>
    /// One or more boxes separated by ';' (e.g. "-135000,200000,-40000,275000;20000,20000,80000,80000").
    /// A spawn is kept if it lies inside any box. Malformed boxes are ignored.
    /// </summary>
    public static IReadOnlyList<Box> BoundsList
    {
        get
        {
            string? text = Environment.GetEnvironmentVariable("FINEAGE_ZONE_BOUNDS");
            if (string.IsNullOrWhiteSpace(text))
                return [];
            var boxes = new List<Box>();
            foreach (string chunk in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = chunk.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length != 4 || !parts.All(p => int.TryParse(p, out _)))
                    continue;
                var v = parts.Select(int.Parse).ToArray();
                boxes.Add(new Box(Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Max(v[0], v[2]), Math.Max(v[1], v[3])));
            }
            return boxes;
        }
    }

    public static bool Contains(IReadOnlyList<Box> boxes, int x, int y)
    {
        if (boxes.Count == 0)
            return true;
        foreach (var b in boxes)
            if (x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY)
                return true;
        return false;
    }
}
