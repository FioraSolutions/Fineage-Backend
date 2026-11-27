namespace Fineage.Zone.Geo;

/// <summary>
/// One A* route search over geo cells that stops between node expansions and resumes on a later tick.
/// Mobius (<c>CellNodeBuffer.findPath</c>) finishes a search inside <c>moveToLocation</c>; Fineage moves the same
/// search off the movement stages, so a single tick must not pay for a whole search. The budget is counted in expanded
/// nodes, not wall time, so the outcome and the tick cost stay reproducible run to run.
/// Cell rules are the ones <see cref="GeoEngine.MoveCheck"/> uses, so a route found here is walkable by the mover.
///
/// Line-of-sight smoothing after the search is resumable too (<see cref="TrySmooth"/>): it accepts the farthest cell
/// reachable by one straight <see cref="GeoEngine.MoveCheck"/> and costs one probe walk per attempt. Keeping it resumable
/// means a tight per-step budget never has to trade route quality for a time slice.
/// </summary>
public sealed class GeoPathSearch
{
    public enum Status
    {
        /// <summary>The budget ran out with nodes still open; call <see cref="Step"/> again.</summary>
        Running,
        Found,
        /// <summary>No route: the open set emptied.</summary>
        NoRoute,
        /// <summary>The node cap was reached; treated as "no route", like Mobius running out of iterations.</summary>
        Exhausted,
    }

    static readonly (int Dx, int Dy)[] Dirs = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    readonly GeoEngine _geo;
    readonly DoorTable? _doors;
    readonly int _ox, _oy, _oz, _tx, _ty, _tz;
    readonly int _sgx, _sgy, _tgx, _tgy;
    readonly int _minX, _maxX, _minY, _maxY;
    readonly int _maxNodes;

    readonly PriorityQueue<(int X, int Y), int> _open = new();
    readonly Dictionary<(int X, int Y), int> _g = [];
    readonly Dictionary<(int X, int Y), (int X, int Y)> _parent = [];
    readonly Dictionary<(int X, int Y), int> _zAt = [];
    readonly HashSet<(int X, int Y)> _closed = [];

    List<(int X, int Y)>? _cells;
    int _smoothIndex;
    int _anchorX, _anchorY, _anchorZ;

    Status _status = Status.Running;

    internal GeoPathSearch(GeoEngine geo, int ox, int oy, int oz, int tx, int ty, int tz, int? maxNodes = null, DoorTable? doors = null)
    {
        ArgumentNullException.ThrowIfNull(geo);
        _geo = geo;
        _ox = ox;
        _oy = oy;
        _oz = oz;
        _tx = tx;
        _ty = ty;
        _tz = tz;
        _maxNodes = maxNodes ?? GeoEngine.PathMaxNodes;
        _sgx = GeoEngine.GeoX(ox);
        _sgy = GeoEngine.GeoY(oy);
        _tgx = GeoEngine.GeoX(tx);
        _tgy = GeoEngine.GeoY(ty);
        _minX = Math.Min(_sgx, _tgx) - GeoEngine.PathMargin;
        _maxX = Math.Max(_sgx, _tgx) + GeoEngine.PathMargin;
        _minY = Math.Min(_sgy, _tgy) - GeoEngine.PathMargin;
        _maxY = Math.Max(_sgy, _tgy) + GeoEngine.PathMargin;

        // Door-aware only where a closed door can matter: no closed door in the search box keeps the hot CanStep
        // path free of the door index.
        _doors = doors != null &&
                 doors.AnyClosedIn(_minX * GeoEngine.CellSize, _minY * GeoEngine.CellSize,
                     _maxX * GeoEngine.CellSize, _maxY * GeoEngine.CellSize)
            ? doors
            : null;

        _g[(_sgx, _sgy)] = 0;
        _zAt[(_sgx, _sgy)] = oz;
        _open.Enqueue((_sgx, _sgy), Heur(_sgx, _sgy, _tgx, _tgy));
    }

    public Status Current => _status;

    /// <summary>Expansions done so far, for telemetry and for tests that assert the work is spread over ticks.</summary>
    public int Expansions { get; private set; }

    /// <summary>Straight-leg cells the smoothing has walked so far, across all steps.</summary>
    public int SmoothingCellsWalked { get; private set; }

    /// <summary>Advance the search by at most <paramref name="maxExpansions"/> closed nodes.</summary>
    public Status Step(int maxExpansions)
    {
        if (_status != Status.Running || maxExpansions <= 0)
            return _status;

        int spent = 0;
        while (_open.Count > 0)
        {
            if (_closed.Count >= _maxNodes)
            {
                _status = Status.Exhausted;
                return _status;
            }

            if (spent >= maxExpansions)
                return _status;

            var cur = _open.Dequeue();
            if (!_closed.Add(cur))
                continue;
            spent++;
            Expansions++;

            if (cur.X == _tgx && cur.Y == _tgy)
            {
                _status = Status.Found;
                return _status;
            }

            int cg = _g[cur], cz = _zAt[cur];
            foreach (var (dx, dy) in Dirs)
            {
                int nx = cur.X + dx, ny = cur.Y + dy;
                if (nx < _minX || nx > _maxX || ny < _minY || ny > _maxY)
                    continue;
                var n = (nx, ny);
                if (_closed.Contains(n) || !_geo.CanStep(cur.X, cur.Y, cz, nx, ny, out int nz, _doors))
                    continue;
                int ng = cg + (dx != 0 && dy != 0 ? 14 : 10);
                if (_g.TryGetValue(n, out int old) && old <= ng)
                    continue;
                _g[n] = ng;
                _zAt[n] = nz;
                _parent[n] = cur;
                _open.Enqueue(n, ng + Heur(nx, ny, _tgx, _tgy));
            }
        }

        _status = Status.NoRoute;
        return _status;
    }

