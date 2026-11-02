namespace Fineage.Zone.Geo;

/// <summary>
/// Server-side L2 geodata (Mobius <c>GeoEngine</c>, simplified). Loads retail-style region files
/// <c>XX_YY.l2j</c> (L2J/Mobius format) or <c>XX_YY.l2d</c> (aCis/L2D format) from <c>data/geodata</c>
/// (or <c>FINEAGE_GEODATA_DIR</c>). A region is 32768×32768 world units = 256×256 blocks of 8×8 cells, 16 units per cell.
/// Where a region file is missing, <see cref="HasGeo"/> is false and callers keep their non-geo fallback.
/// </summary>
public sealed class GeoEngine
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

    /// <summary>
    /// Mobius <c>getValidLocation</c>/<c>canMoveToTarget</c>: walk the geo cells from origin toward dest; stop at the last
    /// cell before a wall (missing NSWE direction) or a climb above <see cref="MaxClimb"/>. Returns the reachable point with
    /// ground Z. Cells without geo are passed through (Z kept). <paramref name="blocked"/> is true when truncated.
    /// </summary>
    public (int X, int Y, int Z) MoveCheck(int ox, int oy, int oz, int tx, int ty, int tz, out bool blocked)
    {
        blocked = false;
        int gx = GeoX(ox), gy = GeoY(oy);
        int gtx = GeoX(tx), gty = GeoY(ty);
        bool hasCur = CellAt(gx, gy, oz, out int curZ, out byte curNswe);
        if (!hasCur)
            curZ = oz;
        if (gx == gtx && gy == gty)
            return (tx, ty, hasCur ? curZ : tz);

        int dx = Math.Abs(gtx - gx), dy = Math.Abs(gty - gy);
        int sx = gtx > gx ? 1 : -1, sy = gty > gy ? 1 : -1;
        int err = dx - dy;
        int lastX = ox, lastY = oy;
        while (gx != gtx || gy != gty)
        {
            int e2 = err * 2;
            int nx = gx, ny = gy;
            if (e2 > -dy) { err -= dy; nx += sx; }
            if (e2 < dx) { err += dx; ny += sy; }

            bool hasNext = CellAt(nx, ny, curZ, out int nextZ, out byte nextNswe);
            if (hasCur && hasNext)
            {
                byte need = 0;
                if (nx != gx) need |= sx > 0 ? East : West;
                if (ny != gy) need |= sy > 0 ? South : North;
                bool ok = (curNswe & need) == need && nextZ - curZ <= MaxClimb;
                if (ok && nx != gx && ny != gy)
                {
                    // Diagonal: both orthogonal neighbours must be passable too (no corner-cutting through walls).
                    ok = CellAt(nx, gy, curZ, out _, out _) && CellAt(gx, ny, curZ, out _, out _)
                         && Passable(nx, gy, curZ, sy > 0 ? South : North) && Passable(gx, ny, curZ, sx > 0 ? East : West);
                }

                if (!ok)
                {
                    blocked = true;
                    return (lastX, lastY, curZ);
                }
            }

            gx = nx;
            gy = ny;
            hasCur = hasNext;
            curNswe = nextNswe;
            if (hasNext)
                curZ = nextZ;
            lastX = WorldX(gx);
            lastY = WorldY(gy);
        }

        return (tx, ty, hasCur ? curZ : tz);
    }

    bool Passable(int gx, int gy, int z, byte dir)
    {
        CellAt(gx, gy, z, out _, out byte n);
        return (n & dir) != 0;
    }

    /// <summary>One 2048×2048-cell region. Flat blocks share one height; complex = 64 cells; multilayer = per-cell layers.</summary>
    internal sealed class Region
    {
        // Per block: offset into _cells (packed short: height<<4 | nswe like L2J) and layer index for multilayer.
        readonly byte[] _type = new byte[RegionBlocks * RegionBlocks];
        readonly int[] _offset = new int[RegionBlocks * RegionBlocks];
        readonly List<short> _data = [];
        short[] _arr = [];

        const byte Flat = 0, Complex = 1, Multi = 2;

        internal static Region Flat1(short height)
        {
            var r = new Region();
            for (int i = 0; i < r._type.Length; i++)
            {
                r._type[i] = Flat;
                r._offset[i] = 0;
            }

            r._data.Add(height);
            r._arr = [.. r._data];
            return r;
        }

        /// <summary>Test hook: overwrite one block as complex with given per-cell (height, nswe).</summary>
        internal void SetComplexBlock(int bx, int by, Func<int, int, (short H, byte Nswe)> cell)
        {
            int b = bx * RegionBlocks + by;
            _type[b] = Complex;
            var list = new List<short>(_arr);
            _offset[b] = list.Count;
            for (int cx = 0; cx < 8; cx++)
            for (int cy = 0; cy < 8; cy++)
            {
                var (h, n) = cell(cx, cy);
                list.Add(Pack(h, n));
            }

            _arr = [.. list];
        }

        static short Pack(short h, byte nswe) => (short)(((h << 1) & 0xFFF0) | (nswe & 0x0F));

        internal static Region ParseL2j(byte[] b)
        {
            var r = new Region();
            int p = 0;
            for (int bx = 0; bx < RegionBlocks; bx++)
            for (int by = 0; by < RegionBlocks; by++)
            {
                int idx = bx * RegionBlocks + by;
                byte t = b[p++];
                r._type[idx] = t;
                r._offset[idx] = r._data.Count;
                switch (t)
                {
                    case Flat:
                        r._data.Add(BitConverter.ToInt16(b, p));
                        p += 2;
                        break;
                    case Complex:
                        for (int i = 0; i < 64; i++, p += 2)
                            r._data.Add(BitConverter.ToInt16(b, p));
                        break;
                    case Multi:
                        for (int i = 0; i < 64; i++)
                        {
                            byte layers = b[p++];
                            r._data.Add(layers);
                            for (int l = 0; l < layers; l++, p += 2)
                                r._data.Add(BitConverter.ToInt16(b, p));
                        }

                        break;
                    default:
                        throw new InvalidDataException($"bad l2j block type {t} at {p - 1}");
                }
            }

            r._arr = [.. r._data];
            r._data.Clear();
            return r;
        }

        /// <summary>aCis L2D: 0xD0 flat(short h), 0xD1 complex(64×[byte nswe, short h]), 0xD2 multi(64×[byte n, n×[byte nswe, short h]]).</summary>
        internal static Region ParseL2d(byte[] b)
        {
            var r = new Region();
            int p = 0;
            for (int bx = 0; bx < RegionBlocks; bx++)
            for (int by = 0; by < RegionBlocks; by++)
            {
                int idx = bx * RegionBlocks + by;
                byte t = b[p++];
                r._offset[idx] = r._data.Count;
                switch (t)
                {
                    case 0xD0:
                        r._type[idx] = Flat;
                        r._data.Add(BitConverter.ToInt16(b, p));
                        p += 2;
                        break;
                    case 0xD1:
                        r._type[idx] = Complex;
                        for (int i = 0; i < 64; i++)
                        {
                            byte n = b[p++];
                            short h = BitConverter.ToInt16(b, p);
                            p += 2;
                            r._data.Add(Pack(h, n));
                        }

                        break;
                    case 0xD2:
                        r._type[idx] = Multi;
                        for (int i = 0; i < 64; i++)
                        {
                            byte layers = b[p++];
                            r._data.Add(layers);
                            for (int l = 0; l < layers; l++)
                            {
                                byte n = b[p++];
                                short h = BitConverter.ToInt16(b, p);
                                p += 2;
                                r._data.Add(Pack(h, n));
                            }
                        }

                        break;
                    default:
                        throw new InvalidDataException($"bad l2d block type 0x{t:X2} at {p - 1}");
                }
            }

            r._arr = [.. r._data];
            r._data.Clear();
            return r;
        }

        static short Height(short packed) => (short)((short)(packed & 0xFFF0) >> 1);

        internal void Nearest(int lx, int ly, int z, out short height, out byte nswe)
        {
            int b = (lx >> 3) * RegionBlocks + (ly >> 3);
            int off = _offset[b];
            int cell = ((lx & 7) << 3) + (ly & 7);
            switch (_type[b])
            {
                case Flat:
                    height = _arr[off];
                    nswe = All;
                    return;
                case Complex:
                {
                    short v = _arr[off + cell];
                    height = Height(v);
                    nswe = (byte)(v & 0x0F);
                    return;
                }
                default:
                {
                    int p = off;
                    for (int i = 0; i < cell; i++)
                        p += 1 + _arr[p];
                    int layers = _arr[p++];
                    height = (short)z;
                    nswe = All;
                    int best = int.MaxValue;
                    for (int l = 0; l < layers; l++)
                    {
                        short v = _arr[p + l];
                        short h = Height(v);
                        int d = Math.Abs(h - z);
                        if (d < best)
                        {
                            best = d;
                            height = h;
                            nswe = (byte)(v & 0x0F);
                        }
                    }

                    return;
                }
            }
        }
    }
}
