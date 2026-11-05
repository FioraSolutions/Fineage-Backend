namespace Fineage.Zone.Geo;

/// <summary>
/// Mobius <c>GeoEngine.findPath</c> (CellPathFinding) simplified: A* over 16-unit geo cells honouring NSWE walls and
/// <see cref="GeoEngine.MaxClimb"/>, bounded search box / node budget, then line-of-sight smoothing with
/// <see cref="GeoEngine.MoveCheck"/> so the route is a few straight waypoints (fed one by one as MoveToLocation).
/// Returns null when there is no geo or no route (caller falls back to MoveCheck truncation, like Mobius).
/// </summary>
public sealed partial class GeoEngine
{
    public const int PathMaxNodes = 40000;
    public const int PathMargin = 96; // cells (~1.5k units) of detour allowed around the origin/target box

    static readonly (int Dx, int Dy)[] Dirs = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>Single-cell step legality (same rules as MoveCheck). Outputs next cell ground Z.</summary>
    bool CanStep(int gx, int gy, int z, int nx, int ny, out int nz)
    {
        nz = z;
        if (!CellAt(gx, gy, z, out int cz, out byte cn) || !CellAt(nx, ny, cz, out nz, out _))
            return false;
        int sx = nx - gx, sy = ny - gy;
        byte need = 0;
        if (sx != 0) need |= sx > 0 ? East : West;
        if (sy != 0) need |= sy > 0 ? South : North;
        if ((cn & need) != need || nz - cz > MaxClimb)
            return false;
        if (sx != 0 && sy != 0)
        {
            return CellAt(nx, gy, cz, out _, out _) && CellAt(gx, ny, cz, out _, out _)
                   && Passable(nx, gy, cz, sy > 0 ? South : North) && Passable(gx, ny, cz, sx > 0 ? East : West);
        }

        return true;
    }

    public List<(int X, int Y, int Z)>? FindPath(int ox, int oy, int oz, int tx, int ty, int tz)
    {
        int sgx = GeoX(ox), sgy = GeoY(oy), tgx = GeoX(tx), tgy = GeoY(ty);
        if (!CellAt(sgx, sgy, oz, out int sz, out _) || !CellAt(tgx, tgy, tz, out _, out _))
            return null;
        int minX = Math.Min(sgx, tgx) - PathMargin, maxX = Math.Max(sgx, tgx) + PathMargin;
        int minY = Math.Min(sgy, tgy) - PathMargin, maxY = Math.Max(sgy, tgy) + PathMargin;

        var open = new PriorityQueue<(int X, int Y), int>();
        var g = new Dictionary<(int, int), int>();
        var parent = new Dictionary<(int, int), (int, int)>();
        var zAt = new Dictionary<(int, int), int>();
        var closed = new HashSet<(int, int)>();
        var start = (sgx, sgy);
        g[start] = 0;
        zAt[start] = sz;
        open.Enqueue(start, Heur(sgx, sgy, tgx, tgy));
        bool found = false;
        while (open.Count > 0 && closed.Count < PathMaxNodes)
        {
            var cur = open.Dequeue();
            if (!closed.Add(cur))
                continue;
            if (cur.X == tgx && cur.Y == tgy)
            {
                found = true;
                break;
            }

            int cg = g[cur], cz = zAt[cur];
            foreach (var (dx, dy) in Dirs)
            {
                int nx = cur.X + dx, ny = cur.Y + dy;
                if (nx < minX || nx > maxX || ny < minY || ny > maxY)
                    continue;
                var n = (nx, ny);
                if (closed.Contains(n) || !CanStep(cur.X, cur.Y, cz, nx, ny, out int nz))
                    continue;
                int ng = cg + (dx != 0 && dy != 0 ? 14 : 10);
                if (g.TryGetValue(n, out int old) && old <= ng)
                    continue;
                g[n] = ng;
                zAt[n] = nz;
                parent[n] = cur;
                open.Enqueue(n, ng + Heur(nx, ny, tgx, tgy));
            }
        }

        if (!found)
            return null;

        var cells = new List<(int X, int Y)>();
        for (var c = (tgx, tgy); ; c = parent[c])
        {
            cells.Add(c);
            if (c == start)
                break;
        }

        cells.Reverse();

        // LOS smoothing: from the current anchor jump to the farthest cell reachable by a straight MoveCheck.
        var result = new List<(int X, int Y, int Z)>();
        int ax = ox, ay = oy, az = oz, i = 0;
        while (i < cells.Count - 1)
        {
            int best = i + 1;
            for (int j = cells.Count - 1; j > i + 1; j--)
            {
                var (jx, jy) = Pt(cells[j], j == cells.Count - 1, tx, ty);
                MoveCheck(ax, ay, az, jx, jy, zAt[cells[j]], out bool b);
                if (!b) { best = j; break; }
            }

            var (wx, wy) = Pt(cells[best], best == cells.Count - 1, tx, ty);
            int wz = zAt[cells[best]];
            result.Add((wx, wy, wz));
            ax = wx; ay = wy; az = wz; i = best;
        }

        if (result.Count == 0)
            result.Add((tx, ty, zAt[(tgx, tgy)]));
        return result;
    }

    static (int, int) Pt((int X, int Y) c, bool last, int tx, int ty) => last ? (tx, ty) : (WorldX(c.X), WorldY(c.Y));

    static int Heur(int x, int y, int tx, int ty)
    {
        int dx = Math.Abs(tx - x), dy = Math.Abs(ty - y);
        return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy);
    }
}