    /// <summary>
    /// Appends the found route's straight walkable legs to <paramref name="appendTo"/>, staying inside
    /// <paramref name="maxCellsWalked"/> probe cells for this call. Returns true when the route is complete, false when the
    /// caller must resume.
    ///
    /// The farthest cell the slice can afford is picked once per call (arithmetic only, no raycast), so the raycasts land
    /// where they collapse the most route and the call never overruns its slice. A leg that cannot afford any probe falls
    /// back to the adjacent A* cell (walkable by construction) and ends the call: the anchor strictly advances, so a
    /// resuming caller — the <c>npc.paths</c> stage or <see cref="BuildPath"/> — never spins.
    /// </summary>
    public bool TrySmooth(int maxCellsWalked, List<(int X, int Y, int Z)> appendTo)
    {
        ArgumentNullException.ThrowIfNull(appendTo);
        if (_status != Status.Found)
            return false;

        _cells ??= ReconstructCells();
        if (_smoothIndex == 0)
        {
            _anchorX = _ox;
            _anchorY = _oy;
            _anchorZ = _oz;
        }

        int budget = Math.Max(0, maxCellsWalked);
        int last = _cells.Count - 1;

        // Farthest cell this slice can reach from the call's anchor. Scanning down is arithmetic only, so it costs
        // nothing from the probe budget.
        int cap = last;
        while (cap > _smoothIndex + 1 && ProbeCost(_anchorX, _anchorY, cap) > budget)
            cap--;

        while (_smoothIndex < last)
        {
            int best = _smoothIndex + 1;
            int probed = 0;
            for (int j = cap; j > _smoothIndex + 1; j--)
            {
                int cost = ProbeCost(_anchorX, _anchorY, j);
                if (cost > budget)
                    break;

                budget -= cost;
                probed++;
                SmoothingCellsWalked += cost;
                var (jx, jy) = Pt(_cells[j], j == last);
                _geo.MoveCheck(_anchorX, _anchorY, _anchorZ, jx, jy, _zAt[_cells[j]], out bool blocked, _doors);
                if (!blocked)
                {
                    best = j;
                    break;
                }
            }

            var (wx, wy) = Pt(_cells[best], best == last);
            int wz = _zAt[_cells[best]];
            appendTo.Add((wx, wy, wz));
            _anchorX = wx;
            _anchorY = wy;
            _anchorZ = wz;
            _smoothIndex = best;

            if (probed == 0 && _smoothIndex < last)
            {
                // No probe fit in the slice: the adjacent A* cell carried the leg. Resume from there, never spin.
                return false;
            }
        }

        if (appendTo.Count == 0)
            appendTo.Add((_tx, _ty, _zAt[(_tgx, _tgy)]));
        return true;
    }

    /// <summary>One-shot convenience for callers that can afford a whole route on one thread.</summary>
    public List<(int X, int Y, int Z)>? BuildPath()
    {
        if (_status != Status.Found)
            return null;
        var result = new List<(int X, int Y, int Z)>();
        while (!TrySmooth(int.MaxValue, result))
        {
        }

        return result;
    }

    List<(int X, int Y)> ReconstructCells()
    {
        var cells = new List<(int X, int Y)>();
        var start = (_sgx, _sgy);
        for (var c = (_tgx, _tgy); ; c = _parent[c])
        {
            cells.Add(c);
            if (c == start)
                break;
        }

        cells.Reverse();
        return cells;
    }

    /// <summary>Cells a straight <see cref="GeoEngine.MoveCheck"/> from the anchor to <paramref name="index"/> walks.</summary>
    int ProbeCost(int anchorX, int anchorY, int index)
    {
        var (x, y) = Pt(_cells![index], index == _cells.Count - 1);
        return Math.Max(Math.Abs(GeoEngine.GeoX(x) - GeoEngine.GeoX(anchorX)),
            Math.Abs(GeoEngine.GeoY(y) - GeoEngine.GeoY(anchorY))) + 1;
    }

    (int, int) Pt((int X, int Y) c, bool last) => last ? (_tx, _ty) : (GeoEngine.WorldX(c.X), GeoEngine.WorldY(c.Y));

    static int Heur(int x, int y, int tx, int ty)
    {
        int dx = Math.Abs(tx - x), dy = Math.Abs(ty - y);
        return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy);
    }
}
