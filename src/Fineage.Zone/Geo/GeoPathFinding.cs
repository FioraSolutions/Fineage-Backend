namespace Fineage.Zone.Geo;

/// <summary>
/// Mobius <c>GeoEngine.findPath</c> (CellPathFinding) simplified: A* over 16-unit geo cells honouring NSWE walls and
/// <see cref="GeoEngine.MaxClimb"/>, bounded search box / node budget, then line-of-sight smoothing with
/// <see cref="GeoEngine.MoveCheck"/> so the route is a few straight waypoints (fed one by one as MoveToLocation).
/// Returns null when there is no geo or no route (caller falls back to MoveCheck truncation, like Mobius).
/// </summary>
public sealed partial class GeoEngine
{
    /// <summary>
    /// Expanded-node cap of one search: Mobius <c>CellNodeBuffer.MAX_ITERATIONS</c> (3500). A search that needs more
    /// gives up and the mover falls back to the truncated straight line, exactly like the original.
    /// </summary>
    public const int PathMaxNodes = 3500;
    public const int PathMargin = 96; // cells (~1.5k units) of detour allowed around the origin/target box

    /// <summary>Starts a resumable route search; <see cref="GeoPathSearch.Step"/> spends the caller's node budget.</summary>
    public GeoPathSearch BeginPath(int ox, int oy, int oz, int tx, int ty, int tz, int? maxNodes = null) =>
        new(this, ox, oy, oz, tx, ty, tz, maxNodes);

    /// <summary>Single-cell step legality (same rules as MoveCheck). Outputs next cell ground Z.</summary>
    internal bool CanStep(int gx, int gy, int z, int nx, int ny, out int nz)
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
        if (!CellAt(GeoX(ox), GeoY(oy), oz, out _, out _) || !CellAt(GeoX(tx), GeoY(ty), tz, out _, out _))
            return null;

        var search = BeginPath(ox, oy, oz, tx, ty, tz);
        while (search.Step(int.MaxValue) == GeoPathSearch.Status.Running)
        {
        }

        return search.BuildPath();
    }
}
