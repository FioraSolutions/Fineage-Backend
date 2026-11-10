namespace Fineage.Zone.Geo;

public sealed partial class GeoEngine
{
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
}
