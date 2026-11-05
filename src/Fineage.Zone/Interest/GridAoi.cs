namespace Fineage.Zone.Interest;

/// <summary>
/// Grid spatial hash AOI (ARCHITECTURE_NET Phase 1). Watch = 3×3 cells; forget = 5×5.
/// </summary>
public sealed class GridAoi : IAoi
{
    public const int DefaultCellSize = 3000;

    readonly int _cellSize;
    readonly Dictionary<int, long> _entityCell = [];
    readonly Dictionary<long, HashSet<int>> _cells = [];

    public GridAoi(int cellSize = DefaultCellSize)
    {
        if (cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
        _cellSize = cellSize;
    }

    public int CellSize => _cellSize;

    public void Enter(int objectId, int x, int y)
    {
        Leave(objectId);
        long key = CellKey(x, y);
        _entityCell[objectId] = key;
        if (!_cells.TryGetValue(key, out var set))
        {
            set = [];
            _cells[key] = set;
        }

        set.Add(objectId);
    }

    public void Move(int objectId, int x, int y)
    {
        long key = CellKey(x, y);
        if (_entityCell.TryGetValue(objectId, out long prev) && prev == key)
            return;
        Enter(objectId, x, y);
    }

    public void Leave(int objectId)
    {
        if (!_entityCell.Remove(objectId, out long key))
            return;
        if (!_cells.TryGetValue(key, out var set))
            return;
        set.Remove(objectId);
        if (set.Count == 0)
            _cells.Remove(key);
    }

    public IReadOnlyCollection<int> GetVisible(int objectId)
    {
        if (!_entityCell.TryGetValue(objectId, out long key))
            return [];

        Unpack(key, out int gx, out int gy);
        var visible = new List<int>();
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!_cells.TryGetValue(Pack(gx + dx, gy + dy), out var set))
                    continue;
                foreach (int id in set)
                {
                    if (id != objectId)
                        visible.Add(id);
                }
            }
        }

        return visible;
    }

    public bool StillKnown(int objectId, int otherId)
    {
        if (!_entityCell.TryGetValue(objectId, out long a) || !_entityCell.TryGetValue(otherId, out long b))
            return false;
        Unpack(a, out int ax, out int ay);
        Unpack(b, out int bx, out int by);
        return Math.Abs(ax - bx) <= 2 && Math.Abs(ay - by) <= 2;
    }

    long CellKey(int x, int y) => Pack(FloorDiv(x, _cellSize), FloorDiv(y, _cellSize));

    static long Pack(int gx, int gy) => ((long)gx << 32) ^ (uint)gy;

    static void Unpack(long key, out int gx, out int gy)
    {
        gx = (int)(key >> 32);
        gy = (int)(uint)key;
    }

    static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        if (value < 0 && value % divisor != 0)
            q--;
        return q;
    }
}
