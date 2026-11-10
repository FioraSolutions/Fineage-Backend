namespace Fineage.Zone.Geo;

/// <summary>
/// Server-side L2 geodata (Mobius <c>GeoEngine</c>, simplified). Loads retail-style region files
/// <c>XX_YY.l2j</c> (L2J/Mobius format) or <c>XX_YY.l2d</c> (aCis/L2D format) from <c>data/geodata</c>
/// (or <c>FINEAGE_GEODATA_DIR</c>). A region is 32768×32768 world units = 256×256 blocks of 8×8 cells, 16 units per cell.
/// Where a region file is missing, <see cref="HasGeo"/> is false and callers keep their non-geo fallback.
/// </summary>
public sealed partial class GeoEngine
{
    public const int CellSize = 16;
    public const int RegionCells = 2048;
    const int RegionBlocks = 256;
    const int TileZeroX = 20;
    const int TileZeroY = 18;
    const int TileSize = 32768;

    // Mobius Cell NSWE bits.
    public const byte East = 1, West = 2, South = 4, North = 8, All = 15;

    /// <summary>Max upward step between neighbouring cells a walker may take (Mobius uses ~ cell height checks).</summary>
    public const int MaxClimb = 48;

    readonly Dictionary<int, Region> _regions = [];

    public static readonly GeoEngine Empty = new();

    public int RegionCount => _regions.Count;
    public string? Directory { get; private set; }

    public static GeoEngine LoadDefault()
    {
        string? dir = Environment.GetEnvironmentVariable("FINEAGE_GEODATA_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            dir = FindDefaultDir();
        if (dir == null || !System.IO.Directory.Exists(dir))
        {
            ZoneLog.Info("[geo] no geodata dir (data/geodata or FINEAGE_GEODATA_DIR) -> geo disabled, Z uses stub sanity clamp");
            return new GeoEngine();
        }

        var geo = LoadDirectory(dir);
        ZoneLog.Info(geo.RegionCount == 0
            ? $"[geo] {dir}: no *.l2j/*.l2d region files -> geo disabled, Z uses stub sanity clamp"
            : $"[geo] loaded {geo.RegionCount} region(s) from {dir}");
        return geo;
    }

    static string? FindDefaultDir()
    {
        foreach (string start in new[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var d = new DirectoryInfo(Path.GetFullPath(start));
            while (d != null)
            {
                string p = Path.Combine(d.FullName, "data", "geodata");
                if (System.IO.Directory.Exists(p))
                    return p;
                d = d.Parent;
            }
        }

        return null;
    }

    public static GeoEngine LoadDirectory(string dir)
    {
        var geo = new GeoEngine { Directory = dir };
        foreach (string file in System.IO.Directory.EnumerateFiles(dir))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".l2j" && ext != ".l2d")
                continue;
            string[] parts = Path.GetFileNameWithoutExtension(file).Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int rx) || !int.TryParse(parts[1], out int ry))
                continue;
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                geo._regions[Key(rx, ry)] = ext == ".l2j" ? Region.ParseL2j(bytes) : Region.ParseL2d(bytes);
            }
            catch (Exception ex)
            {
                ZoneLog.Info($"[geo] failed to load {file}: {ex.Message}");
            }
        }

        return geo;
    }

    /// <summary>Test/tool hook: install a region built in memory.</summary>
    internal void SetRegion(int rx, int ry, Region region) => _regions[Key(rx, ry)] = region;

    static int Key(int rx, int ry) => (rx << 8) | ry;

    static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    public static int GeoX(int worldX) => FloorDiv(worldX, CellSize);
    public static int GeoY(int worldY) => FloorDiv(worldY, CellSize);
    public static int WorldX(int geoX) => geoX * CellSize + CellSize / 2;
    public static int WorldY(int geoY) => geoY * CellSize + CellSize / 2;

    Region? RegionAtGeo(int gx, int gy, out int lx, out int ly)
    {
        int rx = FloorDiv(gx, RegionCells) + TileZeroX;
        int ry = FloorDiv(gy, RegionCells) + TileZeroY;
        lx = gx - (rx - TileZeroX) * RegionCells;
        ly = gy - (ry - TileZeroY) * RegionCells;
        return _regions.TryGetValue(Key(rx, ry), out var r) ? r : null;
    }

    public bool HasGeo(int worldX, int worldY) => RegionAtGeo(GeoX(worldX), GeoY(worldY), out _, out _) != null;

    /// <summary>Ground Z of the layer nearest to <paramref name="worldZ"/> (Mobius getHeight/getNearestZ). Returns worldZ when no geo.</summary>
    public int GetHeight(int worldX, int worldY, int worldZ)
    {
        var r = RegionAtGeo(GeoX(worldX), GeoY(worldY), out int lx, out int ly);
        if (r == null)
            return worldZ;
        r.Nearest(lx, ly, worldZ, out short h, out _);
        return h;
    }

    bool CellAt(int gx, int gy, int z, out int h, out byte nswe)
    {
        var r = RegionAtGeo(gx, gy, out int lx, out int ly);
        if (r == null)
        {
            h = z;
            nswe = All;
            return false;
        }

        r.Nearest(lx, ly, z, out short hh, out nswe);
        h = hh;
        return true;
    }
}
